# Visual Studio 2026 Extension Feasibility Analysis

## Executive Summary

Creating a Visual Studio 2026 extension with the same features as the VS Code extension is **technically feasible but requires significant architectural adaptation**. The core challenge is that Visual Studio (unlike VS Code) uses a fundamentally different plugin architecture based on Managed Extensibility Framework (MEF) and the Visual Studio SDK (VSIX), not a Node.js/JavaScript runtime.

## Current Architecture Analysis

### Kilo Code Stack (VS Code)

```
┌─────────────────────────────────────────────────────────┐
│  VS Code Extension (packages/kilo-vscode/)              │
│  - TypeScript/Node.js                                    │
│  - Webview UI (SolidJS)                                  │
│  - Spawns kilo serve as child process                   │
└─────────────────────────────────────────────────────────┘
                          │
                          │ HTTP + SSE (localhost)
                          │
┌─────────────────────────────────────────────────────────┐
│  CLI Backend (packages/opencode/)                        │
│  - Bun runtime (TypeScript)                              │
│  - Hono HTTP server                                      │
│  - AI agent runtime, tools, sessions                     │
│  - Auto-generated SDK (@kilocode/sdk)                    │
└─────────────────────────────────────────────────────────┘
```

**Key Insight**: The VS Code extension is a **thin client** that:
1. Spawns the CLI backend as a child process
2. Communicates via HTTP REST + SSE over localhost
3. Renders UI in a webview (SolidJS)

### Visual Studio Extension Architecture

Visual Studio extensions use:
- **VSIX package format** (based on MEF - Managed Extensibility Framework)
- **C#/.NET** for extension code (not TypeScript/Node.js)
- **WPF/WinForms** for UI (not webviews/SolidJS)
- **Visual Studio SDK** for editor integration
- **No Node.js runtime** built-in (requires bundling or external process)

## Feasibility Assessment

### ✅ Feasible Components

| Component | Feasibility | Notes |
|---|---|---|
| **CLI Backend** | ✅ Direct reuse | The `packages/opencode/` backend can run as an external process unchanged |
| **HTTP/SSE Protocol** | ✅ Direct reuse | REST API + SSE works from any HTTP client |
| **SDK** | ✅ Reusable | TypeScript SDK can be regenerated for C# or used via HTTP directly |
| **AI Agent Runtime** | ✅ Direct reuse | All logic lives in the CLI backend |
| **Model Providers** | ✅ Direct reuse | 500+ models handled by backend |
| **Tool System** | ✅ Direct reuse | File ops, terminal commands, MCP all in backend |

### ⚠️ Challenging Components

| Component | Challenge | Mitigation |
|---|---|---|
| **UI Layer** | VS Code uses webviews (SolidJS); VS Studio uses WPF/WinForms | Build native C# UI or embed WebView2 |
| **Extension Architecture** | VS Code: Node.js; VS Studio: MEF/.NET | Complete rewrite of extension shell |
| **Process Spawning** | VS Code: child_process; VS Studio: System.Diagnostics | Adapt to .NET process management |
| **Editor Integration** | VS Code: VS Code API; VS Studio: EnvDTE/VS SDK | Use Visual Studio SDK APIs |
| **Terminal Integration** | VS Code: VS Code terminal API; VS Studio: PackageTerminal | Use IVsTerminal interfaces |

### ❌ Non-Feasible (Without Major Work)

