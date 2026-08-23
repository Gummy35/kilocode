# PORT-CORE-002: Authentication and Core Service Parity

**Status:** Phase 4 Complete - Phase 5 (Provider Management) remaining  
**Created:** 2026-08-21  
**Depends On:** `PORT-CLI-001` (CLI/HTTP client), `PORT-WEBVIEW-002` (WebView integration)  
**Blocks:** `PORT-CORE-001` (remaining extension-host functionality)

---

## Objective

Achieve functional parity between the Visual Studio extension and VS Code extension for authentication flows and core handler services identified as incomplete or missing in the audit.

This task addresses **critical gaps** that prevent the Visual Studio extension from functioning correctly:
1. Authentication (login/logout/organization switching)
2. Permission/question recovery mechanisms
3. Session directory tracking for worktree support
4. Message loading and transformation
5. Provider management

---

## Background

The audit identified the following critical inconsistencies between VS Code and Visual Studio implementations:

### Authentication (P0 - Critical)
- **VS Code**: Full OAuth device flow with QR code, verification URL, polling callback (`auth.ts:28-74`)
- **VS**: Only fetches profile without initiating device auth flow (`AuthHandlerService.cs:49-70`)
- **Impact**: Users cannot authenticate to Kilo Gateway

### Permission System (P1 - High)
- **VS Code**: Full permission response posting + recovery via `fetchAndSendPendingPermissions()` (`permission-handler.ts:58-164`)
- **VS**: Explicitly not implemented - NSwag client missing endpoint (`InteractionHandlerService.cs:285`)
- **Impact**: Permission system broken; SSE reconnection leaves requests hanging

### Session Management (P1 - High)
- **VS Code**: Tracks `sessionDirectories` map for worktree support; proper abort controllers
- **VS**: Uses `System.Environment.CurrentDirectory` everywhere; no worktree support
- **Impact**: Agent Manager worktrees cannot function

### Message Loading (P2 - Medium)
- **VS Code**: Proper message transformation via `sessionToWebview()`
- **VS**: Creates empty message objects with hardcoded fields (`SessionHandlerService.cs:360-372`)
- **Impact**: Session messages not displayed correctly

---

## Scope

### In Scope

1. **Authentication Handler** (`AuthHandlerService`)
   - Implement full OAuth device flow in `HandleLoginAsync()`
   - Add `HandleLogoutAsync()` with credential removal
   - Add `HandleSetOrganizationAsync()` for org switching
   - Add `HandleRefreshProfileAsync()` (already exists, verify completeness)

2. **Permission/Question Recovery** (`InteractionHandlerService`)
   - Add missing NSwag endpoint for permission response posting
   - Implement `fetchAndSendPendingPermissions()` equivalent
   - Implement `fetchAndSendPendingQuestions()` equivalent
   - Add suggestion recovery if backend supports it

3. **Session Directory Tracking** (`SessionHandlerService` + `SSEHelper`)
   - Add `sessionDirectories` dictionary to track per-session directories
   - Update all session operations to use directory parameter
   - Add worktree directory override support
   - Integrate with Agent Manager worktree flow

4. **Message Loading** (`SessionHandlerService`)
   - Fix message deserialization to properly transform backend messages
   - Remove hardcoded empty message objects
   - Use generated DTOs from `PORT-WEBVIEW-001` where applicable

5. **Provider Management** (`ProviderRequestService`)
   - Implement provider key storage for authenticated fetches
   - Add provider connection/disconnection actions
   - Add OAuth authorization flow for providers
   - Match VS Code's agent filtering logic

6. **NSwag Client Updates**
   - Regenerate from latest OpenAPI spec
   - Add missing endpoints: permission response, MCP removal
   - Verify all endpoints used in VS Code exist in NSwag

### Out of Scope

1. Migration handler (`kilo-provider/handlers/migration.ts`) - defer to separate task
2. Cloud session timeout handling improvements - existing implementation adequate
3. Config caching optimization - can be incremental
4. SSE integration improvements - covered by `PORT-SSE-001`
5. Memory handler improvements - already well-implemented

---

## Acceptance Criteria

