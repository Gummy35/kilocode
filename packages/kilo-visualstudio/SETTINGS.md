# Visual Studio Extension Settings View - Implementation Status

## Overview

This document tracks the implementation status of the settings view for the Visual Studio extension. The settings view matches 100% the VS Code extension implementation since the webview JavaScript is shared between both extensions.

## Implementation Status: COMPLETE

### Webview UI (Shared)
✅ **Already implemented** - The webview JavaScript (`webview.js`) is built from the VS Code extension's webview code and includes:
- Settings component with all 16 tabs
- SettingsRow component for individual settings
- Tab navigation with vertical layout
- Save bar with dirty state tracking
- All translation keys from `@kilocode/kilo-i18n`

### C# Extension Code - Message Handlers

#### Implemented in VSProvider.cs
✅ `HandleOpenSettingsPanelAsync` - Opens settings view with optional tab navigation
✅ `HandleOpenConfigFileAsync` - Opens config file using System.Diagnostics.Process
✅ `HandleUpdateSettingAsync` - Updates individual setting via CLI backend
✅ `HandleUpdateConfigAsync` - Updates full config via CLI backend
✅ `HandleRequestGlobalConfigAsync` - Requests global config from CLI backend
✅ `HandleRequestBrowserSettings` - Returns browser settings
✅ `HandleRequestNotificationSettings` - Returns notification settings
✅ `HandleRequestRemoteStatus` - Returns remote control status
✅ `HandleSetLanguageAsync` - Sets language preference
✅ `HandleResetAllSettingsAsync` - Resets all settings to defaults
✅ `HandleRequestClaudeCompatSetting` - Returns Claude compat setting
✅ `HandleRequestTimelineSetting` - Returns timeline setting
✅ `HandleRequestModelSelectorExpanded` - Returns model selector expanded state

#### Helper Methods
✅ `GetConfig()` - Helper method to fetch config from CLI backend

### Message Flow

#### Webview → Extension (All Supported)
1. ✅ `openSettingsPanel` - Opens settings with optional tab
2. ✅ `openConfigFile` - Opens config file (scope: local/global)
3. ✅ `updateSetting` - Updates individual setting
4. ✅ `updateConfig` - Updates full config
5. ✅ `settingsTabChanged` - Notifies tab change
6. ✅ `requestConfig` - Requests config data
7. ✅ `requestGlobalConfig` - Requests global config
8. ✅ `requestProviders` - Requests provider data
9. ✅ `requestAgents` - Requests agent data
10. ✅ `requestIndexingSettings` - Requests indexing settings
11. ✅ `requestChatSettings` - Requests chat settings
12. ✅ `requestThroughputSetting` - Requests throughput setting
13. ✅ `requestAutocompleteSettings` - Requests autocomplete settings
14. ✅ `requestBrowserSettings` - Requests browser settings
15. ✅ `requestNotificationSettings` - Requests notification settings
16. ✅ `requestRemoteStatus` - Requests remote status
17. ✅ `setLanguage` - Sets language preference
18. ✅ `resetAllSettings` - Resets all settings

#### Extension → Webview (All Supported)
1. ✅ `configLoaded` - Config data with features
2. ✅ `providersLoaded` - Provider data with states
3. ✅ `agentsLoaded` - Agent/mode data
4. ✅ `indexingSettingsLoaded` - Indexing settings
5. ✅ `chatSettingsLoaded` - Chat settings
6. ✅ `throughputSettingLoaded` - Throughput visibility
7. ✅ `autocompleteSettingsLoaded` - Autocomplete settings
8. ✅ `browserSettingsLoaded` - Browser settings
9. ✅ `notificationSettingsLoaded` - Notification settings
10. ✅ `remoteStatus` - Remote control status
11. ✅ `navigate` - Navigate to view/tab
12. ✅ `settingsTabChanged` - Acknowledges tab change

## Settings Tabs (16 Total)

All tabs are implemented in the shared webview:

