using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebViewContractGenerator
{
    public class GeneratorOptions
    {
        public string ContractPath { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public string Namespace { get; set; } = "KiloVisualStudioExtension.WebView.Generated";
    }

    public class Generator
    {
        private readonly GeneratorOptions _options;
        private readonly JsonSerializerOptions _jsonOptions;

        public Generator(GeneratorOptions options)
        {
            _options = options;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
        }

        public void Generate()
        {
            Console.WriteLine("WebView DTO Generator");
            Console.WriteLine("=====================");
            Console.WriteLine();

            if (!File.Exists(_options.ContractPath))
            {
                Console.Error.WriteLine($"Error: Contract file not found: {_options.ContractPath}");
                Environment.Exit(1);
            }

            Console.WriteLine($"Reading contract from: {_options.ContractPath}");
            var contractJson = File.ReadAllText(_options.ContractPath);
            var contract = JsonSerializer.Deserialize<WebViewContract>(contractJson, _jsonOptions);

            if (contract == null)
            {
                Console.Error.WriteLine("Error: Failed to parse contract JSON");
                Environment.Exit(1);
            }

            Console.WriteLine($"Contract version: {contract.schemaVersion}");
            Console.WriteLine($"Total types: {contract.statistics.totalTypes}");
            Console.WriteLine($"WebView→Extension messages: {contract.statistics.webviewToExtensionMessages}");
            Console.WriteLine($"Extension→WebView messages: {contract.statistics.extensionToWebviewMessages}");
            Console.WriteLine();

            var outputDir = _options.OutputPath;
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            GenerateMessages(contract, outputDir);
            GenerateTypes(contract, outputDir);
            GenerateFactory(contract, outputDir);

            Console.WriteLine();
            Console.WriteLine("Generation complete!");
            Console.WriteLine($"Output directory: {outputDir}");
        }

        private void GenerateMessages(WebViewContract contract, string outputDir)
        {
            Console.WriteLine("Generating message classes...");

            var webviewToExtDir = Path.Combine(outputDir, "Messages", "WebviewToExtension");
            var extToWebviewDir = Path.Combine(outputDir, "Messages", "ExtensionToWebview");

            Directory.CreateDirectory(webviewToExtDir);
            Directory.CreateDirectory(extToWebviewDir);

            foreach (var message in contract.messages.webviewToExtension)
            {
                var code = GenerateMessageClass(message, _options.Namespace);
                var filePath = Path.Combine(webviewToExtDir, $"{message.name}.cs");
                File.WriteAllText(filePath, code);
                Console.WriteLine($"  Generated: {message.name}");
            }

            foreach (var message in contract.messages.extensionToWebview)
            {
                var code = GenerateMessageClass(message, _options.Namespace);
                var filePath = Path.Combine(extToWebviewDir, $"{message.name}.cs");
                File.WriteAllText(filePath, code);
                Console.WriteLine($"  Generated: {message.name}");
            }
        }

        private void GenerateTypes(WebViewContract contract, string outputDir)
        {
            Console.WriteLine("Generating type definitions...");

            var typesDir = Path.Combine(outputDir, "Types");
            Directory.CreateDirectory(typesDir);

            var processedNames = new HashSet<string>();

            foreach (var typeDef in contract.types)
            {
                if (processedNames.Contains(typeDef.name))
                    continue;

                if (typeDef.kind == "interface" && typeDef.properties != null)
                {
                    var code = GenerateTypeClass(typeDef, _options.Namespace);
                    var filePath = Path.Combine(typesDir, $"{typeDef.name}.cs");
                    File.WriteAllText(filePath, code);
                    Console.WriteLine($"  Generated: {typeDef.name}");
                    processedNames.Add(typeDef.name);
                }
                else if (typeDef.kind == "union")
                {
                    GenerateUnionTypes(typeDef, typesDir, processedNames);
                }
            }
        }

        private void GenerateUnionTypes(TypeDefinition unionDef, string typesDir, HashSet<string> processedNames)
        {
            if (unionDef.unionMembers == null)
                return;

            foreach (var memberName in unionDef.unionMembers)
            {
                if (processedNames.Contains(memberName))
                    continue;

                var memberDef = FindTypeByName(unionDef, memberName);
                if (memberDef != null && memberDef.properties != null)
                {
                    var code = GenerateTypeClass(memberDef, _options.Namespace);
                    var filePath = Path.Combine(typesDir, $"{memberDef.name}.cs");
                    File.WriteAllText(filePath, code);
                    Console.WriteLine($"  Generated: {memberDef.name} (union member)");
                    processedNames.Add(memberDef.name);
                }
            }
        }

        private TypeDefinition? FindTypeByName(TypeDefinition unionDef, string name)
        {
            return unionDef.unionMembers?.Contains(name) == true 
                ? new TypeDefinition 
                { 
                    name = name, 
                    kind = "interface",
                    properties = new List<PropertyDefinition>(),
                    sourceFile = unionDef.sourceFile
                }
                : null;
        }

        private void GenerateFactory(WebViewContract contract, string outputDir)
        {
            Console.WriteLine("Generating discriminator factory...");

            var factoryCode = GenerateDiscriminatorFactory(contract, _options.Namespace);
            var factoryPath = Path.Combine(outputDir, "WebViewMessageFactory.cs");
            File.WriteAllText(factoryPath, factoryCode);
        }

        private string GenerateMessageClass(MessageType message, string ns)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//     This code was generated by WebViewContractGenerator.");
            sb.AppendLine("//     Do not modify this file directly as changes will be lost on regeneration.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine($"#nullable enable");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns};");
            sb.AppendLine();
            sb.AppendLine($"using Newtonsoft.Json;");
            sb.AppendLine();
            sb.AppendLine($"/// <summary>");
            sb.AppendLine($"/// WebView message: {message.name}");
            sb.AppendLine($"/// Discriminator: {message.discriminator.field} = \"{message.discriminator.value}\"");
            sb.AppendLine($"/// Source: {message.sourceFile}");
            sb.AppendLine($"/// </summary>");
            sb.AppendLine($"public class {message.name}");
            sb.AppendLine("{");

            foreach (var prop in message.properties)
            {
                var propType = MapTypeToCSharp(prop);
                var nullable = prop.optional || prop.nullable ? "?" : "";
                var jsonAttr = prop.name != message.discriminator.field 
                    ? $"    [JsonProperty(\"{prop.name}\")]" + Environment.NewLine 
                    : "";
                var summary = string.IsNullOrEmpty(prop.description) ? "" 
                    : $"    /// <summary>{prop.description}</summary>" + Environment.NewLine;
                
                sb.AppendLine(jsonAttr + summary + $"    public {propType}{nullable} {PascalCase(prop.name)} {{ get; set; }}");
            }

            sb.AppendLine("}");

            return sb.ToString();
        }

        private string GenerateTypeClass(TypeDefinition typeDef, string ns)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//     This code was generated by WebViewContractGenerator.");
            sb.AppendLine("//     Do not modify this file directly as changes will be lost on regeneration.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine($"#nullable enable");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns};");
            sb.AppendLine();
            sb.AppendLine($"using Newtonsoft.Json;");
            sb.AppendLine();
            
            if (typeDef.discriminator != null)
            {
                sb.AppendLine($"/// <summary>");
                sb.AppendLine($"/// Part type: {typeDef.name}");
                sb.AppendLine($"/// Discriminator: {typeDef.discriminator.field} = \"{typeDef.discriminator.value}\"");
                sb.AppendLine($"/// Source: {typeDef.sourceFile}");
                sb.AppendLine($"/// </summary>");
            }
            else
            {
                sb.AppendLine($"/// <summary>");
                sb.AppendLine($"/// Type: {typeDef.name}");
                sb.AppendLine($"/// Source: {typeDef.sourceFile}");
                sb.AppendLine($"/// </summary>");
            }
            
            sb.AppendLine($"public class {typeDef.name}");
            sb.AppendLine("{");

            if (typeDef.properties != null)
            {
                foreach (var prop in typeDef.properties)
                {
                    var propType = MapTypeToCSharp(prop);
                    var nullable = prop.optional || prop.nullable ? "?" : "";
                    var jsonAttr = prop.name != typeDef.discriminator?.field
                        ? $"    [JsonProperty(\"{prop.name}\")]" + Environment.NewLine
                        : "";
                    var summary = string.IsNullOrEmpty(prop.description) ? ""
                        : $"    /// <summary>{prop.description}</summary>" + Environment.NewLine;

                    sb.AppendLine(jsonAttr + summary + $"    public {propType}{nullable} {PascalCase(prop.name)} {{ get; set; }}");
                }
            }

            sb.AppendLine("}");

            return sb.ToString();
        }

        private string GenerateDiscriminatorFactory(WebViewContract contract, string ns)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//     This code was generated by WebViewContractGenerator.");
            sb.AppendLine("//     Do not modify this file directly as changes will be lost on regeneration.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine($"#nullable enable");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns};");
            sb.AppendLine();
            sb.AppendLine($"using System;");
            sb.AppendLine($"using Newtonsoft.Json;");
            sb.AppendLine($"using Newtonsoft.Json.Linq;");
            sb.AppendLine();
            sb.AppendLine($"/// <summary>");
            sb.AppendLine($"/// Discriminator-based deserialization factory for WebView messages.");
            sb.AppendLine($"/// Uses explicit discriminator checking instead of JsonConverter inheritance.");
            sb.AppendLine($"/// </summary>");
            sb.AppendLine($"public static class WebViewMessageFactory");
            sb.AppendLine("{");
            sb.AppendLine($"    private static readonly JsonSerializer Serializer = KiloApiClient.CreateJsonSerializer();");
            sb.AppendLine();
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Deserialize a WebView message from JSON using discriminator-based routing.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public static T Deserialize<T>(JToken token) where T : class");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        var type = token[\"type\"]?.Value<string>();");
            sb.AppendLine();
            sb.AppendLine($"        return type switch");
            sb.AppendLine($"        {{");

            foreach (var message in contract.messages.webviewToExtension)
            {
                sb.AppendLine($"            \"{message.discriminator.value}\" => token.ToObject<{message.name}>(Serializer),");
            }

            foreach (var message in contract.messages.extensionToWebview)
            {
                sb.AppendLine($"            \"{message.discriminator.value}\" => token.ToObject<{message.name}>(Serializer),");
            }

            sb.AppendLine($"            _ => throw new JsonSerializationException($\"Unknown message type: {{type}}\")");
            sb.AppendLine($"        }};");
            sb.AppendLine($"    }}");
            sb.AppendLine();
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Deserialize a WebView message from JSON string using discriminator-based routing.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public static T Deserialize<T>(string json) where T : class");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        var token = JToken.Parse(json);");
            sb.AppendLine($"        return Deserialize<T>(token);");
            sb.AppendLine($"    }}");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private string MapTypeToCSharp(PropertyDefinition prop)
        {
            return prop.type switch
            {
                "string" => "string",
                "number" => "double",
                "integer" => "int",
                "boolean" => "bool",
                "array" => $"List<{prop.elementType ?? "object"}>",
                "record" => $"Dictionary<string, {prop.elementType ?? "object"}>",
                "literal" => "string",
                "union" => "object",
                "any" or "unknown" => "object",
                _ => prop.type switch
                {
                    "Part" => "Part",
                    "Message" => "Message",
                    "SessionInfo" => "SessionInfo",
                    "FileAttachment" => "FileAttachment",
                    _ => prop.typeRef?.name ?? "object"
                }
            };
        }

        private string PascalCase(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;
            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }
    }
}