### NSwag Client
- [x] Regenerated from `packages/opencode` OpenAPI spec
- [x] `Auth_removeAsync()` exists
- [x] `Provider_oauth_authorizeAsync()` exists
- [x] `Provider_oauth_callbackAsync()` exists
- [x] `Kilo_organization_setAsync()` exists
- [x] `Permission_listAsync()` exists
- [x] `Permission_replyAsync()` exists
- [x] `Permission_saveAlwaysRulesAsync()` exists
- [x] `Question_listAsync()` exists
- [x] `Question_replyAsync()` exists
- [x] `Question_rejectAsync()` exists
- [x] Build succeeds with 0 errors

### Authentication
- [x] `HandleLoginAsync()` initiates OAuth device flow and returns verification URL + code
- [x] `HandleLogoutAsync()` removes credentials and clears profile
- [x] `HandleSetOrganizationAsync()` switches org and refreshes profile + providers + agents
- [x] All three methods match VS Code error handling patterns
- [x] Helper methods added to `VSProvider`: `GetLoginAttempt()`, `DisposeGlobal()`, `FetchAndSendProviders()`, `FetchAndSendAgents()`
- [x] Message handlers registered for `logout` and `setOrganization`
- [ ] Unit tests for each authentication flow

### Permission/Question Recovery
- [x] `InteractionHandlerService` has `_permissionDirectories` and `_questionDirectories` tracking
- [x] `FetchAndSendPendingPermissionsAsync()` implemented
- [x] `FetchAndSendPendingQuestionsAsync()` implemented
- [x] `HandlePermissionResponseAsync()` fully implemented (uses directory tracking)
- [x] `HandlePermissionReplyAsync()` uses directory tracking
- [x] `HandleQuestionReplyAsync()` uses directory tracking
- [x] `HandleQuestionRejectAsync()` uses directory tracking
- [x] `VSProvider.GetSessionDirectories()` helper added
- [x] `VSProvider.IsTrackedSession()` helper added
- [x] `SSEHelper.GetSessionDirectories()` helper added
- [x] `SSEHelper.IsTrackedSession()` helper added
- [x] Recovery integrated into SSE reconnection flow (via `HandleStateChange`)
- [ ] Unit tests for recovery scenarios

### Session Directory Tracking
- [x] `SSEHelper` has `sessionDirectories` dictionary (pre-existing)
- [x] `SetSessionDirectory()` method added
- [x] `GetSessionDirectory()` method added  
- [x] `ResolveDirectory()` method added
- [x] `Session_messagesAsync` uses `ResolveDirectory()`
- [x] `Session_deleteAsync` uses `ResolveDirectory()`
- [x] `Session_updateAsync` uses `ResolveDirectory()`
- [x] `Session_deleteMessageAsync` uses `ResolveDirectory()`
- [x] `Session_getAsync` uses `ResolveDirectory()`
- [x] `Session_revertAsync` uses `ResolveDirectory()`
- [x] `Session_unrevertAsync` uses `ResolveDirectory()`
- [ ] Worktree directory overrides work correctly (Agent Manager integration)
- [ ] Unit tests for directory resolution

### Message Loading
- [ ] `HandleLoadMessagesAsync()` properly deserializes messages from backend
- [ ] No hardcoded empty message objects
- [ ] Messages include all required fields (id, role, parts, createdAt, etc.)
- [ ] Integration test validates message round-trip

### Provider Management
- [ ] Provider keys stored extension-side in `storedProviderKeys` dictionary
- [ ] `HandleConnectProviderAsync()` connects provider
- [ ] `HandleDisconnectProviderAsync()` disconnects provider
- [ ] `HandleAuthorizeProviderOAuthAsync()` initiates OAuth flow
- [ ] Agent filtering matches VS Code (exclude subagent mode, hidden agents)

---

## Implementation Plan

### Phase 1: NSwag Client Regeneration

**Goal**: Ensure all required endpoints exist in the generated client.

**Pre-requisite Check**:
1. Verify backend server is running on port 56631 OR use existing OpenAPI spec at `packages/kilo-visualstudio/porting/docs/openapi-spec.json`
2. Check current NSwag client for missing endpoints:
   ```powershell
   Select-String -Pattern "Permission_|Mcp_|Provider_" -Path "packages\kilo-visualstudio\KiloVisualStudioExtension\ApiClient\KiloApiClient.cs"
   ```

**Regeneration Steps**:
1. Run NSwag regeneration script:
   ```powershell
   cd packages/kilo-visualstudio
   .\scripts\regenerate-nswag-client.ps1
   ```

2. If server not running, script will use existing OpenAPI spec from `porting/docs/openapi-spec.json`

