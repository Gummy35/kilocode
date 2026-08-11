using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WebViewContractGenerator
{
    public class WebViewContract
    {
        [JsonPropertyName("schemaVersion")]
        public string schemaVersion { get; set; } = "";

        [JsonPropertyName("generatedFrom")]
        public SourceInfo generatedFrom { get; set; } = new();

        [JsonPropertyName("metadata")]
        public Metadata metadata { get; set; } = new();

        [JsonPropertyName("messages")]
        public MessageCollections messages { get; set; } = new();

        [JsonPropertyName("types")]
        public List<TypeDefinition> types { get; set; } = new();

        [JsonPropertyName("diagnostics")]
        public Diagnostics diagnostics { get; set; } = new();

        [JsonPropertyName("statistics")]
        public Statistics statistics { get; set; } = new();
    }

    public class SourceInfo
    {
        [JsonPropertyName("repository")]
        public string repository { get; set; } = "";

        [JsonPropertyName("branch")]
        public string branch { get; set; } = "";

        [JsonPropertyName("commit")]
        public string commit { get; set; } = "";

        [JsonPropertyName("generatedAt")]
        public string generatedAt { get; set; } = "";
    }

    public class Metadata
    {
        [JsonPropertyName("extractorVersion")]
        public string extractorVersion { get; set; } = "";

        [JsonPropertyName("typescriptVersion")]
        public string typescriptVersion { get; set; } = "";

        [JsonPropertyName("sourcePath")]
        public string sourcePath { get; set; } = "";
    }

    public class MessageCollections
    {
        [JsonPropertyName("webviewToExtension")]
        public List<MessageType> webviewToExtension { get; set; } = new();

        [JsonPropertyName("extensionToWebview")]
        public List<MessageType> extensionToWebview { get; set; } = new();
    }

    public class MessageType
    {
        [JsonPropertyName("name")]
        public string name { get; set; } = "";

        [JsonPropertyName("type")]
        public string type { get; set; } = "";

        [JsonPropertyName("discriminator")]
        public DiscriminatorInfo discriminator { get; set; } = new();

        [JsonPropertyName("properties")]
        public List<PropertyDefinition> properties { get; set; } = new();

        [JsonPropertyName("sourceFile")]
        public string sourceFile { get; set; } = "";
    }

    public class TypeDefinition
    {
        [JsonPropertyName("name")]
        public string name { get; set; } = "";

        [JsonPropertyName("kind")]
        public string kind { get; set; } = "";

        [JsonPropertyName("properties")]
        public List<PropertyDefinition>? properties { get; set; }

        [JsonPropertyName("unionMembers")]
        public List<string>? unionMembers { get; set; }

        [JsonPropertyName("discriminator")]
        public DiscriminatorInfo? discriminator { get; set; }

        [JsonPropertyName("sourceFile")]
        public string sourceFile { get; set; } = "";

        [JsonPropertyName("description")]
        public string? description { get; set; }
    }

    public class PropertyDefinition
    {
        [JsonPropertyName("name")]
        public string name { get; set; } = "";

        [JsonPropertyName("type")]
        public string type { get; set; } = "";

        [JsonPropertyName("optional")]
        public bool optional { get; set; }

        [JsonPropertyName("nullable")]
        public bool nullable { get; set; }

        [JsonPropertyName("elementType")]
        public string? elementType { get; set; }

        [JsonPropertyName("typeRef")]
        public TypeReference? typeRef { get; set; }

        [JsonPropertyName("literalValue")]
        public object? literalValue { get; set; }

        [JsonPropertyName("isLiteral")]
        public bool isLiteral { get; set; }

        [JsonPropertyName("description")]
        public string? description { get; set; }
    }

    public class TypeReference
    {
        [JsonPropertyName("name")]
        public string name { get; set; } = "";

        [JsonPropertyName("kind")]
        public string kind { get; set; } = "";
    }

    public class DiscriminatorInfo
    {
        [JsonPropertyName("field")]
        public string field { get; set; } = "";

        [JsonPropertyName("value")]
        public string value { get; set; } = "";
    }

    public class Diagnostics
    {
        [JsonPropertyName("errors")]
        public List<string> errors { get; set; } = new();

        [JsonPropertyName("warnings")]
        public List<string> warnings { get; set; } = new();
    }

    public class Statistics
    {
        [JsonPropertyName("totalTypes")]
        public int totalTypes { get; set; }

        [JsonPropertyName("webviewToExtensionMessages")]
        public int webviewToExtensionMessages { get; set; }

        [JsonPropertyName("extensionToWebviewMessages")]
        public int extensionToWebviewMessages { get; set; }
    }
}
