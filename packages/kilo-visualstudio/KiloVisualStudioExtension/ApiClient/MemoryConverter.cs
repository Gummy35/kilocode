using KiloExtensionDTOs.Memory;
using Newtonsoft.Json;

namespace KiloVisualStudioExtension.ApiClient
{
  public static class MemoryEventConverter
  {
    public interface IMemoryEvent
    {

    }

    public static MemoryEventDetail? ToMemoryEventDetail<T>(this T? evt) where T : IMemoryEvent
    {
      if (evt == null) return null;

      // Get the Detail property via reflection (works for Detail, Detail2, Detail3)
      var detailProperty = evt.GetType().GetProperty("Detail");
      if (detailProperty == null) return null;

      var detail = detailProperty.GetValue(evt);
      if (detail == null) return null;

      // Serialize to JSON and deserialize to MemoryEventDetail
      var json = JsonConvert.SerializeObject(detail);
      return JsonConvert.DeserializeObject<MemoryEventDetail>(json);
    }
  }
}
