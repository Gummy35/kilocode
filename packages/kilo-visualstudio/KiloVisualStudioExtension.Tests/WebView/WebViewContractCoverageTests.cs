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

            var webviewToExtFiles = Directory.EnumerateFiles(Path.Combine(_messagesDir, "WebviewMessages"), "*.cs", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension).ToHashSet();
            
            var extToWebviewFiles = Directory.EnumerateFiles(Path.Combine(_messagesDir, "ExtensionMessages"), "*.cs", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension).ToHashSet();
            
            var partsFiles = Directory.EnumerateFiles(Path.Combine(_messagesDir, "Parts"), "*.cs", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension).ToHashSet();
            
            var sessionsFiles = Directory.EnumerateFiles(Path.Combine(_messagesDir, "Sessions"), "*.cs", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension).ToHashSet();
            
            var agentsFiles = Directory.EnumerateFiles(Path.Combine(_messagesDir, "Agents"), "*.cs", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension).ToHashSet();
            
            var memoryFiles = Directory.EnumerateFiles(Path.Combine(_messagesDir, "Memory"), "*.cs", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension).ToHashSet();
            
            var migrationFiles = Directory.EnumerateFiles(Path.Combine(_messagesDir, "Migration"), "*.cs", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension).ToHashSet();
            
            var sharedFiles = Directory.EnumerateFiles(Path.Combine(_messagesDir, "Shared"), "*.cs", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension).ToHashSet();
            
            var questionsFiles = Directory.EnumerateFiles(Path.Combine(_messagesDir, "Questions"), "*.cs", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension).ToHashSet();

            var allExtToWebviewFiles = new HashSet<string>(extToWebviewFiles);
            allExtToWebviewFiles.UnionWith(partsFiles);
            allExtToWebviewFiles.UnionWith(sessionsFiles);
            allExtToWebviewFiles.UnionWith(agentsFiles);
            allExtToWebviewFiles.UnionWith(memoryFiles);
            allExtToWebviewFiles.UnionWith(migrationFiles);
            allExtToWebviewFiles.UnionWith(sharedFiles);
            allExtToWebviewFiles.UnionWith(questionsFiles);

            foreach (var message in webviewToExtMessages)
            {
                var name = message["name"]!.Value<string>()!;
                webviewToExtFiles.Should().Contain(name, $"Contract message {name} should have a generated file");
            }
            
            foreach (var message in extToWebviewMessages)
            {
                var name = message["name"]!.Value<string>()!;
                allExtToWebviewFiles.Should().Contain(name, $"Contract message {name} should have a generated file");
            }
        }
    }
}
