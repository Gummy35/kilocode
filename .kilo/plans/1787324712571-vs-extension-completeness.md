# PORT-VS-001: Visual Studio Extension Message Handler Completeness

**Status:** Planning  
**Created:** 2026-08-23  
**Depends On:** `PORT-CORE-002` (Auth service parity - Complete)  
**Blocks:** None

---

## Objective

Achieve functional parity between the Visual Studio extension and VS Code extension by implementing all missing message handlers identified in the audit.

This task addresses **~60+ missing or incomplete message handlers** that prevent the Visual Studio extension from having full feature parity with the VS Code extension.

---

## Background

An audit of `ProcessMessageAsync` in `VSProvider.cs` compared against `KiloProvider.ts` in VS Code revealed significant gaps in message handler coverage. While core authentication and session management are implemented, many critical handlers are missing or incomplete.

---

## Scope

### In Scope

1. **Core Messaging Handlers**
   - `sendMessage` - Send message to session
   - `sendCommand` - Send command to session

2. **MCP (Model Context Protocol) Handlers**
   - `connectMcp` - Connect MCP server
   - `disconnectMcp` - Disconnect MCP server
   - `authenticateMcp` - Authenticate MCP server
   - `removeMcp` - Remove MCP server

3. **Skills & Agents Handlers**
   - `removeSkill` - Remove skill
   - `removeAgent` - Remove agent
   - `requestAgentRequirements` - Request agent requirements

4. **Session Management Handlers**
   - `forkSession` - Fork session
   - `openSubAgentViewer` - Open sub-agent viewer

5. **Provider Management**
   - `saveCustomProvider` - Fix incomplete implementation (currently only handles API key)
   - `fetchCustomProviderModels` - Fetch custom provider models

6. **Settings Handlers**
   - `openSettingsTab` - Open settings tab
   - `setLanguage` - Set language
   - `requestBrowserSettings` - Request browser settings
   - `requestClaudeCompatSetting` - Request Claude compat setting
   - `requestNotificationSettings` - Request notification settings
   - `requestTimelineSetting` - Request timeline setting
   - `resetAllSettings` - Reset all settings
   - `resetReadNotifications` - Reset read notifications

7. **Remote & Cloud Handlers**
   - `toggleRemote` - Toggle remote
   - `setRemoteEnabled` - Set remote enabled
   - `requestRemoteStatus` - Request remote status
   - `requestCloudSessions` - Request cloud sessions
   - `requestCloudSessionData` - Request cloud session data

8. **File & Context Handlers**
   - `requestFileSearch` - Request file search
   - `requestSessionSearch` - Request session search
   - `requestFilePicker` - Request file picker
   - `requestTerminalContext` - Request terminal context

9. **Other Handlers**
   - `openKiloClaw` - Open Kilo Claw
   - `openMarketplacePanel` - Open marketplace panel
   - `saveImage` - Save image
   - `reload` - Reload
   - `sessionCostAlertResponse` - Session cost alert response
   - `requestSandboxStatus` - Request sandbox status
   - `requestSandboxDefault` - Request sandbox default
   - `setSandboxDefault` - Set sandbox default
   - `toggleSandbox` - Toggle sandbox
   - `chatCompletionAccepted` - Chat completion accepted
   - `telemetry` - Telemetry
   - `persistVariant` - Persist variant
   - `persistRecents` - Persist recents
   - `toggleFavorite` - Toggle favorite
   - `enhancePrompt` - Enhance prompt

10. **Migration Handlers**
    - `requestMigrationData` - Request migration data
    - `startMigration` - Start migration
    - `skipLegacyMigration` - Skip legacy migration
    - `clearLegacyData` - Clear legacy data
    - `finalizeLegacyMigration` - Finalize legacy migration

### Out of Scope

1. **Platform-Specific Handlers** (VS Code-only)
   - `anacondaDesktopStatus`
   - `anacondaDesktopOpen`
   - `anacondaDesktopSync`
   - `cancelAnacondaDesktopRequest`
   - `openVSCodeSettings` (VS Code-specific)

2. **Deferred to Future Tasks**
   - Handlers that require significant Visual Studio-specific implementation
   - Handlers that depend on backend endpoints not yet available

---

## Acceptance Criteria

### Core Messaging
- [ ] `sendMessage` sends message to session via backend
- [ ] `sendCommand` sends command to session via backend

### MCP Support
- [ ] `connectMcp` connects MCP server
- [ ] `disconnectMcp` disconnects MCP server
- [ ] `authenticateMcp` initiates MCP authentication
- [ ] `removeMcp` removes MCP server

### Skills & Agents
- [ ] `removeSkill` removes skill from config
- [ ] `removeAgent` removes agent from config
- [ ] `requestAgentRequirements` fetches agent requirements

