using KiloExtensionDTOs;
using KiloExtensionDTOs.ExtensionMessages;
using KiloVisualStudioExtension.ApiClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PermissionResponse = KiloVisualStudioExtension.ApiClient.Body13Reply;
using PermissionReplyRequestBody = KiloVisualStudioExtension.ApiClient.Body13;
using KiloVisualStudioExtension.Utils;
using Microsoft.VisualStudio.Utilities;

namespace KiloVisualStudioExtension.Services
{
  public class PermissionService : ServiceProviderServiceBase
  {
    public class PermissionContext
    {
      public KiloApiClient? Client { get; set; }
      public string? CurrentSessionId { get; set; }
      public HashSet<string> TrackedSessionIds { get; set; } = new();
      public Dictionary<string, string> SessionDirectories { get; set; } = new();
      public Func<string[]>? ExtraDirectories { get; set; }
      public Action<IWebviewMessage?> PostMessage { get; set; } = _ => { };
      public Func<string?, string> GetWorkspaceDirectory { get; set; } = _ => "";
      public Action<string, string> RecordPermissionDirectory { get; set; } = (_, _) => { };
      public Func<string, string?> GetPermissionDirectory { get; set; } = _ => null;
      public Action<string> ClearPermissionDirectory { get; set; } = _ => { };
      public Action<HashSet<string>, HashSet<string>?> PrunePermissionDirectories { get; set; } = (_, _) => { };
    }

    public PermissionService(ServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    private VSProvider Provider => _serviceProvider.GetService<VSProvider>()
        ?? throw new InvalidOperationException("VSProvider not registered in service provider");

    public static string[] RecoveryDirs(string workspace, Dictionary<string, string> dirs, string[]? extra = null)
    {
      var set = new HashSet<string> { workspace };
      foreach (var dir in dirs.Values) set.Add(dir);
      if (extra != null)
      {
        foreach (var e in extra) set.Add(e);
      }
      return set.ToArray();
    }

    public static IEnumerable<PermissionRequest> RecoverablePermissions(
        List<PermissionRequest> perms,
        HashSet<string> tracked,
        HashSet<string> seen)
    {
      foreach (var perm in perms)
      {
        if (seen.Contains(perm.Id)) continue;
        seen.Add(perm.Id);
        if (tracked.Contains(perm.SessionID))
          yield return perm;
      }
    }

    private static bool IsNotFoundError(object? error)
    {
      if (error == null) return false;

      var obj = Record(error);
      if (obj == null) return false;

      var cause = GetProp(obj, "cause");
      var body = GetProp(cause, "body");

      var check = new[] { obj, GetProp(obj, "data"), cause, body, GetProp(body, "data") };
      return check.Any(v =>
      {
        var name = GetProp(v, "name")?.ToString();
        var status = GetProp(v, "status");
        return name == "NotFoundError" || (status != null && Convert.ToInt32(status) == 404);
      });
    }
    private static object? Record(object? value)
    {
      if (value != null && value is System.Collections.IDictionary dict)
        return dict;
      if (value != null && value.GetType().IsClass)
        return value;
      return null;
    }

    private static object? GetProp(object? obj, string name)
    {
      if (obj is System.Collections.IDictionary dict && dict.Contains(name))
        return dict[name];
      var prop = obj?.GetType().GetProperty(name);
      return prop?.GetValue(obj);
    }
    public async Task HandlePermissionResponse(
            PermissionContext ctx,
            string permissionId,
            string sessionID,
            PermissionResponse response,
            string[] approvedAlways,
            string[] deniedAlways)
    {
      if (ctx.Client == null)
      {
        ctx.PostMessage(new PermissionErrorMessage
        {
          PermissionID = permissionId
        });
        return;
      }

      var target = string.IsNullOrEmpty(sessionID) ? ctx.CurrentSessionId : sessionID;
      if (string.IsNullOrEmpty(target))
      {
        System.Diagnostics.Debug.WriteLine("[Kilo New] KiloProvider: No sessionID for permission response");
        ctx.PostMessage(new PermissionErrorMessage { PermissionID = permissionId });
        return;
      }

      var dir = ctx.GetPermissionDirectory(permissionId) ?? ctx.GetWorkspaceDirectory(target);

      void StaleCleanup()
      {
        ctx.ClearPermissionDirectory(permissionId);
        ctx.PostMessage(new PermissionErrorMessage { PermissionID = permissionId, Stale = true });
        _ = FetchAndSendPendingPermissions(ctx);
      }

      if (approvedAlways.Length > 0 || deniedAlways.Length > 0)
      {
        string saveResult;
        try
        {
          await ctx.Client.Permission_saveAlwaysRulesAsync(permissionId, dir, "", new Body14
          {
            ApprovedAlways = approvedAlways,
            DeniedAlways = deniedAlways
          });
          saveResult = "ok";
        }
        catch (Exception error)
        {
          if (IsNotFoundError(error))
          {
            saveResult = "stale";
          }
          else
          {
            System.Diagnostics.Debug.Write($"[Kilo New] KiloProvider: Failed to save always-rules: {error}");
            ctx.PostMessage(new PermissionErrorMessage { PermissionID = permissionId });
            saveResult = "error";
          }
        }


        if (saveResult == "stale")
        {
          StaleCleanup();
          return;
        }
        if (saveResult == "error") return;
      }

      string replyResult;
      try
      {
        await ctx.Client.Permission_replyAsync(
          permissionId,
          dir,
          "",
          new PermissionReplyRequestBody
          {
            Reply = response
          });
        replyResult = "ok";

      }
      catch (Exception error)
      {

        if (IsNotFoundError(error))
        {
          replyResult = "stale";
        }
        else
        {
          System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed to respond to permission: {error}");
          ctx.PostMessage(new PermissionErrorMessage { PermissionID = permissionId });
          replyResult = "error";
        }
      }

      if (replyResult == "stale")
      {
        StaleCleanup();
      }
    }

    public async Task FetchAndSendPendingPermissions(PermissionContext ctx)
    {
      if (ctx.Client == null) return;

      try
      {
        var extra = ctx.ExtraDirectories?.Invoke() ?? Array.Empty<string>();
        var dirs = RecoveryDirs(ServiceProvider.GetService<ProjectDirectoryProvider>().GetWorkspaceDirectory(), ctx.SessionDirectories, extra);

        var seen = new HashSet<string>();
        var valid = new HashSet<string>();

        foreach (var dir in dirs)
        {
          ICollection<PermissionRequest> result;
          try
          {
            result = await ctx.Client.Permission_listAsync(dir, "");
          }
          catch (Exception error)
          {
            System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed to fetch pending permissions for {dir}: {error.Message}");
            continue;
          }

          valid.Add(dir);
          if (result == null) continue;

          foreach (var perm in RecoverablePermissions(result.ToList(), ctx.TrackedSessionIds, seen))
          {
            ctx.RecordPermissionDirectory(perm.Id, dir);
            ctx.PostMessage(new PermissionRequestMessage
            {
              Permission = new KiloExtensionDTOs.Permissions.PermissionRequest
              {
                Id = perm.Id,
                SessionID = perm.SessionID,
                ToolName = perm.Permission,
                Patterns = perm.Patterns.ToList(),
                Always = perm.Always.ToList(),
                Args = perm.Metadata,
                Message = $"Permission required: {perm.Permission}",
                Tool = EntityConverter.Convert(perm.Tool)
              }
            });
          }
        }

        ctx.PrunePermissionDirectories(seen, valid);
      }
      catch (Exception error)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed to fetch pending permissions: {error.Message}");
      }
    }
  }
}