| Component | Reason |
|---|---|
| **Direct Code Sharing** | Extension code is platform-specific (TypeScript vs C#) |
| **Webview UI Reuse** | VS Code webviews don't exist in Visual Studio |
| **Extension Lifecycle** | Completely different activation/deactivation models |

## Recommended Approach

### Option 1: WebView2-Based Extension (Recommended)

**Architecture**:
```
┌─────────────────────────────────────────────────────────┐
│  Visual Studio Extension (C# VSIX)                       │
│  - VSIX package with MEF components                      │
│  - WebView2 control (Edge Chromium embedded)             │
│  - Reuse existing SolidJS webview code                   │
└─────────────────────────────────────────────────────────┘
                          │
                          │ HTTP + SSE (localhost)
                          │
┌─────────────────────────────────────────────────────────┐
│  CLI Backend (packages/opencode/)                        │
│  - Run as external process (unchanged)                   │
│  - Or bundle as native binary                            │
└─────────────────────────────────────────────────────────┘
```

**Advantages**:
- ✅ Reuse 100% of existing webview UI code (SolidJS)
- ✅ Minimal changes to CLI backend
- ✅ WebView2 is built into Windows 10/11
- ✅ Faster development (UI already exists)

**Disadvantages**:
- ⚠️ Requires WebView2 runtime (usually pre-installed)
- ⚠️ Not native Visual Studio look-and-feel
- ⚠️ Additional memory overhead for WebView

**Implementation Effort**: 3-6 months for MVP

### Option 2: Native C# UI

**Architecture**:
```
┌─────────────────────────────────────────────────────────┐
│  Visual Studio Extension (C# VSIX)                       │
│  - VSIX package with MEF components                      │
│  - WPF/WinForms UI (native Visual Studio theme)          │
│  - HTTP client to communicate with backend               │
└─────────────────────────────────────────────────────────┘
                          │
                          │ HTTP + SSE (localhost)
                          │
┌─────────────────────────────────────────────────────────┐
│  CLI Backend (packages/opencode/)                        │
│  - Run as external process (unchanged)                   │
└─────────────────────────────────────────────────────────┘
```

**Advantages**:
- ✅ Native Visual Studio look-and-feel
- ✅ Better performance (no WebView overhead)
- ✅ Follows Visual Studio extension best practices

**Disadvantages**:
- ❌ Complete rewrite of UI layer (months of work)
- ❌ No code reuse from existing SolidJS components
- ❌ Requires C#/.NET expertise

**Implementation Effort**: 6-12 months for MVP

## Feature Parity Analysis

### Core Features (All Feasible)

| Feature | Implementation Notes |
|---|---|
| Chat sidebar/panel | WebView2: reuse existing webview; Native: rebuild in WPF |
| Agent Manager | WebView2: reuse existing; Native: rebuild with tabs/worktrees |
| Inline autocomplete | Requires VS Editor API (IVsTextViewConnectionProvider) |
| Terminal integration | VS SDK IVsTerminal interfaces |
| File operations | Via CLI backend (HTTP API) |
| Git integration | Via CLI backend or VS SDK |
| Settings UI | WebView2: reuse; Native: rebuild |
| Marketplace | WebView2: reuse; Native: rebuild |
| Diff viewer | WebView2: reuse; Native: use VS diff viewer |

### Platform-Specific Features

| VS Code Feature | Visual Studio Equivalent |
|---|---|
| `vscode.window.createWebviewPanel` | `ToolWindowPane` with WebView2 |
| `vscode.languages.registerCompletionItemProvider` | `VsCompletionSource` |
| `vscode.window.createTerminal` | `IVsTerminal` |
| `vscode.workspace.openTextDocument` | `DocumentManager` |
| `vscode.commands.executeCommand` | `OleMenuCommand` |

## Technical Requirements

### For WebView2 Approach

1. **Visual Studio Extension Infrastructure**:
   - VSIX manifest
   - MEF export for tool window
   - Package initialization code

2. **WebView2 Integration**:
   - Microsoft.Web.WebView2 NuGet package
   - Navigate to bundled webview or local server
   - PostMessage bridge for extension↔webview communication

3. **CLI Backend Management**:
   - Spawn `kilo serve` process using `System.Diagnostics`
   - Parse port from stdout
   - Manage lifecycle (start/stop with extension)

4. **Editor Integration**:
   - VS SDK for editor commands
   - Completion provider implementation
   - Terminal integration

### For Native C# Approach

1. **All WebView2 requirements, plus**:
2. **Complete UI Rewrite**:
   - WPF/XAML for all panels
   - Convert SolidJS components to WPF
   - Implement all interactions in C#

## Risk Assessment

| Risk | Severity | Mitigation |
|---|---|---|
| **Performance** | Medium | WebView2 has ~200MB memory overhead; native UI is lighter |
| **Development Time** | High | WebView2: 3-6 months; Native: 6-12 months |
| **Maintenance** | Medium | Two codebases to maintain (extension + backend) |
| **User Experience** | Medium | WebView2 may feel "foreign" in Visual Studio |
| **API Compatibility** | Low | VS SDK has equivalents for most VS Code APIs |

## Recommended Next Steps

1. **Prototype Phase (2-4 weeks)**:
   - Create minimal VSIX with WebView2 tool window
   - Spawn CLI backend process
   - Connect to HTTP/SSE API
   - Display existing webview UI

2. **MVP Phase (2-3 months)**:
   - Implement core chat functionality
   - Add basic editor integration (commands, context menu)
   - Implement settings persistence
   - Test with real users

3. **Feature Parity Phase (2-4 months)**:
   - Inline autocomplete
   - Terminal integration
   - Agent Manager
   - Marketplace
   - Diff viewer

4. **Polish Phase (1-2 months)**:
   - Performance optimization
   - Visual Studio theme integration
   - Bug fixes
   - Documentation

## Alternative: Cross-Platform Solution

Consider **Visual Studio Code for Visual Studio** - Microsoft's project to run VS Code extensions inside Visual Studio. However, this is still in preview and not production-ready.

## Conclusion

**Recommendation**: Proceed with **Option 1 (WebView2-based)** for fastest time-to-market with maximum code reuse.

**Estimated Timeline**:
- MVP: 3-6 months
- Feature Parity: 6-9 months
- Production Ready: 9-12 months

**Key Success Factors**:
1. CLI backend remains unchanged (single source of truth)
2. WebView2 enables 100% UI code reuse
3. Focus on core features first (chat, autocomplete)
4. Incremental feature rollout based on user feedback

**Open Questions**:
1. What is the target Visual Studio version (2022 vs 2026)?
2. Are there specific Visual Studio-only features required?
3. What is the budget/timeline constraint?
4. Is there a C#/.NET development team available?