3. Verify missing endpoints in generated client:
   - `Permission_replyAsync()` - exists (used in InteractionHandlerService)
   - `Permission_postAsync()` - **MUST EXIST** for permission response posting
   - `Mcp_removeAsync()` - **MUST EXIST** for MCP server removal
   - `Provider_oauth_authorizeAsync()` - exists (used for provider OAuth)
   - `Auth_removeAsync()` - **MUST EXIST** for logout

4. If endpoints missing:
   - Check `packages/opencode/src/server/routes/` for endpoint implementation
   - Check `packages/opencode/src/server/openapi.ts` for OpenAPI definition
   - If implemented but not in spec, add OpenAPI definition
   - If not implemented, mark as deferred and document in plan

5. Create/update type aliases in `ApiClientAliases.cs`:
   - Map new BodyNN types generated by NSwag
   - Follow existing pattern: `using PermissionResponseRequest = KiloVisualStudioExtension.ApiClient.BodyXX;`

**Validation**:
- [ ] Build succeeds with 0 errors
- [ ] All required methods exist in `KiloApiClient`
- [ ] `ApiClientAliases.cs` updated with new type mappings
- [ ] No Kiota references remain in production code

**Deliverables**:
- Updated `KiloApiClient.cs` with all required endpoints
- Updated `ApiClientInheritance.cs` with polymorphic type declarations
- Updated `ApiClientAliases.cs` with type mappings
- Verification report listing all required endpoints and their status

---

### Phase 2: Authentication Handler

**Goal**: Full OAuth device flow parity with VS Code.

**Implementation Details**:

1. **`HandleLoginAsync()`** - Replace current implementation (lines 49-70):
   ```csharp
   public async Task HandleLoginAsync(JsonElement? payload)
   {
       var nswagClient = Provider.GetNswagClient();
       if (nswagClient == null)
       {
           await Provider.SendErrorAsync("Not connected", "Not connected to backend");
           return;
       }
       
       var directory = Environment.CurrentDirectory;
       var attempt = Provider.GetLoginAttempt(); // Track login attempt for cancellation check
       
       try
       {
           // 1. Initiate OAuth authorization
           var auth = await nswagClient.Provider_oauth_authorizeAsync(directory, "", new BodyXX 
           { 
               ProviderID = "kilo", 
               Method = 0 // 0 = device auth
           });
           
           // 2. Extract code from instructions (format: "code: ABCD-1234")
           var code = ExtractCodeFromInstructions(auth.Instructions);
           
           // 3. Send deviceAuthStarted to webview
           Provider.PostMessage(new DeviceAuthStartedMessage 
           { 
               Code = code,
               VerificationUrl = auth.Url,
               ExpiresIn = 900 // 15 minutes
           });
           
           // 4. Poll for OAuth callback (block until complete or cancelled)
           await WaitForOAuthCallback(directory, attempt);
           
           // Check if login was cancelled during polling
           if (attempt != Provider.GetLoginAttempt()) return;
           
           // 5. Fetch profile and send to webview
           var profile = await nswagClient.Kilo_profileAsync(directory, "");
           await Provider.SendProfileDataAsync(JsonSerializer.SerializeToElement(profile));
           Provider.PostMessage(new DeviceAuthCompleteMessage());
           
           // 6. Refresh providers and agents after successful login
           await Provider.RefreshProvidersAndAgents();
       }
       catch (OperationCanceledException)
       {
           if (attempt != Provider.GetLoginAttempt()) return;
           Provider.PostMessage(new DeviceAuthFailedMessage 
           { 
               Error = "Login cancelled by user" 
           });
       }
       catch (Exception ex)
       {
           System.Diagnostics.Debug.WriteLine($"[Kilo] AuthHandler: login error: {ex.Message}");
           await Provider.SendErrorAsync("Login error", ex.Message);
       }
   }
   
   private string ExtractCodeFromInstructions(string? instructions)
   {
       if (string.IsNullOrEmpty(instructions)) return "";
       var match = System.Text.RegularExpressions.Regex.Match(instructions, @"code:\s*(\S+)", 
           System.Text.RegularExpressions.RegexOptions.IgnoreCase);
       return match.Success ? match.Groups[1].Value.ToUpperInvariant() : "";
   }
   
   private async Task WaitForOAuthCallback(string directory, int expectedAttempt)
   {
       // Poll every 2 seconds until OAuth complete or attempt changes
       var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
       while (!cts.Token.IsCancellationRequested && expectedAttempt == Provider.GetLoginAttempt())
       {
           try
           {
               // This will throw if not yet authorized, succeed when authorized
               await Provider.GetNswagClient()!.Provider_oauth_callbackAsync(directory, "", new BodyYY());
               return; // Success
           }
           catch (ApiException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
           {
               // Still waiting for authorization
               await Task.Delay(2000, cts.Token);
           }
       }
   }
   ```