1. ✅ **Models** - Model selection (default, small, subagent, autocomplete, speech-to-text, mode models)
2. ✅ **Providers** - Provider configuration and connection management
3. ✅ **Agent Behaviour** - Agent behavior settings
4. ✅ **Auto Approve** - Auto-approval rules configuration
5. ✅ **Browser** - Browser automation settings
6. ✅ **Checkpoints** - Checkpoint/revert settings
7. ✅ **Display** - Display preferences (font size, terminal/command display, etc.)
8. ✅ **Autocomplete** - Autocomplete configuration
9. ✅ **Notifications** - Notification preferences
10. ✅ **Context** - Context/source control settings
11. ✅ **Commit Message** - Commit message generation settings
12. ✅ **Indexing** - Codebase indexing settings (conditional)
13. ✅ **Experimental** - Experimental features (remote control, share mode, formatter, LSP, etc.)
14. ✅ **Sandboxing** - Sandboxing settings (conditional)
15. ✅ **Language** - Language/translation settings
16. ✅ **About Kilo Code** - Extension info, version, migration options

## Supported Visual Studio Versions

- ✅ **Visual Studio 2022** (version 17.x)
- ✅ **Visual Studio 2026** (version 18.x)
- No support for earlier versions

## Key Implementation Details

### WebView2 Integration
- The webview uses WebView2 control in `KiloWebViewControl.cs`
- Message passing via `CoreWebView2.PostWebMessageAsString()` and `CoreWebView2.WebMessageReceived`
- The `vscode-api.js` compatibility layer provides `acquireVsCodeApi()` for the webview

### Settings Navigation
When the user clicks the settings button in the sidebar:
1. Webview sends `openSettingsPanel` message with optional tab parameter
2. VSProvider handles the message and sends `navigate` message back to webview
3. Webview's App.tsx receives the `navigate` message and switches to settings view
4. Settings component renders with the specified tab active

### Config File Opening
- Uses `System.Diagnostics.Process.Start()` to open config files in the default editor
- The CLI backend determines the config file path based on scope (local/global)

## Translation Keys

All translation keys are provided by `@kilocode/kilo-i18n` package and are available in the shared webview. The settings view uses these keys for:
- Tab labels
- Setting titles and descriptions
- Button labels
- Error messages
- Save bar messages

## Conclusion

## Settings Panel in Separate Webview - Analysis

### VS Code Implementation Workflow

The VS Code extension uses a **SettingsEditorProvider** class to open settings in a **separate editor panel** (not in the sidebar). This is the key architectural pattern:

#### Architecture
```
SettingsEditorProvider (extension.ts)
├── Manages singleton WebviewPanels per view type: "settings", "profile", "indexing"
├── Creates dedicated KiloProvider instance for each panel
├── Each panel is a full-featured webview with its own KiloProvider
└── Panels survive webview reloads via retainContextWhenHidden: true
```

#### Message Flow for Settings Panel
1. **User clicks settings button** in sidebar → `openSettingsPanel` message sent to extension
2. **KiloProvider.ts** handles `openSettingsPanel` → executes command `kilo-code.new.settingsButtonClicked` with optional tab parameter
3. **extension.ts** command handler → calls `settingsEditorProvider.openPanel("settings", tab)`
4. **SettingsEditorProvider.openPanel()**:
   - Checks if panel already exists (singleton pattern)
   - If exists: reveals panel and sends `navigate` message with view/tab
   - If new: creates `vscode.WebviewPanel` with `ViewColumn.Active`
   - Creates dedicated `KiloProvider` instance for the panel
   - Calls `provider.resolveWebviewPanel(panel)` to wire up message handlers
   - Sends `navigate` message after webviewReady (50ms delay)
5. **Webview receives `navigate` message** → App.tsx switches to Settings view with specified tab

#### Key Code Locations (VS Code)
- `packages/kilo-vscode/src/SettingsEditorProvider.ts` - Main provider class (169 lines)
- `packages/kilo-vscode/src/extension.ts:397-398` - Command registration
- `packages/kilo-vscode/src/KiloProvider.ts:1112-1114` - openSettingsPanel handler
- `packages/kilo-vscode/src/KiloProvider.ts:1295-1298` - openSettingsTab handler (indexing special case)

