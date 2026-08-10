using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SymbolInventory
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string sourcePath = "packages\\kilo-visualstudio\\KiloVisualStudioExtension";
            string outputPath = "packages\\kilo-visualstudio\\porting\\manifest\\symbols.json";
            
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--sourcePath" && i + 1 < args.Length)
                {
                    sourcePath = args[++i];
                }
                else if (args[i] == "--output" && i + 1 < args.Length)
                {
                    outputPath = args[++i];
                }
            }

            var symbols = new List<SymbolEntry>();
            var rootDir = Directory.GetParent(Path.GetFullPath(sourcePath)).FullName;
            
            var projects = new[] { "KiloVisualStudioExtension", "KiloVisualStudioExtension.Tests" };
            foreach (var project in projects)
            {
                var projectPath = Path.Combine(rootDir, project);
                if (Directory.Exists(projectPath))
                {
                    var csFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories);
                    foreach (var file in csFiles.OrderBy(f => f, StringComparer.Ordinal))
                    {
                        var relativePath = $"{project}\\{Path.GetRelativePath(projectPath, file)}";
                        var fileId = GenerateFileId(relativePath);
                        var text = await File.ReadAllTextAsync(file);
                        var syntaxTree = CSharpSyntaxTree.ParseText(text);
                        var root = syntaxTree.GetRoot();

                        var references = AppDomain.CurrentDomain.GetAssemblies()
                            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                            .Select(a => MetadataReference.CreateFromFile(a.Location))
                            .ToList();

                        var compilation = CSharpCompilation.Create("temp")
                            .AddReferences(references)
                            .AddSyntaxTrees(syntaxTree);

                        var semanticModel = compilation.GetSemanticModel(syntaxTree);
                        var walker = new SymbolWalker(semanticModel, fileId, relativePath);
                        walker.Visit(root);
                        symbols.AddRange(walker.Symbols);
                    }
                }
            }

            symbols.Sort((a, b) => string.Compare(a.symbolId, b.symbolId, StringComparison.Ordinal));

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(symbols, options);
            await File.WriteAllTextAsync(outputPath, json);
            Console.WriteLine($"Generated {symbols.Count} symbols to {outputPath}");
        }

        static string GenerateFileId(string relativePath)
        {
            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var project = parts[0];
            var ext = Path.GetExtension(relativePath).TrimStart('.').ToLowerInvariant();
            var fileName = Path.GetFileNameWithoutExtension(relativePath);
            
            if (parts.Length == 2)
            {
                return $"file-{project}-{fileName}-{ext}";
            }
            var pathComponents = parts.Skip(1).Take(parts.Length - 2);
            return $"file-{project}-{string.Join("-", pathComponents)}-{fileName}-{ext}";
        }

        public static string GenerateSymbolId(string name, string signature, string fileId)
        {
            var combined = fileId + name + signature;
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
            var fullHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return $"symbol-{fullHash}";
        }

        public static string ComputeHash(string text)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    class SymbolWalker : CSharpSyntaxWalker
    {
        public List<SymbolEntry> Symbols { get; } = new List<SymbolEntry>();
        private readonly SemanticModel _semanticModel;
        private readonly string _fileId;
        private readonly string _relativePath;

        public SymbolWalker(SemanticModel semanticModel, string fileId, string relativePath)
        {
            _semanticModel = semanticModel;
            _fileId = fileId;
            _relativePath = relativePath;
        }

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null)
            {
                var name = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var signature = name;
                var entry = new SymbolEntry
                {
                    symbolId = Program.GenerateSymbolId(name, signature, _fileId),
                    fileId = _fileId,
                    fullyQualifiedName = name,
                    symbolKind = "namespace",
                    accessibility = null,
                    signature = signature,
                    rawSourceHash = Program.ComputeHash(node.ToString()),
                    containingSymbolId = null,
                    sourceSpan = $"{_relativePath}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}"
                };
                Symbols.Add(entry);
            }
            base.VisitNamespaceDeclaration(node);
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null)
            {
                var name = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                string[] baseTypes;
                if (symbol.BaseType != null && symbol.BaseType.SpecialType != SpecialType.System_Object)
                {
                    baseTypes = new[] { symbol.BaseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) }
                        .Concat(symbol.Interfaces.Select(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                        .ToArray();
                }
                else
                {
                    baseTypes = symbol.Interfaces.Select(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToArray();
                }
                var signature = $"{GetAccessibility(symbol.DeclaredAccessibility)} class {symbol.Name}{(node.TypeParameterList != null ? node.TypeParameterList.Parameters.ToString() : "")} : {string.Join(", ", baseTypes)}";
                string? containingSymbolId;
                if (symbol.ContainingType != null)
                {
                    containingSymbolId = Program.GenerateSymbolId(symbol.ContainingType.Name, symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), _fileId);
                }
                else if (symbol.ContainingNamespace != null && !symbol.ContainingNamespace.IsGlobalNamespace)
                {
                    containingSymbolId = Program.GenerateSymbolId(symbol.ContainingNamespace.Name, symbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), _fileId);
                }
                else
                {
                    containingSymbolId = null;
                }
                var entry = new SymbolEntry
                {
                    symbolId = Program.GenerateSymbolId(name, signature, _fileId),
                    fileId = _fileId,
                    fullyQualifiedName = name,
                    symbolKind = "class",
                    accessibility = GetAccessibilityString(symbol.DeclaredAccessibility),
                    signature = signature,
                    rawSourceHash = Program.ComputeHash(node.ToString()),
                    containingSymbolId = containingSymbolId,
                    sourceSpan = $"{_relativePath}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}",
                    modifiers = GetModifiers(node),
                    baseTypes = baseTypes
                };
                Symbols.Add(entry);
            }
            base.VisitClassDeclaration(node);
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null)
            {
                var name = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var baseTypes = symbol.Interfaces.Select(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToArray();
                var signature = $"{GetAccessibility(symbol.DeclaredAccessibility)} interface {symbol.Name} : {string.Join(", ", baseTypes)}";
                string? containingSymbolId;
                if (symbol.ContainingType != null)
                {
                    containingSymbolId = Program.GenerateSymbolId(symbol.ContainingType.Name, symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), _fileId);
                }
                else if (symbol.ContainingNamespace != null && !symbol.ContainingNamespace.IsGlobalNamespace)
                {
                    containingSymbolId = Program.GenerateSymbolId(symbol.ContainingNamespace.Name, symbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), _fileId);
                }
                else
                {
                    containingSymbolId = null;
                }
                var entry = new SymbolEntry
                {
                    symbolId = Program.GenerateSymbolId(name, signature, _fileId),
                    fileId = _fileId,
                    fullyQualifiedName = name,
                    symbolKind = "interface",
                    accessibility = GetAccessibilityString(symbol.DeclaredAccessibility),
                    signature = signature,
                    rawSourceHash = Program.ComputeHash(node.ToString()),
                    containingSymbolId = containingSymbolId,
                    sourceSpan = $"{_relativePath}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}",
                    baseTypes = baseTypes
                };
                Symbols.Add(entry);
            }
            base.VisitInterfaceDeclaration(node);
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null)
            {
                var name = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var returnType = symbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var parameters = string.Join(", ", symbol.Parameters.Select(p => $"{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {p.Name}"));
                var signature = $"{GetAccessibility(symbol.DeclaredAccessibility)} {returnType} {symbol.Name}({parameters})";
                string? containingSymbolId;
                if (symbol.ContainingType != null)
                {
                    containingSymbolId = Program.GenerateSymbolId(symbol.ContainingType.Name, symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), _fileId);
                }
                else if (symbol.ContainingNamespace != null && !symbol.ContainingNamespace.IsGlobalNamespace)
                {
                    containingSymbolId = Program.GenerateSymbolId(symbol.ContainingNamespace.Name, symbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), _fileId);
                }
                else
                {
                    containingSymbolId = null;
                }
                var entry = new SymbolEntry
                {
                    symbolId = Program.GenerateSymbolId(name, signature, _fileId),
                    fileId = _fileId,
                    fullyQualifiedName = name,
                    symbolKind = "method",
                    accessibility = GetAccessibilityString(symbol.DeclaredAccessibility),
                    signature = signature,
                    rawSourceHash = Program.ComputeHash(node.ToString()),
                    containingSymbolId = containingSymbolId,
                    sourceSpan = $"{_relativePath}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}",
                    modifiers = GetModifiers(node),
                    returnType = returnType,
                    parameters = symbol.Parameters.Select(p => new { p.Name, type = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) }).ToArray()
                };
                Symbols.Add(entry);
            }
            base.VisitMethodDeclaration(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null)
            {
                var name = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var type = symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var signature = $"{GetAccessibility(symbol.DeclaredAccessibility)} {type} {symbol.Name} {{ get; set; }}";
                string? containingSymbolId;
                if (symbol.ContainingType != null)
                {
                    containingSymbolId = Program.GenerateSymbolId(symbol.ContainingType.Name, symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), _fileId);
                }
                else if (symbol.ContainingNamespace != null && !symbol.ContainingNamespace.IsGlobalNamespace)
                {
                    containingSymbolId = Program.GenerateSymbolId(symbol.ContainingNamespace.Name, symbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), _fileId);
                }
                else
                {
                    containingSymbolId = null;
                }
                var entry = new SymbolEntry
                {
                    symbolId = Program.GenerateSymbolId(name, signature, _fileId),
                    fileId = _fileId,
                    fullyQualifiedName = name,
                    symbolKind = "property",
                    accessibility = GetAccessibilityString(symbol.DeclaredAccessibility),
                    signature = signature,
                    rawSourceHash = Program.ComputeHash(node.ToString()),
                    containingSymbolId = containingSymbolId,
                    sourceSpan = $"{_relativePath}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}",
                    type = type
                };
                Symbols.Add(entry);
            }
            base.VisitPropertyDeclaration(node);
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            foreach (var variable in node.Declaration.Variables)
            {
                var symbol = _semanticModel.GetDeclaredSymbol(variable);
                if (symbol != null)
                {
                    var name = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var typeSymbol = symbol as IFieldSymbol;
                    var type = typeSymbol?.Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "unknown";
                    var signature = $"{GetAccessibility(symbol.DeclaredAccessibility)} {type} {symbol.Name}";
                    string? containingSymbolId;
                    if (symbol.ContainingType != null)
                    {
                        containingSymbolId = Program.GenerateSymbolId(symbol.ContainingType.Name, symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), _fileId);
                    }
                    else if (symbol.ContainingNamespace != null && !symbol.ContainingNamespace.IsGlobalNamespace)
                    {
                        containingSymbolId = Program.GenerateSymbolId(symbol.ContainingNamespace.Name, symbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), _fileId);
                    }
                    else
                    {
                        containingSymbolId = null;
                    }
                    var entry = new SymbolEntry
                    {
                        symbolId = Program.GenerateSymbolId(name, signature, _fileId),
                        fileId = _fileId,
                        fullyQualifiedName = name,
                        symbolKind = "field",
                        accessibility = GetAccessibilityString(symbol.DeclaredAccessibility),
                        signature = signature,
                        rawSourceHash = Program.ComputeHash(variable.ToString()),
                        containingSymbolId = containingSymbolId,
                        sourceSpan = $"{_relativePath}:{variable.GetLocation().GetLineSpan().StartLinePosition.Line + 1}",
                        type = type
                    };
                    Symbols.Add(entry);
                }
            }
            base.VisitFieldDeclaration(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null)
            {
                var name = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var parameters = string.Join(", ", symbol.Parameters.Select(p => $"{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {p.Name}"));
                var signature = $"{GetAccessibility(symbol.DeclaredAccessibility)} {symbol.Name}({parameters})";
                string? containingSymbolId;
                if (symbol.ContainingType != null)
                {
                    containingSymbolId = Program.GenerateSymbolId(symbol.ContainingType.Name, symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), _fileId);
                }
                else if (symbol.ContainingNamespace != null && !symbol.ContainingNamespace.IsGlobalNamespace)
                {
                    containingSymbolId = Program.GenerateSymbolId(symbol.ContainingNamespace.Name, symbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), _fileId);
                }
                else
                {
                    containingSymbolId = null;
                }
                var entry = new SymbolEntry
                {
                    symbolId = Program.GenerateSymbolId(name, signature, _fileId),
                    fileId = _fileId,
                    fullyQualifiedName = name,
                    symbolKind = "constructor",
                    accessibility = GetAccessibilityString(symbol.DeclaredAccessibility),
                    signature = signature,
                    rawSourceHash = Program.ComputeHash(node.ToString()),
                    containingSymbolId = containingSymbolId,
                    sourceSpan = $"{_relativePath}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}",
                    parameters = symbol.Parameters.Select(p => new { p.Name, type = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) }).ToArray()
                };
                Symbols.Add(entry);
            }
            base.VisitConstructorDeclaration(node);
        }

        public override void VisitEventDeclaration(EventDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null)
            {
                var name = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var type = symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var signature = $"{GetAccessibility(symbol.DeclaredAccessibility)} event {type} {symbol.Name}";
                string? containingSymbolId;
                if (symbol.ContainingType != null)
                {
                    containingSymbolId = Program.GenerateSymbolId(symbol.ContainingType.Name, symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), _fileId);
                }
                else if (symbol.ContainingNamespace != null && !symbol.ContainingNamespace.IsGlobalNamespace)
                {
                    containingSymbolId = Program.GenerateSymbolId(symbol.ContainingNamespace.Name, symbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), _fileId);
                }
                else
                {
                    containingSymbolId = null;
                }
                var entry = new SymbolEntry
                {
                    symbolId = Program.GenerateSymbolId(name, signature, _fileId),
                    fileId = _fileId,
                    fullyQualifiedName = name,
                    symbolKind = "event",
                    accessibility = GetAccessibilityString(symbol.DeclaredAccessibility),
                    signature = signature,
                    rawSourceHash = Program.ComputeHash(node.ToString()),
                    containingSymbolId = containingSymbolId,
                    sourceSpan = $"{_relativePath}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}",
                    type = type
                };
                Symbols.Add(entry);
            }
            base.VisitEventDeclaration(node);
        }

        private static string GetAccessibility(Accessibility accessibility)
        {
            return accessibility switch
            {
                Accessibility.Public => "public",
                Accessibility.Protected => "protected",
                Accessibility.Internal => "internal",
                Accessibility.ProtectedOrInternal => "protected internal",
                Accessibility.Private => "private",
                _ => "unknown"
            };
        }

        private static string GetAccessibilityString(Accessibility accessibility)
        {
            return GetAccessibility(accessibility);
        }

        private static string[] GetModifiers(SyntaxNode node)
        {
            var modifiers = new List<string>();
            if (node is TypeDeclarationSyntax typeDecl)
            {
                foreach (var modifier in typeDecl.Modifiers)
                {
                    modifiers.Add(modifier.Text);
                }
            }
            else if (node is MemberDeclarationSyntax memberDecl)
            {
                foreach (var modifier in memberDecl.Modifiers)
                {
                    modifiers.Add(modifier.Text);
                }
            }
            return modifiers.ToArray();
        }
    }

    class SymbolEntry
    {
        public string symbolId { get; set; } = "";
        public string fileId { get; set; } = "";
        public string fullyQualifiedName { get; set; } = "";
        public string symbolKind { get; set; } = "";
        public string? accessibility { get; set; }
        public string signature { get; set; } = "";
        public string rawSourceHash { get; set; } = "";
        public string? normalizedSourceHash { get; set; }
        public string? containingSymbolId { get; set; }
        public string sourceSpan { get; set; } = "";
        public string[]? modifiers { get; set; }
        public string[]? baseTypes { get; set; }
        public string? returnType { get; set; }
        public string? type { get; set; }
        public dynamic[]? parameters { get; set; }
    }
}