2. **`HandleLogoutAsync()`** - New method:
   ```csharp
   public async Task HandleLogoutAsync(JsonElement? payload)
   {
       var nswagClient = Provider.GetNswagClient();
       if (nswagClient == null) return;
       
       try
       {
           // 1. Remove credentials
           await nswagClient.Auth_removeAsync(new BodyZZ { ProviderID = "kilo" });
           
           // 2. Dispose global state (clears cached data)
           await Provider.DisposeGlobal();
           
           // 3. Clear profile in webview
           Provider.PostMessage(new ProfileDataMessage { Data = null });
           
           // 4. Refresh providers to show unauthenticated state
           await Provider.RefreshProvidersAndAgents();
       }
       catch (Exception ex)
       {
           System.Diagnostics.Debug.WriteLine($"[Kilo] AuthHandler: logout error: {ex.Message}");
       }
   }
   ```

3. **`HandleSetOrganizationAsync()`** - New method:
   ```csharp
   public async Task HandleSetOrganizationAsync(JsonElement? payload)
   {
       if (!payload.HasValue) return;
       
       var orgId = payload.Value.TryGetProperty("organizationId", out var org) 
           ? (org.ValueKind == JsonValueKind.Null ? null : org.GetString()) 
           : null;
       
       var nswagClient = Provider.GetNswagClient();
       if (nswagClient == null) return;
       
       try
       {
           // 1. Set organization
           await nswagClient.Kilo_organization_setAsync(new BodyAA { OrganizationId = orgId });
           
           // 2. Dispose global state
           await Provider.DisposeGlobal();
           
           // 3. Refresh profile and providers independently (best-effort)
           await Provider.RefreshProfileAndProviders();
       }
       catch (Exception ex)
       {
           System.Diagnostics.Debug.WriteLine($"[Kilo] AuthHandler: org switch error: {ex.Message}");
           // On error, still try to refresh profile to reset webview state
           await Provider.RefreshProfileOnly();
       }
   }
   ```

4. **Helper methods to add to `VSProvider`**:
   - `GetLoginAttempt()` - returns current login attempt counter
   - `DisposeGlobal()` - clears global cached state
   - `RefreshProvidersAndAgents()` - fetches and sends providers + agents
   - `RefreshProfileAndProviders()` - refreshes profile, then providers, then agents
   - `RefreshProfileOnly()` - refreshes profile only (used on error)

**DTOs to Generate** (via WebView contract extractor):
- `DeviceAuthStartedMessage` - `{ type: "deviceAuthStarted", code, verificationUrl, expiresIn }`
- `DeviceAuthCompleteMessage` - `{ type: "deviceAuthComplete" }`
- `DeviceAuthFailedMessage` - `{ type: "deviceAuthFailed", error }`

**Validation**:
- [ ] Login flow completes successfully with real Kilo Gateway
- [ ] Login cancellation is handled correctly
- [ ] Logout clears profile and refreshes providers
- [ ] Organization switch refreshes all dependent data
- [ ] Error handling matches VS Code patterns

**Deliverables**:
- Updated `AuthHandlerService.cs` with full implementation
- Updated `VSProvider.cs` with helper methods
- Generated DTOs for device auth messages
- Unit tests: `AuthHandlerServiceTests.cs`
- Integration test: complete login flow with mock OAuth server

---

### Phase 3: Permission/Question Recovery (Week 2-3)

**Goal**: Implement recovery mechanisms matching VS Code.

