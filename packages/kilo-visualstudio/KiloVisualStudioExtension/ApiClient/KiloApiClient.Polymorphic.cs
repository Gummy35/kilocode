using Newtonsoft.Json;
using static KiloVisualStudioExtension.Services.MessagePageFetcher;

namespace KiloVisualStudioExtension.ApiClient
{
  /// <summary>
  /// Partial class implementation for NSwag serializer settings customization.
  /// Provides a shared settings mechanism that both HTTP API and SSE can use.
  /// </summary>
  public partial class KiloApiClient
  {
    /// <summary>
    /// NSwag extension point for customizing JSON serializer settings.
    /// Called during client initialization to apply Kilo-specific configuration.
    /// </summary>
    static partial void UpdateJsonSerializerSettings(JsonSerializerSettings settings)
    {
      // Apply any NSwag-specific customizations here
      // Currently empty - NSwag uses default settings
    }

    /// <summary>
    /// Internal method for creating shared serializer settings used by both
    /// NSwag HTTP API and SSE deserialization.
    /// </summary>
    internal static void UpdateJsonSerializerSettingsForShared(JsonSerializerSettings settings)
    {
      // Apply the same customizations as UpdateJsonSerializerSettings
      UpdateJsonSerializerSettings(settings);
    }
  }

  public partial class SessionStatus
  {
    // Discriminator: "idle" | "retry" | "busy" | "offline" | "error"
    [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
    public string? Type { get; set; }

    // For "retry" status
    [JsonProperty("attempt", NullValueHandling = NullValueHandling.Ignore)]
    public long? Attempt { get; set; }

    [JsonProperty("message", NullValueHandling = NullValueHandling.Ignore)]
    public string? Message { get; set; }

    [JsonProperty("action", NullValueHandling = NullValueHandling.Ignore)]
    public RetryAction? Action { get; set; }

    [JsonProperty("next", NullValueHandling = NullValueHandling.Ignore)]
    public long? Next { get; set; }

    // For "error" status
    [JsonProperty("reason", NullValueHandling = NullValueHandling.Ignore)]
    public string? Reason { get; set; }
  }

  public class RetryAction
  {
    [JsonProperty("reason", NullValueHandling = NullValueHandling.Ignore)]
    public string? Reason { get; set; }

    [JsonProperty("provider", NullValueHandling = NullValueHandling.Ignore)]
    public string? Provider { get; set; }

    [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
    public string? Title { get; set; }

    [JsonProperty("message", NullValueHandling = NullValueHandling.Ignore)]
    public string? Message { get; set; }

    [JsonProperty("label", NullValueHandling = NullValueHandling.Ignore)]
    public string? Label { get; set; }

    [JsonProperty("link", NullValueHandling = NullValueHandling.Ignore)]
    public string? Link { get; set; }


    // Sample use :

    //var status = await client.Session_statusAsync(directory, workspace);

    //foreach (var kvp in status)
    //{
    //    var sessionID = kvp.Key;
    //    var sessionStatus = kvp.Value;

    //    // Access the type discriminator
    //    var type = sessionStatus.Type;  // "idle" | "retry" | "busy" | "offline" | "error"

    //    // Access retry-specific properties
    //    if (type == "retry")
    //    {
    //        var attempt = sessionStatus.Attempt;
    //      var message = sessionStatus.Message;
    //      var next = sessionStatus.Next;
    //      var action = sessionStatus.Action;
    //    }
    //}
  }
}