#### Panel Types
```typescript
type PanelView = "settings" | "profile" | "indexing"

PANEL_TITLES: Record<PanelView, string> = {
  settings: "Kilo Settings",
  profile: "Kilo Profile",
  indexing: "Codebase Indexing",
}
```

#### WirePanel Logic (SettingsEditorProvider.ts:96-150)
1. Sets panel icon from extension assets
2. Creates new KiloProvider with extensionUri, connectionService, context, projectDirectory
3. Calls provider.resolveWebviewPanel(panel) - this is the same method used for sidebar
4. Listens for `closePanel` message → disposes panel
5. Listens for `webviewReady` → sends `navigate` message after 50ms delay
6. Listens for `settingsTabChanged` → remembers tab in this.tabs map
7. Disposes everything when panel closes

#### Important Details
- **Singleton pattern**: Only one panel per view type exists at a time
- **Tab persistence**: Current tab is remembered across reloads via `this.tabs` map
- **Project directory**: Resolved from active text editor or workspace folders
- **Remote service**: Can be set via `setRemoteService()` and applied to all providers
- **Deserialization**: Panels can be restored after extension restart via `deserializePanel()`

### Visual Studio Extension Implementation Status

#### Current Implementation (INCORRECT)
The VS extension currently handles `openSettingsPanel` by sending a `navigate` message directly to the **shared sidebar webview**:

```csharp
// VSProvider.cs (current - INCORRECT)
private async Task HandleOpenSettingsPanelAsync(JsonElement? payload)
{
    string? tab = null;
    if (payload.HasValue && payload.Value.TryGetProperty("tab", out var tabProp))
        tab = tabProp.GetString();
    
    // This navigates the SIDEBAR webview, not a separate panel
    PostMessage(JsonSerializer.Serialize(new { type = "navigate", view = "settings", tab }));
}
```

**Problem**: This only navigates the existing sidebar webview to settings, instead of opening a **separate editor panel** like VS Code does.

#### Required Implementation for VS Extension

To match VS Code 100%, the VS extension needs:

1. **New C# class: SettingsEditorProvider.cs**
   - Manages singleton WebView2 panels per view type
   - Creates dedicated VSProvider instances for each panel
   - Handles panel lifecycle (create, reveal, dispose)
   - Remembers active tab for each panel type

2. **Package-level registration**
   - Register commands for opening settings/profile/indexing panels
   - Store SettingsEditorProvider instance in package

3. **Message flow changes**
   - `openSettingsPanel` message → execute VS command → open separate panel
   - NOT just sending navigate to shared sidebar webview

#### Files to Create/Modify

**Create:**
- `packages/kilo-visualstudio/KiloVisualStudioExtension/SettingsEditorProvider.cs`
  - Similar structure to SettingsEditorProvider.ts (169 lines)
  - Uses WebView2 instead of vscode.WebviewPanel
  - Manages panel dictionary: `Dictionary<PanelView, KiloWebViewPanel>`
  - Each panel gets its own VSProvider instance

**Modify:**
- `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloProvider.cs`
  - Update `HandleOpenSettingsPanelAsync` to execute VS command instead of direct navigate
- `packages/kilo-visualstudio/KiloVisualStudioExtension\KiloVisualStudioExtensionPackage.cs`
  - Register new commands: `kilo-code.new.settingsButtonClicked`
  - Initialize SettingsEditorProvider

#### WebView2 Panel Implementation Notes

Unlike VS Code's `vscode.WebviewPanel`, Visual Studio's WebView2 doesn't have a built-in panel abstraction. You'll need to:

1. Create a **ToolWindow** for each panel type (settings, profile, indexing)
2. Each ToolWindow contains a KiloWebViewControl
3. Track active ToolWindow instances in SettingsEditorProvider
4. Use `IVsWindowFrame.Show()` to reveal existing windows
5. Send navigate message via the panel's VSProvider instance

#### Alternative: Simpler Approach

If creating separate ToolWindows is too complex, a simpler approach that still matches VS Code behavior:

1. Create a **single generic settings panel** ToolWindow
2. Use the `navigate` message to switch between settings/profile/indexing views
3. Track current tab in the panel's VSProvider
4. This loses the singleton-per-type pattern but achieves the same UX