1. **Permission Recovery**:
   ```csharp
   public async Task FetchAndSendPendingPermissionsAsync()
   {
       if (!IsConnected()) return;
       
       var dirs = GetRecoveryDirectories(); // workspace + sessionDirectories + extra
       var seen = new HashSet<string>();
       
       foreach (var dir in dirs)
       {
           var perms = await nswagClient.Permission_listAsync(dir, "");
           foreach (var perm in RecoverablePermissions(perms, seen))
           {
               _permissionDirectories[perm.Id] = dir;
               PostMessage(new PermissionRequestMessage 
               {
                   Permission = new PermissionData 
                   {
                       Id = perm.Id,
                       SessionID = perm.SessionID,
                       ToolName = perm.Permission,
                       Patterns = perm.Patterns,
                       Always = perm.Always,
                       Args = perm.Metadata,
                       Message = $"Permission required: {perm.Permission}",
                       Tool = perm.Tool
                   }
               });
           }
       }
       
       PrunePermissionDirectories(seen);
   }
   ```

2. **Question Recovery**: Similar pattern with `Question_listAsync()`

3. **HandlePermissionResponseAsync()** - Complete implementation:
   ```csharp
   public async Task HandlePermissionResponseAsync(JsonElement? payload)
   {
       var requestId = payload?.Value.TryGetProperty("requestId", out var rid) ? rid.GetString() : null;
       var response = payload?.Value.TryGetProperty("response", out var resp) ? resp.GetString() : null;
       var sessionID = payload?.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : null;
       
       var dir = _permissionDirectories.GetValueOrDefault(requestId) 
                 ?? GetWorkspaceDirectory(sessionID);
       
       // Save always rules first if any
       if (approvedAlways.Any() || deniedAlways.Any())
       {
           await nswagClient.Permission_saveAlwaysRulesAsync(new PermissionSaveAlwaysRulesRequest 
           {
               RequestID = requestId,
               Directory = dir,
               ApprovedAlways = approvedAlways,
               DeniedAlways = deniedAlways
           });
       }
       
       // Then reply
       await nswagClient.Permission_replyAsync(requestId, dir, "", new PermissionReplyRequest 
       {
           Reply = response.ToLower() switch 
           {
               "approve" or "allow" => Body13Reply.Once,
               "always" => Body13Reply.Always,
               "reject" or "deny" => Body13Reply.Reject,
               _ => Body13Reply.Once
           }
       });
   }
   ```

4. Integrate with SSE reconnection:
   - Call `FetchAndSendPendingPermissionsAsync()` in `HandleSseEvent()` when SSE reconnects
   - Call `FetchAndSendPendingQuestionsAsync()` similarly

**Deliverables**:
- Updated `InteractionHandlerService.cs` with recovery methods
- New methods: `FetchAndSendPendingPermissionsAsync()`, `FetchAndSendPendingQuestionsAsync()`
- `_permissionDirectories` and `_questionDirectories` dictionaries
- Unit tests for recovery scenarios
- Integration test for SSE reconnection recovery

---

### Phase 4: Session Directory Tracking (Week 3-4)

**Goal**: Enable worktree support via per-session directory tracking.

1. **Add to `SSEHelper`**:
   ```csharp
   public class SSEHelper
   {
       // Existing
       private string? _currentSessionID;
       
       // New
       private readonly Dictionary<string, string> _sessionDirectories = new();
       
       public void SetSessionDirectory(string sessionID, string directory)
       {
           _sessionDirectories[sessionID] = directory;
       }
       
       public string? GetSessionDirectory(string sessionID)
       {
           return _sessionDirectories.GetValueOrDefault(sessionID);
       }
       
       public string ResolveDirectory(string? sessionID = null)
       {
           if (sessionID != null && _sessionDirectories.TryGetValue(sessionID, out var dir))
               return dir;
           return Environment.CurrentDirectory;
       }
   }
   ```

2. **Update all session operations** to use directory parameter:
   ```csharp
   // Before
   await nswagClient.Session_messagesAsync(sessionID, "", limit, before);
   
   // After
   var directory = _sseHelper.ResolveDirectory(sessionID);
   await nswagClient.Session_messagesAsync(sessionID, directory, "", limit, before);
   ```

3. **Agent Manager integration**:
   ```csharp
   // When worktree is created
   _sseHelper.SetSessionDirectory(sessionId, worktreePath);
   
   // When worktree is deleted
   _sseHelper.SetSessionDirectory(sessionId, null); // or remove from dictionary
   ```

