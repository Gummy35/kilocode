using Newtonsoft.Json;

namespace KiloVisualStudioExtension.Services.WorkStyle
{
  public sealed class PartialWorkStyleSettings
  {
    [JsonProperty(
        "showTaskTimeline",
        NullValueHandling = NullValueHandling.Ignore)]
    public bool? ShowTaskTimeline { get; set; }
  }

}