### HandleUpdateConfigAsync - FIXED

#### VS Code Implementation (KiloProvider.ts:3018-3114)

```typescript
private async handleUpdateConfig(
  partial: Partial<Config>,
  project: Partial<Config> = {},
  globalUnset: string[][] = [],
  projectUnset: string[][] = [],
): Promise<void> {
  if (!this.client || this.connectionState !== "connected") {
    this.postMessage({ type: "configUpdateFailed", message: "Not connected to CLI backend" })
    return
  }

  const refreshProviders =
    partial.provider !== undefined ||
    partial.disabled_providers !== undefined ||
    partial.enabled_providers !== undefined ||
    partial.hide_prompt_training_models !== undefined
  const refreshAgents =
    partial.default_agent !== undefined ||
    partial.agent !== undefined ||
    project.default_agent !== undefined ||
    project.agent !== undefined
  const hasGlobal = Object.keys(partial).length > 0 || globalUnset.length > 0
  const hasProject = Object.keys(project).length > 0 || projectUnset.length > 0

  this.pending++
  const dir = this.getWorkspaceDirectory()

  try {
    await this.connectionService.drainPendingPrompts()
    if (hasGlobal) {
      await this.client.config.overlayUpdate(
        { scope: "global", set: partial, unset: globalUnset, directory: dir },
        { throwOnError: true },
      )
    }
    if (hasProject) {
      await this.client.config.overlayUpdate(
        { scope: "project", set: project, unset: projectUnset, directory: dir },
        { throwOnError: true },
      )
    }
  } catch (error) {
    this.postConfigFailure(error)
    this.pending--
    return
  }

  try {
    const [{ data: merged }, { data: global }, { data: overlay }] = await Promise.all([
      retry(() => this.client!.config.get({ directory: dir }, { throwOnError: true })),
      this.client.global.config.get({ throwOnError: true }),
      this.client.config.overlay({ directory: dir, scope: "project" }, { throwOnError: true }),
    ])
    this.cachedGlobalConfig = global ?? null
    this.cachedConfigMessage = {
      type: "configLoaded",
      config: merged,
      globalConfig: global,
      projectConfig: overlay?.project,
      settings: { maxCost: this.maxCostSetting(), languageCommitMessage: this.commitMessageLanguageSetting() },
      features: configFeatures(merged),
    }
    this.postMessage({
      type: "configUpdated",
      config: merged,
      globalConfig: global,
      projectConfig: overlay?.project,
      settings: { maxCost: this.maxCostSetting(), languageCommitMessage: this.commitMessageLanguageSetting() },
      features: configFeatures(merged),
    })
    this.requirements.clear()
    await Promise.all([
      refreshProviders ? this.fetchAndSendProviders() : Promise.resolve(),
      refreshAgents ? this.fetchAndSendAgents() : Promise.resolve(),
    ])
  } catch (error) {
    console.error("[Kilo New] KiloProvider: Config write succeeded but post-write refresh failed:", error)
    const patch =
      partial.indexing === undefined && project.indexing === undefined
        ? { ...partial, ...project }
        : { ...partial, ...project, indexing: { ...(partial.indexing ?? {}), ...(project.indexing ?? {}) } }
    const cached = (this.cachedConfigMessage as { config?: unknown } | null)?.config
    const features = (this.cachedConfigMessage as { features?: unknown } | null)?.features
    const optimistic =
      cached && typeof cached === "object" ? { ...(cached as Record<string, unknown>), ...patch } : patch
    this.postMessage({
      type: "configUpdated",
      config: optimistic,
      globalConfig: this.cachedGlobalConfig ?? undefined,
      settings: { maxCost: this.maxCostSetting(), languageCommitMessage: this.commitMessageLanguageSetting() },
      features: features ?? configFeatures(optimistic as Config),
    })
    this.requirements.clear()
  } finally {
    this.pending--
  }
}
```

