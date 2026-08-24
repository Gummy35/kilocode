using System;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KiloExtensionDTOs.ExtensionMessages;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Utils;
using ProfileResponse = KiloVisualStudioExtension.ApiClient.Response23;

namespace KiloVisualStudioExtension.Services.Handlers.Auth
{
  public class AuthHandlerService : ServiceProviderServiceBase
  {
    private bool _disposed;

    private VSProvider Provider => _serviceProvider.GetService<VSProvider>()
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

    public AuthHandlerService(ServiceProvider serviceProvider) : base(serviceProvider)
    {
    }


    /// <summary>
    /// Handles the login message from the webview.
    /// Initiates the login flow with the backend by fetching the user profile.
    /// 
    /// VS Code workflow: Matches the pattern in kilo-provider/handlers/auth.ts where
    /// handleLogin fetches /kilo/profile and sends profileData to the webview.
    /// 
    /// Workflow steps:
    /// 1. Check if HTTP client is connected to backend
    /// 2. Fetch user profile from /kilo/profile endpoint
    /// 3. Extract profile property from response
    /// 4. Send profileData message to webview with cloned profile data
    /// 
    /// Messages sent to webview:
    /// - profileData: { profile: { email, name, id, ... } }
    /// - error: { message: "Not connected to backend" }
    /// </summary>
    /// <param name="payload">The message payload (unused for login).</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleLoginAsync(JsonElement? payload)
    {
      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null)
      {
        await Provider.SendErrorAsync("Not connected", "Not connected to backend");
        return;
      }

      var directory = Provider.GetWorkspaceDirectory();
      var attempt = Provider.GetLoginAttempt();

      try
      {
        var auth = await nswagClient.Provider_oauth_authorizeAsync("kilo", directory, "", new Body16
        {
          Method = 0,
          Inputs = new System.Collections.Generic.Dictionary<string, string> { { "providerID", "kilo" } }
        });

        var match = Regex.Match(auth.Instructions ?? "", @"code:\s*(\S+)", RegexOptions.IgnoreCase);
        var code = match.Success ? match.Groups[1].Value.ToUpperInvariant() : "";

        Provider.PostMessage(new DeviceAuthStartedMessage
        {
          Code = code,
          VerificationUrl = auth.Url,
          ExpiresIn = 900
        });

        await WaitForOAuthCallback(nswagClient, directory, attempt);

        if (attempt != Provider.GetLoginAttempt()) return;

        await Provider.DisposeGlobal();

        var profile = await nswagClient.Kilo_profileAsync(directory, "");
        await Provider.SendProfileDataAsync(profile != null ? EntityConverter.Convert(profile) : null);
        Provider.PostMessage(new DeviceAuthCompleteMessage());

        await Provider.FetchAndSendProviders();
      }
      catch (OperationCanceledException)
      {
        if (attempt != Provider.GetLoginAttempt()) return;
        Provider.PostMessage(new DeviceAuthFailedMessage { Error = "Login cancelled by user" });
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] AuthHandler: login error: {ex.Message}");
        if (attempt != Provider.GetLoginAttempt()) return;
        Provider.PostMessage(new DeviceAuthFailedMessage { Error = ex.Message });
      }
    }

    private async Task WaitForOAuthCallback(KiloApiClient client, string directory, int expectedAttempt)
    {
      var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
      while (!cts.Token.IsCancellationRequested && expectedAttempt == Provider.GetLoginAttempt())
      {
        try
        {
          await client.Provider_oauth_callbackAsync("kilo", directory, "", new Body17());
          return;
        }
        catch (ApiException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
        {
          await Task.Delay(2000, cts.Token);
        }
      }
    }

    /// <summary>
    /// Handles the refreshProfile message from the webview.
    /// Refreshes the user profile data from the backend and sends it to the webview.
    /// 
    /// VS Code workflow: Matches the pattern in kilo-provider/handlers/auth.ts where
    /// handleRefreshProfile fetches /kilo/profile and posts profileData to webview.
    /// 
    /// Workflow steps:
    /// 1. Get HTTP client from provider
    /// 2. Verify client is connected
    /// 3. Fetch user profile from /kilo/profile endpoint
    /// 4. Construct message with type "profileData" and profile data
    /// 5. Post message to webview via provider
    /// 
    /// Messages sent to webview:
    /// - profileData: { data: { email, name, id, ... } }
    /// </summary>
    /// <param name="payload">The message payload (unused for refreshProfile).</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleRefreshProfileAsync(JsonElement? payload)
    {
      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null) return;
      try
      {
        var profile = await nswagClient.Kilo_profileAsync(Provider.GetWorkspaceDirectory(), "");
        await Provider.SendProfileDataAsync(profile != null ? EntityConverter.Convert(profile) : null);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] AuthHandler: error refreshing profile: {ex.Message}");
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = ex.Message }));
      }
    }

    public async Task HandleLogoutAsync(JsonElement? payload)
    {
      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null) return;

      try
      {
        await nswagClient.Auth_removeAsync("kilo");
        await Provider.DisposeGlobal();
        Provider.PostMessage(new ProfileDataMessage { Data = null });
        await Provider.FetchAndSendProviders();
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] AuthHandler: logout error: {ex.Message}");
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = ex.Message }));
      }
    }

    public async Task HandleSetOrganizationAsync(JsonElement? payload)
    {
      if (!payload.HasValue) return;

      var orgId = payload.Value.TryGetProperty("organizationId", out var org)
          ? (org.ValueKind == JsonValueKind.Null ? null : org.GetString())
          : null;

      var directory = Provider.GetWorkspaceDirectory();
      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null) return;

      try
      {
        var orgBody = new Body51();
        if (orgId != null)
        {
          orgBody.OrganizationId.AdditionalProperties["organizationId"] = orgId;
        }
        await nswagClient.Kilo_organization_setAsync(directory, "", orgBody);
        await Provider.DisposeGlobal();
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] AuthHandler: org switch error: {ex.Message}");
        try
        {
          var profile = await nswagClient.Kilo_profileAsync(Provider.GetWorkspaceDirectory(), "");
          await Provider.SendProfileDataAsync(profile != null ? EntityConverter.Convert(profile) : null);
        }
        catch { }
        return;
      }

      try
      {
        var profile = await nswagClient.Kilo_profileAsync(Provider.GetWorkspaceDirectory(), "");
        await Provider.SendProfileDataAsync(profile != null ? EntityConverter.Convert(profile) : null);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] AuthHandler: error refreshing profile after org switch: {ex.Message}");
      }
      try
      {
        await Provider.FetchAndSendProviders();
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] AuthHandler: error refreshing providers after org switch: {ex.Message}");
      }
      try
      {
        await Provider.FetchAndSendAgents();
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] AuthHandler: error refreshing agents after org switch: {ex.Message}");
      }
    }

    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
    }
  }
}