### Session Management
- [ ] `forkSession` forks current session
- [ ] `openSubAgentViewer` opens sub-agent viewer panel

### Provider Management
- [ ] `saveCustomProvider` fully implements config update (not just API key)
- [ ] `fetchCustomProviderModels` fetches models from custom provider

### Settings
- [ ] All settings handlers properly read/write config
- [ ] `resetAllSettings` resets all settings to defaults
- [ ] `resetReadNotifications` clears read notifications

### Remote & Cloud
- [ ] Remote handlers toggle and query remote state
- [ ] Cloud session handlers fetch cloud session data

### File & Context
- [ ] File search handlers return file search results
- [ ] Terminal context handler returns terminal context

### Other
- [ ] All other handlers implemented or explicitly deferred

### Build & Tests
- [ ] Build succeeds with 0 errors
- [ ] All existing tests pass
- [ ] New handlers have unit tests where applicable

---

## Implementation Plan

### Phase 1: Critical Core Handlers (Week 1)

**Goal**: Implement essential messaging functionality.

1. **`sendMessage` Handler**
   ```csharp
   public async Task HandleSendMessageAsync(JsonElement? payload)
   {
       var sessionID = payload?.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : null;
       var text = payload?.Value.TryGetProperty("text", out var t) ? t.GetString() : null;
       
       // Validate
       if (string.IsNullOrEmpty(sessionID) || string.IsNullOrEmpty(text)) return;
       
       var nswagClient = Provider.GetNswagClient();
       if (nswagClient == null) return;
       
       var directory = Provider.GetSSEHelper().ResolveDirectory(sessionID);
       
       var part = new Parts2();
       part.AdditionalProperties["type"] = "text";
       part.AdditionalProperties["text"] = text;
       
       await nswagClient.Session_prompt_asyncAsync(sessionID, directory, "", new Body24 
       { 
           Parts = new System.Collections.Generic.List<Parts2> { part } 
       });
   }
   ```

2. **`sendCommand` Handler**
   - Similar pattern to `sendMessage` but for commands

**Validation**:
- [ ] Can send messages to sessions
- [ ] Messages appear in session history

---

### Phase 2: MCP Support (Week 2)

**Goal**: Implement full MCP server management.

1. **`connectMcp` Handler**
   ```csharp
   public async Task HandleConnectMcpAsync(JsonElement? payload)
   {
       var name = payload?.Value.TryGetProperty("name", out var n) ? n.GetString() : null;
       var directory = Provider.GetWorkspaceDirectory();
       
       var nswagClient = Provider.GetNswagClient();
       if (nswagClient == null) return;
       
       await nswagClient.Mcp_connectAsync(name, directory, "");
   }
   ```

2. **`disconnectMcp` Handler**
3. **`authenticateMcp` Handler**
4. **`removeMcp` Handler**

**Validation**:
- [ ] Can connect/disconnect MCP servers
- [ ] MCP status updates correctly

---

### Phase 3: Skills & Agents (Week 3)

**Goal**: Implement skills and agents management.

1. **`removeSkill` Handler**
   ```csharp
   public async Task HandleRemoveSkillAsync(JsonElement? payload)
   {
       var skillName = payload?.Value.TryGetProperty("skillName", out var s) ? s.GetString() : null;
       
       // Remove from global config
       var nswagClient = Provider.GetNswagClient();
       if (nswagClient == null) return;
       
       // Update config to remove skill
   }
   ```

2. **`removeAgent` Handler**
3. **`requestAgentRequirements` Handler**

**Validation**:
- [ ] Can remove skills and agents
- [ ] Agent requirements fetch correctly

---

### Phase 4: Provider Management Fix (Week 4)

**Goal**: Fix incomplete `saveCustomProvider` implementation.

1. **Full `saveCustomProvider` Implementation**
   - Match VS Code `provider-actions.ts:425-491`
   - Implement config sanitization
   - Implement config update
   - Implement auth handling
   - Send `configLoaded` and `configUpdated` messages

2. **`fetchCustomProviderModels` Handler**

**Validation**:
- [ ] Custom providers save correctly with full config
- [ ] Models fetch from custom providers

---

### Phase 5: Settings Handlers (Week 5)

**Goal**: Implement all settings-related handlers.

1. **Settings Read Handlers**
   - `requestBrowserSettings`
   - `requestClaudeCompatSetting`
   - `requestNotificationSettings`
   - `requestTimelineSetting`

2. **Settings Write Handlers**
   - `openSettingsTab`
   - `setLanguage`

3. **Reset Handlers**
   - `resetAllSettings`
   - `resetReadNotifications`

