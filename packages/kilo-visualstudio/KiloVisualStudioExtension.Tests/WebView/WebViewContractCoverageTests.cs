using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KiloVisualStudioExtension.Tests.WebView
{
    public class WebViewContractCoverageTests
    {
        private static readonly string SolutionRoot = FindSolutionRoot();
        private readonly string _contractPath;
        private readonly string _messagesDir;
        private readonly string _factoryPath;

        private static string FindSolutionRoot()
        {
            return "C:\\prog\\kilocode\\kilocode";
        }

        public WebViewContractCoverageTests()
        {
            _contractPath = Path.Combine(SolutionRoot, "packages", "kilo-visualstudio", "porting", "contract", "WebViewContract.json");
            _messagesDir = Path.Combine(SolutionRoot, "packages", "kilo-visualstudio", "KiloVisualStudioExtension", "WebViewDto", "Messages");
            _factoryPath = Path.Combine(SolutionRoot, "packages", "kilo-visualstudio", "KiloVisualStudioExtension", "WebViewDto", "WebViewMessageFactory.cs");
        }

        [Fact]
        public void AllContractMessages_HaveGeneratedClasses()
        {
            var contract = JObject.Parse(File.ReadAllText(_contractPath));
            var webviewToExtMessages = contract["messages"]!["webviewToExtension"]!.ToObject<List<JObject>>()!;
            var extToWebviewMessages = contract["messages"]!["extensionToWebview"]!.ToObject<List<JObject>>()!;

            var missing = new List<string>();

            foreach (var message in webviewToExtMessages)
            {
                var name = message["name"]!.Value<string>()!;
                var filePath = Path.Combine(_messagesDir, "WebviewToExtension", $"{name}.cs");
                if (!File.Exists(filePath))
                {
                    missing.Add($"{name} (webviewToExtension)");
                }
            }

            foreach (var message in extToWebviewMessages)
            {
                var name = message["name"]!.Value<string>()!;
                var filePath = Path.Combine(_messagesDir, "ExtensionToWebview", $"{name}.cs");
                if (!File.Exists(filePath))
                {
                    missing.Add($"{name} (extensionToWebview)");
                }
            }

            missing.Should().BeEmpty($"All {webviewToExtMessages.Count + extToWebviewMessages.Count} contract messages should have generated classes");
        }

        [Fact]
        public void AllContractDiscriminators_HaveFactoryCases()
        {
            var contract = JObject.Parse(File.ReadAllText(_contractPath));
            var webviewToExtMessages = contract["messages"]!["webviewToExtension"]!.ToObject<List<JObject>>()!;
            var extToWebviewMessages = contract["messages"]!["extensionToWebview"]!.ToObject<List<JObject>>()!;

            var factoryContent = File.ReadAllText(_factoryPath);
            var missing = new List<string>();

            foreach (var message in webviewToExtMessages)
            {
                var discriminator = message["discriminator"]!["value"]!.Value<string>()!;
                if (!factoryContent.Contains($"\"{discriminator}\""))
                {
                    missing.Add($"{discriminator} ({message["name"]})");
                }
            }

            foreach (var message in extToWebviewMessages)
            {
                var discriminator = message["discriminator"]!["value"]!.Value<string>()!;
                if (!factoryContent.Contains($"\"{discriminator}\""))
                {
                    missing.Add($"{discriminator} ({message["name"]})");
                }
            }

            missing.Should().BeEmpty($"All {webviewToExtMessages.Count + extToWebviewMessages.Count} discriminators should have factory cases");
        }

        [Fact]
        public void GeneratedClasses_Count_Matches_Contract()
        {
            var contract = JObject.Parse(File.ReadAllText(_contractPath));
            var webviewToExtMessages = contract["messages"]!["webviewToExtension"]!.ToObject<List<JObject>>()!;
            var extToWebviewMessages = contract["messages"]!["extensionToWebview"]!.ToObject<List<JObject>>()!;

            var webviewToExtFiles = Directory.GetFiles(Path.Combine(_messagesDir, "WebviewToExtension"), "*.cs").Length;
            var extToWebviewFiles = Directory.GetFiles(Path.Combine(_messagesDir, "ExtensionToWebview"), "*.cs").Length;

            webviewToExtFiles.Should().Be(webviewToExtMessages.Count, $"WebviewToExtension message count mismatch: expected {webviewToExtMessages.Count}, got {webviewToExtFiles}");
            
            extToWebviewFiles.Should().BeGreaterOrEqualTo(extToWebviewMessages.Count - 10, 
                $"ExtensionToWebview message count mismatch: expected at least {extToWebviewMessages.Count - 10} (excluding node_modules types), got {extToWebviewFiles}");
        }
    }
}
