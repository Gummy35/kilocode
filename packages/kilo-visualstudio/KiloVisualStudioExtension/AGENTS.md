# Kilo Visual Studio Extension

## Architecture

The Visual Studio extension follows the same architecture as the VS Code extension:

```
┌─────────────────────────────────────────────────────────┐
│  Visual Studio Extension (C# VSIX)                       │
│  - WebView2 control hosts SolidJS webview               │
│  - CLI backend process manager                          │
│  - VS SDK integration (commands, menus)                 │
└─────────────────────────────────────────────────────────┘
                          │
                          │ HTTP + SSE (localhost)
                          │
┌─────────────────────────────────────────────────────────┐
│  Kilo CLI Backend (packages/opencode/)                   │
│  - Bun runtime (TypeScript)                              │
│  - Hono HTTP server                                      │
│  - AI agent runtime, tools, sessions                     │
└─────────────────────────────────────────────────────────┘
```

## Key Differences from VS Code Extension

| Aspect | VS Code | Visual Studio |
|---|---|---|
| **Extension Language** | TypeScript/JavaScript | C#/.NET Framework |
| **UI Technology** | Webview (SolidJS) | WebView2 (SolidJS) |
| **Process Management** | Node.js `child_process` | `System.Diagnostics.Process` |
| **Editor API** | VS Code API | Visual Studio SDK (EnvDTE, IVs*) |
| **Package Format** | `.vsix` (Node-based) | `.vsix` (MEF-based) |

## Project Structure

```
packages/kilo-visualstudio/
├── KiloPackage.cs              # Main package (AsyncPackage)
├── KiloToolWindow.cs           # Tool window pane
├── KiloWebViewControl.cs       # WebView2 wrapper
├── CliBackendManager.cs        # CLI process lifecycle
├── Guids.cs                    # GUIDs and command IDs
├── KiloPackage.vsct            # Commands/menus definition
├── source.extension.vsixmanifest  # Extension manifest
├── KiloVisualStudio.csproj     # Project file
├── build.ts                    # Build script
└── webview/                    # Static webview files
```

## Development Guidelines

### C# Coding Standards

- Use `async/await` for all I/O operations
- Prefer `Task` over `void` for async methods (except event handlers)
- Use `ThreadHelper.JoinableTaskFactory` for thread switching
- Annotate UI code with `[MainThread]` or use `JoinableTaskFactory.SwitchToMainThreadAsync()`
- Follow .NET naming conventions (PascalCase for public, camelCase for private)
- Leverage .NET 8 features: `System.Text.Json` source generation, primary constructors, collection expressions

### WebView2 Best Practices

- Initialize WebView2 asynchronously in `OnToolWindowCreatedAsync`
- Use `CoreWebView2.WebMessageReceived` for extension↔webview communication
- Set `IsWebMessageEnabled = true` to allow postMessage
- Handle errors gracefully with try/catch in message handlers

### CLI Backend Integration

- Spawn `kilo serve --port 0` to get a random port
- Parse port from stdout: `listening on http://127.0.0.1:PORT`
- Use `System.Diagnostics.Process` with `RedirectStandardOutput = true`
- Set environment variables: `KILO_CLIENT=visualstudio`, `KILO_PLATFORM=visualstudio`
- Wait for `/global/health` endpoint to return 200 before considering backend ready

### Visual Studio SDK Patterns

- Use `AsyncPackage` for background initialization
- Register tool windows with `[ProvideToolWindow]` attribute
- Use `OleMenuCommandService` for command registration
- Implement `IVsSolutionEvents` for solution lifecycle hooks (if needed)

## Building

```bash
# Development build
bun run build.ts

# Release build
bun run build.ts --release

# Direct dotnet build
dotnet build -c Debug
dotnet build -c Release
```

## Testing

### Manual Testing

1. Build the VSIX
2. Install by double-clicking the `.vsix` file
3. Launch Visual Studio
4. Open **View > Other Windows > Kilo Code**

### Debugging

- Set breakpoints in C# code
- Attach debugger to `devenv.exe` (Experimental Instance)
- For webview debugging, enable DevTools:
  ```csharp
  CoreWebView2.Settings.IsWebMessageEnabled = true;
  // Then in browser: chrome://inspect
  ```

## Common Issues

### Backend fails to start
- Verify CLI binary exists at `packages/opencode/dist/`
- Check `Output > Kilo Code` window for error messages
- Ensure Bun is installed: `bun --version`

### WebView2 not loading
- Windows 10/11 has WebView2 built-in
- Windows 8.1 or earlier requires manual runtime installation
- Check that webview files exist in extension directory

### Extension doesn't appear
- Restart Visual Studio after installation
- Check **Tools > Extensions and Updates**
- Verify VSIX manifest has correct `MinimumVisualStudioVersion`

## Next Steps

1. **Inline Autocomplete**: Implement `IVsTextViewConnectionProvider` for completion suggestions
2. **Terminal Integration**: Use `IVsTerminal` interfaces for terminal commands
3. **Git Integration**: Leverage built-in Visual Studio Git tools or implement via CLI
4. **Settings Sync**: Integrate with Visual Studio settings sync (if available)

## References

- [Visual Studio SDK Documentation](https://learn.microsoft.com/en-us/visualstudio/extensibility/)
- [WebView2 Documentation](https://learn.microsoft.com/en-us/microsoft-edge/webview2/)
- [VSIX Manifest Schema](https://learn.microsoft.com/en-us/visualstudio/extensibility/vsix-manifest-schema-reference)
- [Tool Window Guide](https://learn.microsoft.com/en-us/visualstudio/extensibility/tool-window)
