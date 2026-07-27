# Kilo Code for Visual Studio

Visual Studio extension providing AI coding assistance powered by the Kilo CLI backend.

## Features

- **AI Chat Interface**: Natural language coding assistance
- **500+ Model Support**: Access to Claude, GPT, Gemini, and more
- **Inline Autocomplete**: Intelligent code suggestions
- **Agent Manager**: Multi-session orchestration
- **Terminal Integration**: AI-assisted command generation
- **File Operations**: Create, edit, and manage files with AI

## Architecture

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

## Prerequisites

- **Visual Studio 2022** (version 17.0+) or **Visual Studio 2026**
- **.NET 8.0** (LTS) - Download from https://dotnet.microsoft.com/download/dotnet/8.0
- **Bun** (for building the CLI backend)
- **Git**

## Development Setup

### 1. Build the CLI Backend

```bash
# From repo root
bun install
bun run build
```

### 2. Build the Webview

The webview is built from the VS Code extension package:

```bash
# From packages/kilo-vscode/
bun run build-storybook
```

This generates the static webview files that will be embedded in the VSIX.

### 3. Build the Extension

```bash
# From packages/kilo-visualstudio/
dotnet build
```

Or from the repo root:

```bash
dotnet build packages/kilo-visualstudio/KiloVisualStudio.csproj
```

### 4. Run in Development Mode

```bash
# Build and launch VSIX in experimental instance
dotnet pack packages/kilo-visualstudio/KiloVisualStudio.csproj
```

Then install the generated `.vsix` file:
1. Build produces `bin\Debug\KiloVisualStudio.vsix`
2. Double-click the `.vsix` file to install
3. Launch Visual Studio - the Kilo Code tool window will appear

## Project Structure

```
packages/kilo-visualstudio/
├── KiloPackage.cs              # Main package class
├── KiloToolWindow.cs           # Tool window definition
├── KiloWebViewControl.cs       # WebView2 host control
├── CliBackendManager.cs        # CLI process manager
├── Guids.cs                    # GUIDs and command IDs
├── KiloPackage.vsct            # Command/menus definition
├── source.extension.vsixmanifest  # VSIX manifest
├── KiloVisualStudio.csproj     # Project file
└── webview/                    # Static webview files
    └── index.html              # WebView entry point
```

## Key Components

### KiloPackage
Main extension package that initializes the CLI backend and registers tool windows.

### KiloToolWindow
Visual Studio tool window that hosts the WebView2 control.

### KiloWebViewControl
WebView2 wrapper that displays the Kilo Code webview UI and handles communication with the extension.

### CliBackendManager
Manages the lifecycle of the `kilo serve` process, including:
- Starting the backend with `--port 0` (random port)
- Parsing the port from stdout
- Waiting for the server to be ready
- Cleaning up on extension shutdown

## Communication Flow

1. **Extension → Backend**: HTTP REST API via `@kilocode/sdk`
2. **Backend → Extension**: Server-Sent Events (SSE) for real-time updates
3. **Extension ↔ Webview**: WebView2 `postMessage` bridge

## Building for Release

```bash
# Release build
dotnet build -c Release

# Create VSIX
dotnet pack -c Release
```

The release VSIX will be in `bin\Release\`.

## Known Limitations

- **Inline Autocomplete**: Requires additional VS Editor API implementation
- **Terminal Integration**: Limited to VS integrated terminal capabilities
- **Git Worktrees**: May require additional configuration

## Troubleshooting

### Backend fails to start
- Check that the CLI binary exists at `packages/opencode/dist/`
- Verify Bun is installed and working
- Check Visual Studio Output window for error messages

### WebView2 not loading
- Ensure WebView2 runtime is installed (Windows 10/11 has it built-in)
- Check that webview files exist in the extension directory
- Verify the backend is running and accessible

### Extension doesn't appear in Visual Studio
- Restart Visual Studio after installation
- Check `Tools > Extensions and Updates`
- Verify VSIX manifest has correct minimum Visual Studio version

## Contributing

See the main [CONTRIBUTING.md](../../CONTRIBUTING.md) for general contribution guidelines.

## License

MIT License - see [LICENSE](../../LICENSE) for details.
