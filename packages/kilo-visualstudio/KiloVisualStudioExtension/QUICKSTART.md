# Kilo Visual Studio Extension

## Quick Start

### Development

```bash
# Build and run in debug mode
bun run build.ts

# Build for release
bun run build.ts --release
```

### Install the Extension

1. Build produces `bin/Debug/KiloVisualStudio.vsix` or `bin/Release/KiloVisualStudio.vsix`
2. Double-click the `.vsix` file to install
3. Restart Visual Studio
4. Open **View > Other Windows > Kilo Code**

### Uninstall

- **Tools > Extensions and Updates > Installed > Kilo Code > Uninstall**

## Development Workflow

### 1. Modify Extension Code

```bash
# Edit C# files in packages/kilo-visualstudio/
# Then rebuild
bun run build.ts
```

### 2. Modify Webview (SolidJS)

```bash
# Edit files in packages/kilo-vscode/webview-ui/
# Build storybook
cd packages/kilo-vscode
bun run build-storybook

# Copy to visualstudio package
# (Manual step for now - automate in build.ts)
```

### 3. Debug the Extension

1. Set breakpoints in C# code
2. Run `dotnet debug` or attach debugger to devenv.exe
3. For webview debugging, use browser DevTools via WebView2

## Testing

### Unit Tests

```bash
dotnet test
```

### Integration Tests

1. Build the VSIX
2. Install in experimental instance
3. Manually test features

## Deployment

### Local Testing

```bash
# Build and install locally
bun run build.ts
```

### Share with Others

1. Build release VSIX: `bun run build.ts --release`
2. Share the `.vsix` file
3. Recipients double-click to install

### Publish to Marketplace

See [DEPLOYMENT.md](DEPLOYMENT.md) for marketplace publishing instructions.