**Validation**:
- [ ] All settings read/write correctly
- [ ] Reset functions work

---

### Phase 6: Remote & Cloud (Week 6)

**Goal**: Implement remote and cloud session handlers.

1. **Remote Handlers**
   - `toggleRemote`
   - `setRemoteEnabled`
   - `requestRemoteStatus`

2. **Cloud Session Handlers**
   - `requestCloudSessions`
   - `requestCloudSessionData`

**Validation**:
- [ ] Remote toggle works
- [ ] Cloud sessions fetch correctly

---

### Phase 7: File & Context (Week 7)

**Goal**: Implement file search and context handlers.

1. **Search Handlers**
   - `requestFileSearch`
   - `requestSessionSearch`
   - `requestFilePicker`

2. **Context Handler**
   - `requestTerminalContext`

**Validation**:
- [ ] File search returns results
- [ ] Terminal context returns correctly

---

### Phase 8: Other Handlers (Week 8)

**Goal**: Implement remaining handlers.

1. **UI Handlers**
   - `openKiloClaw`
   - `openMarketplacePanel`
   - `saveImage`
   - `reload`

2. **Session Handlers**
   - `forkSession`
   - `openSubAgentViewer`
   - `sessionCostAlertResponse`

3. **Sandbox Handlers** (if applicable)
   - `requestSandboxStatus`
   - `requestSandboxDefault`
   - `setSandboxDefault`
   - `toggleSandbox`

4. **Other**
   - `chatCompletionAccepted`
   - `telemetry`
   - `persistVariant`
   - `persistRecents`
   - `toggleFavorite`
   - `enhancePrompt`

**Validation**:
- [ ] All handlers respond correctly
- [ ] UI opens where expected

---

### Phase 9: Migration Handlers (Week 9)

**Goal**: Implement legacy migration handlers.

1. **Migration Flow**
   - `requestMigrationData`
   - `startMigration`
   - `skipLegacyMigration`
   - `clearLegacyData`
   - `finalizeLegacyMigration`

**Validation**:
- [ ] Migration data fetches
- [ ] Migration flow completes

---

## Dependencies

### Required Before Starting
- [x] `PORT-CORE-002` - Auth service parity complete
- [x] NSwag client has all required endpoints
- [ ] Backend endpoints for new handlers (verify existence)

### Blocks
- None - this task enables full VS extension functionality

---

## Risks and Mitigations

### Risk 1: Missing Backend Endpoints

**Impact**: High - handlers cannot work without backend support

**Mitigation**:
1. Verify all endpoints exist in OpenAPI spec
2. If missing, file backend issues
3. Implement stub handlers that return appropriate errors

### Risk 2: Visual Studio API Limitations

**Impact**: Medium - some VS Code features may not have VS equivalents

**Mitigation**:
1. Identify VS-specific constraints early
2. Implement closest equivalent functionality
3. Document limitations

### Risk 3: Config Schema Changes

**Impact**: Medium - config handling may differ from VS Code

**Mitigation**:
1. Use existing config handlers as reference
2. Test config read/write thoroughly
3. Handle migration from legacy config

---

## Validation Plan

### Unit Tests
- All new handlers have unit tests
- Test message parsing
- Test error handling
- Test success paths

### Integration Tests
- Test full workflows (e.g., connect MCP, send message)
- Test config persistence
- Test error recovery

### Manual Testing
- Test each handler via webview messages
- Verify UI updates correctly
- Verify backend calls succeed

---

## Success Metrics

1. **Handler Coverage**: 90%+ of VS Code handlers implemented
2. **Build**: 0 errors, 0 warnings
3. **Tests**: All new tests pass
4. **Functionality**: Core features work end-to-end

---

## Notes

- This plan focuses on **message handler implementation** - the behavioral logic
- Some handlers may be **platform-specific** and can be deferred
- The `saveCustomProvider` fix is **critical** as it's partially implemented but broken
- MCP support is **high priority** as it's a core feature
- Migration handlers can be **deferred** if legacy migration is not needed

---

## References

- VS Code KiloProvider: `packages/kilo-vscode/src/KiloProvider.ts`
- VS Code provider-actions: `packages/kilo-vscode/src/provider-actions.ts`
- VS Code auth handlers: `packages/kilo-vscode/src/kilo-provider/handlers/auth.ts`
- VS Provider: `packages/kilo-visualstudio/KiloVisualStudioExtension/VSProvider.cs`
- VS Auth Handler: `packages/kilo-visualstudio/KiloVisualStudioExtension/Services/Handlers/Auth/AuthHandlerService.cs`
- VS Provider Action: `packages/kilo-visualstudio/KiloVisualStudioExtension/Services/Handlers/Provider/ProviderActionService.cs`