4. **Update message loading** to properly deserialize:
   ```csharp
   public async Task HandleLoadMessagesAsync(JsonElement? payload)
   {
       // ... existing validation ...
       
       var directory = _sseHelper.ResolveDirectory(sessionID);
       var messages = await nswagClient.Session_messagesAsync(sessionID, directory, "", limit, before);
       
       // Properly transform messages (not hardcoded empty objects)
       var items = messages.Select(msg => new 
       {
           id = msg.Id,
           sessionID = msg.SessionID,
           role = msg.Role,
           parts = TransformParts(msg.Parts),
           createdAt = msg.CreatedAt,
           // ... other fields ...
       }).ToArray();
       
       PostMessage(new MessagesLoadedMessage 
       {
           SessionID = sessionID,
           Messages = items,
           Mode = mode,
           HasMore = hasMore
       });
   }
   ```

**Deliverables**:
- Updated `SSEHelper.cs` with directory tracking
- All session operations use `ResolveDirectory()`
- `HandleLoadMessagesAsync()` properly transforms messages
- Unit tests for directory resolution
- Integration test for worktree directory override

---

### Phase 5: Provider Management (Week 4)

**Goal**: Provider connection/disconnection and authenticated model fetches.

1. **Add provider key storage** to `VSProvider`:
   ```csharp
   private readonly Dictionary<string, StoredProviderKey> _storedProviderKeys = new();
   
   public class StoredProviderKey
   {
       public string ProviderID { get; set; } = "";
       public string APIKey { get; set; } = "";
   }
   ```

2. **Implement provider actions**:
   ```csharp
   public async Task HandleConnectProviderAsync(JsonElement? payload)
   {
       var providerID = payload?.Value.TryGetProperty("providerID", out var pid) ? pid.GetString() : null;
       await nswagClient.Provider_connectAsync(providerID, Environment.CurrentDirectory, "");
   }
   
   public async Task HandleDisconnectProviderAsync(JsonElement? payload)
   {
       var providerID = payload?.Value.TryGetProperty("providerID", out var pid) ? pid.GetString() : null;
       await nswagClient.Provider_disconnectAsync(providerID, Environment.CurrentDirectory, "");
   }
   
   public async Task HandleAuthorizeProviderOAuthAsync(JsonElement? payload)
   {
       var providerID = payload?.Value.TryGetProperty("providerID", out var pid) ? pid.GetString() : null;
       await nswagClient.Provider_oauth_authorizeAsync(providerID, Environment.CurrentDirectory, "");
   }
   ```

3. **Update provider fetch** to use stored keys:
   ```csharp
   public async Task HandleRequestProvidersAsync()
   {
       var response = await nswagClient.Provider_listAsync(Environment.CurrentDirectory, "");
       
       foreach (var provider in response.All)
       {
           // Strip keys before sending to webview
           if (_storedProviderKeys.TryGetValue(provider.Id, out var key))
           {
               // Use key for authenticated fetches but don't send to webview
           }
       }
   }
   ```

**Deliverables**:
- `_storedProviderKeys` dictionary in `VSProvider`
- `HandleConnectProviderAsync()`, `HandleDisconnectProviderAsync()`, `HandleAuthorizeProviderOAuthAsync()`
- Updated `ProviderRequestService` to use stored keys
- Unit tests for provider actions

---

## Validation Plan

### Unit Tests

1. **Authentication Tests**:
   - `AuthHandlerServiceTests.HandleLoginAsync_InitiatesDeviceAuthFlow`
   - `AuthHandlerServiceTests.HandleLogoutAsync_RemovesCredentials`
   - `AuthHandlerServiceTests.HandleSetOrganizationAsync_SwitchesOrg`
   - `AuthHandlerServiceTests.ExtractCodeFromInstructions_ParsesCorrectly`

2. **Permission Recovery Tests**:
   - `InteractionHandlerServiceTests.FetchAndSendPendingPermissionsAsync_RecoveresPending`
   - `InteractionHandlerServiceTests.HandlePermissionResponseAsync_SavesAndReplies`
   - `InteractionHandlerServiceTests.PermissionDirectoryTracking_WorksCorrectly`

3. **Session Directory Tests**:
   - `SSEHelperTests.ResolveDirectory_UsesSessionDirectoryWhenSet`
   - `SSEHelperTests.ResolveDirectory_FallsBackToCurrentDirectory`
   - `SSEHelperTests.SetSessionDirectory_UpdatesDictionary`

4. **Message Loading Tests**:
   - `SessionHandlerServiceTests.HandleLoadMessagesAsync_DeserializesCorrectly`
   - `SessionHandlerServiceTests.HandleLoadMessagesAsync_TransformsParts`

