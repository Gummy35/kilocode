# Installation Guide

## Quick Install (Development Mode)

### Option 1: Install from Built DLL (Fastest for Testing)

1. **Build the extension:**
   ```bash
   dotnet build packages\kilo-visualstudio\KiloVisualStudio.csproj -c Debug
   ```

2. **Locate the built files:**
   - DLL: `packages\kilo-visualstudio\bin\Debug\net8.0-windows10.0.17763.0\KiloVisualStudio.dll`

3. **Manually load in Visual Studio:**
   - This method is for testing only - the extension won't appear in the Extensions menu
   - You'll need to use the Experimental Instance of Visual Studio

### Option 2: Build and Install VSIX (Recommended)

1. **Build the VSIX package:**
   ```bash
   cd packages\kilo-visualstudio
   dotnet build -c Debug /p:DeployExtension=false
   ```

2. **Find the VSIX file:**
   - After building, check `bin\Debug\` for a `.vsix` file
   - If no VSIX is generated, you may need to install the VSIX SDK tools

3. **Install the VSIX:**
   - Double-click the `.vsix` file
   - Click "Install" in the Visual Studio Installer dialog
   - Restart Visual Studio when prompted

4. **Open the Kilo Code tool window:**
   - In Visual Studio, go to **View > Other Windows > Kilo Code**
   - Or press `Ctrl+Shift+K` (if keybinding is configured)

## Prerequisites

- **Visual Studio 2022** (version 17.0+) or **Visual Studio 2026**
- **.NET 8.0 SDK** - https://dotnet.microsoft.com/download/dotnet/8.0
- **WebView2 Runtime** (usually pre-installed on Windows 10/11)

## Troubleshooting

### Extension doesn't appear in Visual Studio

1. Check **Tools > Extensions and Updates**
2. Look under "Installed > All" or "Installed > VSIX"
3. If listed but disabled, enable it and restart Visual Studio

### VSIX installation fails

1. Make sure Visual Studio is completely closed
2. Try running the VSIX installer as Administrator
3. Check that your Visual Studio version meets the minimum requirement (17.0+)

### Tool window doesn't open

1. Check the **Output** window in Visual Studio
2. Select **Extensions > Kilo Code** from the output dropdown
3. Look for error messages

### Backend doesn't start

The extension needs the Kilo CLI backend to be available:
1. Build the CLI: `cd packages\opencode && bun run build`
2. The extension will try to spawn `kilo serve` automatically

## Development Workflow

For active development, use the **Experimental Instance**:

1. **Build with deployment:**
   ```bash
   dotnet build packages\kilo-visualstudio\KiloVisualStudio.csproj -c Debug
   ```

2. **Press F5 in Visual Studio** (if opened in IDE) to launch Experimental Instance
   - This creates a isolated Visual Studio instance with your extension
   - Your main Visual Studio installation remains unaffected

3. **Debug:**
   - Set breakpoints in your code
   - The Experimental Instance will attach the debugger automatically

## Uninstall

1. **Tools > Extensions and Updates**
2. Find **Kilo Code for Visual Studio**
3. Click **Uninstall**
4. Restart Visual Studio

## Next Steps

After installation:
1. Open **View > Other Windows > Kilo Code**
2. The tool window will show a loading screen
3. Once the backend starts, you should see the Kilo Code interface
4. If you see errors, check the Output window for details
