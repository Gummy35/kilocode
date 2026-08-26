#nullable enable

namespace KiloExtensionDTOs.ExtensionMessages;

using KiloVisualStudioExtension.ApiClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Type: ExtensionSettings
/// Source: extension-messages.ts
/// </summary>
public class ExtensionSettingsOverride : ExtensionSettings
{
  private System.Collections.Generic.IDictionary<string, object> _additionalProperties;

  [Newtonsoft.Json.JsonExtensionData]
  public System.Collections.Generic.IDictionary<string, object> AdditionalProperties
  {
    get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
    set { _additionalProperties = value; }
  }

  public string ToJson()
  {

    return Newtonsoft.Json.JsonConvert.SerializeObject(this, new Newtonsoft.Json.JsonSerializerSettings());

  }
  public static NotebookResult FromJson(string data)
  {

    return Newtonsoft.Json.JsonConvert.DeserializeObject<NotebookResult>(data, new Newtonsoft.Json.JsonSerializerSettings());

  }
}