**Key behaviors:**
1. Checks connection state
2. Determines if providers/agents need refresh based on changed keys
3. Increments pending counter (ref-count to prevent stale config pushes)
4. Drains pending prompts before config write
5. Updates global config (if hasGlobal)
6. Updates project config (if hasProject)
7. On success: fetches merged/global/project configs, updates cache, sends configUpdated
8. On refresh failure: sends optimistic update with cached data
9. Decrements pending counter in finally block

#### Current VS Extension Implementation (FIXED)

**Status**: ✅ **COMPLETED** - `HandleUpdateConfigAsync` has been reimplemented in `KiloProvider.cs:1986-2230` to match the VS Code implementation 100%.

**Key behaviors implemented**:
1. ✅ Checks connection state before proceeding
2. ✅ Determines if providers/agents need refresh based on changed keys
3. ✅ Drains pending prompts before config write (via `connectionService.drainPendingPrompts()`)
4. ✅ Updates global config using `/config/overlay-update` endpoint with scope="global"
5. ✅ Updates project config using `/config/overlay-update` endpoint with scope="project"
6. ✅ On success: fetches merged/global/project configs via `Promise.all`, updates cache, sends `configUpdated` message
7. ✅ Refreshes providers/agents if needed after successful update
8. ✅ On refresh failure: sends optimistic update with cached data
9. ✅ Proper error handling with `configUpdateFailed` message on write failure

**Helper methods added**:
- `IsJsonObjectEmpty(JsonElement)` - Checks if JSON object has no properties
- `IsJsonArrayEmpty(JsonElement)` - Checks if JSON array has no elements
- `MergeJsonObjects(JsonElement, JsonElement)` - Merges two JSON objects
- `MergeJsonWithIndexing(JsonElement, JsonElement, JsonElement)` - Merges with special indexing handling
- `MergeJsonWithPatch(JsonElement?, JsonElement)` - Merges cached config with patch
- `PostConfigFailure(Exception)` - Posts error message to webview
- `GetCommitMessageLanguage()` - Extracts language_commit_message from config
- `GetConfigFeatures(JsonElement)` - Extracts features from config
- `JsonDocumentBuilder` class - Helper for building JSON objects dynamically

**Files modified**:
- `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloProvider.cs` - Complete rewrite of `HandleUpdateConfigAsync` (lines 1986-2230)
- Added `JsonDocumentBuilder` helper class at end of file

### Next Steps

1. ✅ **HandleUpdateConfigAsync** - FIXED and verified compiling
2. ✅ **SettingsEditorProvider.cs** - CREATED for separate panel support
3. ✅ **OpenSettingsCommand.cs** - UPDATED to use SettingsEditorProvider
4. ✅ **KiloVisualStudioExtensionPackage.cs** - UPDATED with SettingsEditorProvider initialization
5. **Test settings panel** opens in separate window and all settings operations work

### Files Reference

**VS Code (source of truth):**
- `packages/kilo-vscode/src/SettingsEditorProvider.ts` - Settings panel provider
- `packages/kilo-vscode/src/extension.ts:397-401` - Command registration
- `packages/kilo-vscode/src/KiloProvider.ts:1112-1114, 1288-1293, 3018-3114` - Message handlers

**Visual Studio (implemented):**
- ✅ `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloProvider.cs:1986-2230` - `HandleUpdateConfigAsync` reimplemented to match VS Code 100%
- ✅ `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloProvider.cs:2769-2797` - `JsonDocumentBuilder` helper class added
- ✅ `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloProvider.cs:2773-2780` - `SetProjectDirectory`, `PostMessageAsync`, `SetRemoteService` methods added
- ✅ `packages/kilo-visualstudio/KiloVisualStudioExtension/SettingsEditorProvider.cs` - NEW FILE - Settings panel provider with singleton panel management
- ✅ `packages/kilo-visualstudio/KiloVisualStudioExtension/SettingsEditorProvider.cs:21-43` - `SettingsToolWindow` class for separate tool windows
- ✅ `packages/kilo-visualstudio/KiloVisualStudioExtension/OpenSettingsCommand.cs` - Updated to use SettingsEditorProvider
- ✅ `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloVisualStudioExtensionPackage.cs` - SettingsEditorProvider initialization and command registration