5. **Provider Tests**:
   - `ProviderRequestServiceTests.HandleConnectProviderAsync_Connects`
   - `ProviderRequestServiceTests.HandleDisconnectProviderAsync_Disconnects`
   - `ProviderRequestServiceTests.StoredProviderKeys_Persisted`

### Integration Tests

1. **Complete Login Flow**:
   - Initiate login
   - Simulate OAuth callback
   - Verify profile sent to webview
   - Verify providers refreshed

2. **SSE Reconnection Recovery**:
   - Create pending permission
   - Simulate SSE disconnect
   - Simulate SSE reconnect
   - Verify permission recovered and sent to webview

3. **Worktree Directory Override**:
   - Create session in worktree
   - Set session directory to worktree path
   - Load messages
   - Verify messages loaded from worktree directory

### Build Validation

- [ ] Build succeeds with 0 errors, 0 warnings
- [ ] All unit tests pass
- [ ] All integration tests pass
- [ ] No Kiota references remain
- [ ] NSwag client has all required endpoints

---

## Risks and Mitigations

### Risk 1: NSwag Endpoint Missing

**Impact**: High - feature cannot be implemented without backend endpoint

**Mitigation**:
1. Check `packages/opencode/src/server/` for endpoint implementation
2. If implemented but not in OpenAPI spec, add spec definition
3. If not implemented, file backend issue and defer feature

### Risk 2: OAuth Flow Complexity

**Impact**: Medium - device auth polling may be tricky in C#

**Mitigation**:
1. Study VS Code implementation carefully
2. Use `HttpClient` with polling loop
3. Add cancellation token support
4. Test with real Kilo Gateway

### Risk 3: Directory Tracking Breaking Changes

**Impact**: Medium - may affect existing session operations

**Mitigation**:
1. Make directory parameter optional with fallback
2. Test all session operations thoroughly
3. Gradual rollout - start with new sessions only

### Risk 4: Permission Recovery Race Conditions

**Impact**: Medium - concurrent recovery may cause duplicates

**Mitigation**:
1. Use `seen` HashSet to track processed permissions
2. Serialize recovery operations
3. Test with multiple concurrent sessions

---

## Dependencies

### Required Before Starting

- [x] `PORT-CLI-001` - CLI/HTTP client port complete
- [x] `PORT-WEBVIEW-001` - DTO generation complete
- [ ] `PORT-WEBVIEW-002` - WebView integration (should be complete before this task)
- [x] `PORT-SSE-001` - SSE event processing (provides foundation)

### Blocks

- [ ] `PORT-CORE-001` - Remaining extension-host functionality
- [ ] `PORT-TEST-001` - Unit test port (can run in parallel for other areas)

---

## Success Metrics

1. **Authentication**: Users can login, logout, and switch organizations
2. **Permissions**: Permission requests recover after SSE reconnection
3. **Questions**: Question requests recover after SSE reconnection
4. **Worktrees**: Agent Manager worktrees function correctly with directory overrides
5. **Messages**: Session messages load and display correctly
6. **Providers**: Providers can be connected/disconnected with OAuth support
7. **Tests**: All unit and integration tests pass
8. **Build**: Zero errors, zero warnings

---

## Notes

- This task focuses on **functional parity** - matching VS Code behavior
- Platform-specific UI differences are acceptable (Visual Studio dialogs vs VS Code webviews)
- NSwag client regeneration should be done first to unblock other work
- Permission/question recovery is critical for SSE reliability
- Directory tracking is prerequisite for Agent Manager worktree support

---

## References

- VS Code auth handler: `packages/kilo-vscode/src/kilo-provider/handlers/auth.ts`
- VS Code permission handler: `packages/kilo-vscode/src/kilo-provider/handlers/permission-handler.ts`
- VS Code question handler: `packages/kilo-vscode/src/kilo-provider/handlers/question.ts`
- Current VS auth: `packages/kilo-visualstudio/KiloVisualStudioExtension/Services/Handlers/Auth/AuthHandlerService.cs`
- Current VS interaction: `packages/kilo-visualstudio/KiloVisualStudioExtension/Services/Handlers/Interaction/InteractionHandlerService.cs`
- OpenAPI spec: `packages/kilo-visualstudio/porting/docs/openapi-spec.json`
