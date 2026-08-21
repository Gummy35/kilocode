using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace KiloVisualStudioExtension
{
  internal class JsonDocumentBuilder
  {
    private readonly Dictionary<string, JsonElement> _properties = new Dictionary<string, JsonElement>();

    public void Add(string name, JsonElement value)
    {
      _properties[name] = value;
    }

    public JsonElement Build()
    {
      using var stream = new MemoryStream();
      using var writer = new Utf8JsonWriter(stream);
      writer.WriteStartObject();
      foreach (var prop in _properties)
      {
        writer.WritePropertyName(prop.Key);
        prop.Value.WriteTo(writer);
      }
      writer.WriteEndObject();
      writer.Flush();
      stream.Position = 0;
      using var doc = JsonDocument.Parse(stream);
      return doc.RootElement.Clone();
    }
  }
}
