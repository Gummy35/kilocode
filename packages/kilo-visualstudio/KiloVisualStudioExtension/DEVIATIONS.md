# VS Code vs Visual Studio Extension Handler Deviations

This document compares the message handlers in the VS Code extension (`packages/kilo-vscode/src/KiloProvider.ts`) with the C# handler services in the Visual Studio extension (`packages/kilo-visualstudio/KiloVisualStudioExtension/Services/`).

## Summary

- **Total handlers analyzed**: 14 direct message handlers + SSE event handlers
- **Handlers with deviations**: 14
- **Significant deviations**: 12 (missing functionality, different message structure, incomplete implementations)
- **Direct message handlers in VSProvider**: 43
- **SSE event handlers in SSEHelper**: ~20 fully implemented
- **Missing message handlers**: 58 (down from 88 when including SSE handlers)

---

## 1. ProviderRequestService

### VS Code Workflow (`fetchAndSendProviders`)
1. Check if client is connected
2. Fetch provider data from `/provider` endpoint with coalescing (at most one in-flight + one queued)
3. Parse response containing `all`, `connected`, `default`, `authMethods`, `authStates`
4. Compute default selection based on cached config and VS Code settings
5. Send `providersLoaded` message with:
   - `providers`: Record<string, Provider> indexed by ID
   - `connected`: string[] of connected provider IDs
   - `defaults`: Record<string, string> default selections
   - `defaultSelection`: computed default selection object
   - `authMethods`: Record<string, AuthMethod[]>
   - `authStates`: Record<string, AuthState>
6. Cache message for webview refreshes
7. Handle errors gracefully, send cached data if available

### C# Workflow (`HandleRequestProvidersAsync`)
1. Check if HTTP client is available
2. Fetch from `/provider` endpoint
3. Parse response manually converting arrays to dictionaries
4. Send `providersLoaded` message with:
   - `providers`: Record<string, object> (cloned JSON elements)
   - `connected`: string[]
   - `defaults`: Record<string, string>
   - `defaultSelection`: **empty object** (missing computation logic)
   - `authMethods`: **empty arrays** (not parsed from response)
   - `authStates`: **empty object** (not parsed from response)
5. No caching mechanism
6. Falls back to empty providers on error

### Deviations
| Aspect | VS Code | C# | Severity |
|--------|---------|-----|----------|
| Default selection computation | ✅ Full logic with config/settings | ❌ Empty object | High |
| Auth methods parsing | ✅ Parses from response | ❌ Always empty | High |
| Auth states parsing | ✅ Parses from response | ❌ Always empty | High |
| Caching | ✅ Cached message for refreshes | ❌ No caching | Medium |
| Error handling | ✅ Returns cached data | ❌ Returns empty | Medium |
| Request coalescing | ✅ Prevents duplicate requests | ❌ No coalescing | Low |

---

## 2. AgentRequestService

### VS Code Workflow (`fetchAndSendAgents`)
1. Check if client is connected
2. Fetch from `/app/agents` endpoint
3. Filter visible agents (exclude hidden and subagent mode)
4. Map agents using `mapAgent` function (specific field selection)
5. Determine default agent (first visible agent)
6. Send `agentsLoaded` message with:
   - `agents`: filtered visible agents
   - `allAgents`: all agents including hidden
   - `defaultAgent`: default agent name
7. Cache message for webview refreshes

### C# Workflow (`HandleRequestAgentsAsync`)
1. Check if HTTP client is available
2. Fetch from `/agent` endpoint (correct)
3. Filter out hidden and subagent mode (matches VS Code)
4. Map agents manually with field selection (matches VS Code)
5. Determine default agent (first agent or "code" fallback)
6. Send `agentsLoaded` message with same structure
7. **No caching mechanism**

### Deviations
| Aspect | VS Code | C# | Severity |
|--------|---------|-----|----------|
| Caching | ✅ Cached message | ❌ No caching | Medium |
| Default agent logic | ✅ First visible agent | ✅ Similar logic | Low |
| Field mapping | ✅ Uses mapAgent function | ✅ Manual mapping | Low |

---

## 3. StateManagementService

### VS Code Workflow
- **setState**: Not directly handled in KiloProvider - webview manages its own state
- **getState**: Not directly handled - state persistence is webview-side

### C# Workflow
1. `HandleSetStateAsync`: Stores state via `_provider.StoreStateAsync()`
2. `HandleGetStateAsync`: Retrieves state via `_provider.GetStoredStateAsync()` and sends `setState` message

### Deviations
| Aspect | VS Code | C# | Severity |
|--------|---------|-----|----------|
| State management location | ✅ Webview-side | ❌ Extension-side | Medium |
| Message structure | ✅ Different pattern | ❌ Different implementation | Medium |

**Note**: VS Code extension doesn't implement setState/getState handlers in KiloProvider - this appears to be a C#-specific addition that doesn't match VS Code architecture.

---

## 4. InteractionHandlerService

### VS Code Workflow

#### Prompt (`handleSendMessage` / `handleSendCommand`)
1. Resolve session (create if needed)
2. Check sandbox status
3. Gather editor context
4. Build parts array (files + text)
5. Assert agent requirements
6. Record message session ID
7. Wait for checkpoints
8. Send via `session.promptAsync` or `session.command` with retry logic
9. Handle retryable errors with exponential backoff
10. Show retry status to user

#### Permission Reply (`handlePermissionResponse`)
1. Extract permission ID and response
2. POST to `/permission/{id}/reply`
3. Handle via `handlePermissionResponse` helper function

#### Question Reply (`questionReply`)
1. Extract request ID and answers
2. POST to `/question/{id}/reply`
3. Handle via `handleQuestionReply` helper function

### C# Workflow

#### Prompt (`HandlePromptAsync`)
1. Check connection
2. Resolve/create session
3. Build simple text part
4. POST to `/session/{sessionID}/prompt_async`
5. **No retry logic**
6. **No editor context gathering**
7. **No agent requirements check**
8. **No checkpoint handling**
9. **No sandbox status check**

#### Permission Reply (`HandlePermissionReplyAsync`)
1. Extract request ID and response
2. POST to `/permission/{requestId}/reply`
3. **No directory context handling**

#### Question Reply (`HandleQuestionReplyAsync`)
1. Extract request ID and answers
2. POST to `/question/{requestId}/reply`
3. **No directory context handling**

### Deviations
| Aspect | VS Code | C# | Severity |
|--------|---------|-----|----------|
| Retry logic with backoff | ✅ Full implementation | ❌ Missing | High |
| Editor context gathering | ✅ Yes | ❌ Missing | High |
| Agent requirements check | ✅ Yes | ❌ Missing | High |
| Sandbox status check | ✅ Yes | ❌ Missing | High |
| Checkpoint handling | ✅ Yes | ❌ Missing | Medium |
| Message confirmation | ✅ Yes | ❌ Missing | Medium |
| Draft session handling | ✅ Yes | ❌ Missing | Medium |
| Model/agent/variant params | ✅ Full support | ❌ Limited | Medium |
| Review metadata | ✅ Yes | ❌ Missing | Low |

---

## 5. SessionControlHandlerService

### VS Code Workflow

#### Abort (`handleAbort`)
1. Stop session processes
2. Cancel active retry loop
3. Set session status to idle
4. Flush session stream
5. Send `sessionTurnClosed` and `sessionStatus` messages

#### Fork Session (`handleForkSession`)
1. Use `handleForkSession` helper with context
2. Fork logic in extracted module

#### Compact (`handleCompact`)
1. Validate client connection
2. Validate session ID
3. Validate provider/model selection
4. Call `session.summarize` endpoint
5. Handle errors

#### Enhance Prompt
1. Use SDK `enhancePrompt` endpoint
2. Handle errors with user feedback
3. Send result or error to webview

### C# Workflow

#### Abort (`HandleAbortAsync`)
1. Get session ID
2. Send `sessionStatus` message to idle
3. Send `sessionTurnClosed` message
4. **No process stopping**
5. **No retry cancellation**
6. **No stream flushing**

#### Fork Session (`HandleForkSessionAsync`)
1. POST to `/session/fork`
2. **No context building**
3. **No error handling details**

#### Compact (`HandleCompactAsync`)
1. POST to `/session/compact`
2. **No validation**
3. **No model selection check**

#### Enhance Prompt (`HandleEnhancePromptAsync`)
1. POST to `/prompt/enhance`
2. **No SDK integration**
3. **No user feedback**

### Deviations
| Aspect | VS Code | C# | Severity |
|--------|---------|-----|----------|
| Abort - process stopping | ✅ Yes | ❌ Missing | High |
| Abort - retry cancellation | ✅ Yes | ❌ Missing | High |
| Abort - stream flushing | ✅ Yes | ❌ Missing | Medium |
| Compact - validation | ✅ Full validation | ❌ Minimal | Medium |
| Compact - model check | ✅ Yes | ❌ Missing | Medium |
| Enhance prompt - SDK | ✅ Full SDK integration | ❌ Raw HTTP | Medium |
| Enhance prompt - error handling | ✅ User-friendly | ❌ Basic | Low |

---

## 6. UiHandlerService

### VS Code Workflow

#### Open Settings Panel (`openSettingsPanel`)
1. Execute VS Code command `kilo-code.new.settingsButtonClicked`
2. Pass tab parameter

#### Open Sub-Agent Viewer (`openSubAgentViewer`)
1. Execute VS Code command `kilo-code.new.openSubAgentViewer`
2. Pass session ID and title

#### Reload (`handleReload`)
1. Call `client.instance.reload` endpoint
2. Handle 409 conflict (session running)
3. Clear commands cache
4. Reload config/agents/skills/commands if directory changed

#### Save Image (`saveImage`)
1. Save image to workspace directory
2. Handle file I/O

### C# Workflow

#### Open Settings Panel (`HandleOpenSettingsPanelAsync`)
1. Post `navigate` message to webview
2. **Different approach** - VS Code uses commands, C# uses navigation message

#### Open Sub-Agent Viewer (`HandleOpenSubAgentViewerAsync`)
1. **Placeholder only** - no implementation
2. Just logs session ID

#### Reload (`HandleReloadAsync`)
1. **Placeholder only** - no implementation
2. Just logs

#### Save Image (`HandleSaveImageAsync`)
1. **Placeholder only** - no implementation
2. Just logs

### Deviations
| Aspect | VS Code | C# | Severity |
|--------|---------|-----|----------|
| Settings panel - approach | ✅ VS Code commands | ❌ Navigation message | Medium |
| Sub-agent viewer | ✅ Full command execution | ❌ Placeholder | High |
| Reload - backend call | ✅ Yes | ❌ Missing | High |
| Reload - cache clearing | ✅ Yes | ❌ Missing | Medium |
| Reload - config reload | ✅ Yes | ❌ Missing | Medium |
| Save image | ✅ Full implementation | ❌ Placeholder | High |

---

## 7. ConfigHandlerService

### VS Code Workflow

#### Request Config (`fetchAndSendConfig`)
1. Check client connection and state
2. Fetch in parallel: local config, global config, project overlay
3. Build config message with features
4. Cache message for refreshes
5. Send `configLoaded` message

#### Update Config (`handleUpdateConfig`)
1. Determine if providers/agents need refresh
2. Drain pending prompts
3. Update global and/or project config via `config.overlayUpdate`
4. Fetch merged config after update
5. Send `configUpdated` message
6. Refresh providers/agents if needed
7. Handle failures with optimistic update fallback

#### Open Config File (`openConfigFile`)
1. Call backend endpoint to get file path
2. Open file in VS Code

### C# Workflow

#### Request Config (`HandleRequestConfigAsync`)
1. Fetch from `/config` endpoint
2. Extract config and features
3. **No global config fetch**
4. **No project overlay fetch**
5. **No parallel fetching**
6. **No caching**

#### Update Config (`HandleUpdateConfigAsync`)
1. POST to `/config` endpoint
2. **No prompt draining**
3. **No refresh determination**
4. **No optimistic updates**
5. **No error recovery**

#### Open Config File (`HandleOpenConfigFileAsync`)
1. GET from `/config/file?scope={scope}`
2. Open file in external editor
3. **Similar approach but different endpoint**

### Deviations
| Aspect | VS Code | C# | Severity |
|--------|---------|-----|----------|
| Config fetch - parallel | ✅ Yes | ❌ Sequential | Medium |
| Config fetch - global overlay | ✅ Yes | ❌ Missing | High |
| Config fetch - project overlay | ✅ Yes | ❌ Missing | High |
| Config fetch - caching | ✅ Yes | ❌ No | Medium |
| Update config - prompt draining | ✅ Yes | ❌ Missing | High |
| Update config - refresh logic | ✅ Yes | ❌ Missing | High |
| Update config - optimistic update | ✅ Yes | ❌ Missing | High |
| Update config - error recovery | ✅ Yes | ❌ Missing | High |

---

## 8. AuthHandlerService

### VS Code Workflow

#### Login (`handleLogin`)
1. Use `handleLogin` helper with retry attempt tracking
2. Device authentication flow
3. Handle cancellation
4. Refresh profile after login

#### Logout (`handleLogout`)
1. Use `handleLogout` helper
2. Dispose global state
3. Refresh profile and providers

#### Refresh Profile (`handleRefreshProfile`)
1. Fetch from `/kilo/profile`
2. Send `profileData` message
3. Handle 401 (not logged in) gracefully

### C# Workflow

#### Login (`HandleLoginAsync`)
1. Fetch from `/kilo/profile`
2. **Not actual login** - just checks profile
3. **No device auth flow**
4. **No retry attempt tracking**

#### Logout (`HandleLogoutAsync`)
1. POST to `/auth/logout`
2. **No global state disposal**
3. **No profile/providers refresh**

#### Refresh Profile (`HandleRefreshProfileAsync`)
1. Fetch from `/kilo/profile`
2. Send `profileData` message
3. **Similar to VS Code**

### Deviations
| Aspect | VS Code | C# | Severity |
|--------|---------|-----|----------|
| Login - device auth flow | ✅ Full implementation | ❌ Missing | Critical |
| Login - retry tracking | ✅ Yes | ❌ Missing | Medium |
| Login - cancellation handling | ✅ Yes | ❌ Missing | Medium |
| Logout - global disposal | ✅ Yes | ❌ Missing | High |
| Logout - refresh after | ✅ Yes | ❌ Missing | High |
| Refresh profile - 401 handling | ✅ Graceful | ⚠️ Basic | Low |

---

## 9. MiscRequestHandlerService

### VS Code Workflow

#### Request Recents (`requestRecents`)
1. Read from VS Code globalState `recentModels`
2. Validate recents
3. Send `recentsLoaded` message

#### Request Favorites (`requestFavorites`)
1. Read from VS Code globalState `favoriteModels`
2. Validate favorites
3. Send `favoritesLoaded` message

#### Request Variants (`requestVariants`)
1. Read from VS Code globalState `variantSelections`
2. Send `variantsLoaded` message

#### Request Skills (`fetchAndSendSkills`)
1. Fetch from `/app/skills` endpoint
2. Cache message
3. Send `skillsLoaded` message

#### Request Commands (`fetchAndSendCommands`)
1. Load commands from disk/backend
2. Cache message
3. Send `commandsLoaded` message

### C# Workflow

#### Request Recents (`HandleRequestRecents`)
1. **Returns empty array**
2. **No storage integration**

#### Request Favorites (`HandleRequestFavorites`)
1. **Returns empty array**
2. **No storage integration**

#### Request Variants (`HandleRequestVariants`)
1. **Returns empty object**
2. **No storage integration**

#### Request Skills (`HandleRequestSkillsAsync`)
1. Fetch from `/skill` endpoint
2. Parse and send
3. **No caching**

#### Request Commands (`HandleRequestCommandsAsync`)
1. Fetch from `/command` endpoint
2. Parse and send
3. **No caching**

### Deviations
| Aspect | VS Code | C# | Severity |
|--------|---------|-----|----------|
| Recents - storage | ✅ VS Code globalState | ❌ Empty | High |
| Favorites - storage | ✅ VS Code globalState | ❌ Empty | High |
| Variants - storage | ✅ VS Code globalState | ❌ Empty | High |
| Skills - caching | ✅ Yes | ❌ No | Low |
| Commands - caching | ✅ Yes | ❌ No | Low |

---

## 10. ModelHandlerService

### VS Code Workflow

#### Request Model Selections
1. Read from VS Code settings
2. Compute default selection
3. Send `modelSelectionsLoaded` message

#### Persist Variant (`persistVariant`)
1. Write to VS Code globalState `variantSelections`
2. No backend call (local storage only)

#### Persist Recents (`persistRecents`)
1. Write to VS Code globalState `recentModels`
2. No backend call (local storage only)

### C# Workflow

#### Request Model Selections (`HandleRequestModelSelections`)
1. **Returns null values**
2. **No settings integration**

#### Persist Variant (`HandlePersistVariant`)
1. **Placeholder only** - logs payload
2. **No actual persistence**

#### Persist Recents (`HandlePersistRecents`)
1. **Placeholder only** - logs payload
2. **No actual persistence**

### Deviations
| Aspect | VS Code | C# | Severity |
|--------|---------|-----|----------|
| Model selections - reading | ✅ From settings | ❌ Null values | High |
| Persist variant - storage | ✅ GlobalState write | ❌ Placeholder | Critical |
| Persist recents - storage | ✅ GlobalState write | ❌ Placeholder | Critical |

---

## 11. SettingsHandlerService

### VS Code Workflow

#### Request Indexing Settings (`requestIndexingSettings`)
1. Build message from VS Code config
2. Send `indexingSettingsLoaded` message

#### Request Chat Settings (`requestChatSettings`)
1. Build message from VS Code config
2. Send `chatSettingsLoaded` message

#### Request Throughput Setting (`requestThroughputSetting`)
1. Read from VS Code config
2. Send `throughputSettingLoaded` message

#### Request Autocomplete Settings (`requestAutocompleteSettings`)
1. Read from VS Code config
2. Validate setting values
3. Send `autocompleteSettingsLoaded` message

### C# Workflow

#### Request Indexing Settings (`HandleRequestIndexingSettings`)
1. **Hardcoded values**
2. **No config reading**

#### Request Chat Settings (`HandleRequestChatSettings`)
1. **Hardcoded values**
2. **No config reading**

#### Request Throughput Setting (`HandleRequestThroughputSetting`)
1. **Hardcoded values**
2. **No config reading**

#### Request Autocomplete Settings (`HandleRequestAutocompleteSettings`)
1. **Hardcoded values**
2. **No config reading**
3. **No validation**

### Deviations
| Aspect | VS Code | C# | Severity |
|--------|---------|-----|----------|
| All settings - config reading | ✅ From VS Code config | ❌ Hardcoded | Critical |
| Autocomplete - validation | ✅ Yes | ❌ Missing | Medium |

---

## 12. NotificationHandlerService

### VS Code Workflow

#### Request Notifications (`fetchAndSendNotifications`)
1. Fetch from backend
2. Filter by dismissed IDs
3. Cache message
4. Send `notificationsLoaded` message

#### Dismiss Notification (`handleDismissNotification`)
1. POST to `/notification/{id}/dismiss`
2. Broadcast to other providers
3. Refetch notifications

#### Reset Read Notifications (`resetReadNotifications`)
1. POST to `/notification/reset-read`
2. Clear dismissed IDs
3. Refetch notifications

### C# Workflow

#### Request Notifications (`HandleRequestNotificationsAsync`)
1. Fetch from `/notification` endpoint
2. Parse and send
3. **No dismissed ID filtering**
4. **No caching**

#### Dismiss Notification (`HandleDismissNotificationAsync`)
1. POST to `/notification/{id}/dismiss`
2. **No broadcast**
3. **No refetch**

#### Reset Read Notifications (`HandleResetReadNotificationsAsync`)
1. POST to `/notification/reset-read`
2. **No ID clearing**
3. **No refetch**

### Deviations
| Aspect | VS Code | C# | Severity |
|--------|---------|-----|----------|
| Notifications - filtering | ✅ Dismissed IDs | ❌ No filtering | High |
| Notifications - caching | ✅ Yes | ❌ No | Medium |
| Dismiss - broadcast | ✅ Yes | ❌ Missing | Medium |
| Dismiss - refetch | ✅ Yes | ❌ Missing | Medium |
| Reset - ID clearing | ✅ Yes | ❌ Missing | Medium |
| Reset - refetch | ✅ Yes | ❌ Missing | Medium |

---

## 13. McpHandlerService

### VS Code Workflow

#### Request MCP Status (`fetchAndSendMcpStatus`)
1. Fetch from `/mcp/status` endpoint
2. Cache message
3. Send `mcpStatusLoaded` message

#### Connect MCP (`connectMcp`)
1. Call `McpOAuth.connectMcpServer` helper
2. Handle OAuth flow
3. Refresh status on completion

#### Disconnect MCP (`disconnectMcp`)
1. Call `McpOAuth.disconnectMcpServer` helper
2. Refresh status on completion

#### Authenticate MCP (`authenticateMcp`)
1. Call `McpOAuth.authenticateMcpServer` helper
2. Handle OAuth authentication
3. Refresh status on completion

#### Remove MCP (`removeMcp`)
1. Use `removeMcp` helper with context
2. Update config
3. Refresh status

### C# Workflow

#### Request MCP Status (`HandleRequestMcpStatusAsync`)
1. Fetch from `/mcp` endpoint
2. Parse and send
3. **No caching**

#### Connect MCP (`HandleConnectMcpAsync`)
1. POST to `/mcp/connect`
2. **No OAuth flow handling**
3. **No refresh on completion**

#### Disconnect MCP (`HandleDisconnectMcpAsync`)
1. POST to `/mcp/{serverId}/disconnect`
2. **No refresh on completion**

#### Authenticate MCP (`HandleAuthenticateMcpAsync`)
1. POST to `/mcp/authenticate`
2. **No OAuth handling**

#### Remove MCP (`HandleRemoveMcpAsync`)
1. POST to `/mcp/{serverId}/remove`
2. **No config update**
3. **No refresh**

### Deviations
| Aspect | VS Code | C# | Severity |
|--------|---------|-----|----------|
| MCP status - caching | ✅ Yes | ❌ No | Low |
| Connect - OAuth flow | ✅ Full handling | ❌ Missing | High |
| Connect - refresh | ✅ Yes | ❌ Missing | Medium |
| Disconnect - refresh | ✅ Yes | ❌ Missing | Medium |
| Authenticate - OAuth | ✅ Full handling | ❌ Missing | High |
| Remove - config update | ✅ Yes | ❌ Missing | High |
| Remove - refresh | ✅ Yes | ❌ Missing | Medium |

---

## 14. SessionHandlerService

### VS Code Workflow

#### Create Session (`handleCreateSession`)
1. Get workspace directory
2. Generate sandbox metadata
3. POST to `/session` with directory and metadata
4. Set current session
5. Track session ID
6. Send `sessionCreated` message

#### Delete Session (`handleDeleteSession`)
1. Stop session processes
2. POST to `/session/delete`
3. Prune all session-related data
4. Update current session if needed
5. Send `sessionDeleted` message

#### Rename Session (`handleRenameSession`)
1. Call `renameSession` helper
2. Update current session if needed
3. Send `sessionUpdated` message

#### Load Messages (`handleLoadMessages`)
1. Handle different modes (replace/focus/reconcile/prepend)
2. Stop processes for replace/focus
3. Track session ID
4. Focus session
5. Abort previous loads for replace mode
6. Fetch message page with pagination
7. Handle abort cancellation
8. Check if session still tracked
9. Slim metadata (remove unused fields)
10. Record message session IDs
11. Reset message costs for replace/reconcile
12. Drop session stream for replace/reconcile
13. Send `messagesLoaded` message with cursor and hasMore
14. Flush stream for preserveStream mode
15. Recover pending prompts

#### Delete Message (`handleDeleteMessage`)
1. POST to `/session/deleteMessage`
2. Handle errors

### C# Workflow

#### Create Session (`HandleCreateSessionAsync`)
1. Call `CreateSessionInternalAsync`
2. POST to `/session` with directory
3. **No sandbox metadata**
4. Set current session
5. Track session
6. Send `sessionCreated` message
7. Auto-load messages

#### Delete Session (`HandleDeleteSessionAsync`)
1. POST to `/session/delete`
2. Update current session if needed
3. Send `sessionDeleted` message
4. **No process stopping**
5. **No comprehensive pruning**

#### Rename Session (`HandleRenameSessionAsync`)
1. POST to `/session/rename`
2. Update current session if needed
3. Send `sessionUpdated` message
4. **Similar to VS Code**

#### Load Messages (`HandleLoadMessagesAsync`)
1. Handle modes (replace/focus)
2. Track session
3. Focus session
4. Set current session
5. **No reconcile/prepend modes**
6. **No abort controller for cancellation**
7. Fetch messages from `/session/{id}/message`
8. Parse and build message objects
9. Drop stream for replace mode
10. Send `messagesLoaded` message
11. **No metadata slimming**
12. **No message cost tracking**
13. **No session ID recording**
14. **No prompt recovery**

#### Delete Message (`HandleDeleteMessageAsync`)
1. POST to `/session/message/delete`
2. **Basic implementation**

### Deviations
| Aspect | VS Code | C# | Severity |
|--------|---------|-----|----------|
| Create - sandbox metadata | ✅ Yes | ❌ Missing | High |
| Delete - process stopping | ✅ Yes | ❌ Missing | High |
| Delete - comprehensive pruning | ✅ Yes | ❌ Minimal | High |
| Load messages - modes | ✅ 4 modes | ❌ 2 modes | High |
| Load messages - abort controller | ✅ Yes | ⚠️ Basic | Medium |
| Load messages - metadata slimming | ✅ Yes | ❌ Missing | Medium |
| Load messages - cost tracking | ✅ Yes | ❌ Missing | High |
| Load messages - session recording | ✅ Yes | ❌ Missing | Medium |
| Load messages - prompt recovery | ✅ Yes | ❌ Missing | Medium |
| Load messages - pagination cursor | ✅ Yes | ⚠️ Basic | Medium |

---

## Overall Architecture Deviations

### 1. Message Routing
- **VS Code**: Large switch statement in `setupWebviewMessageHandler` with extracted handler functions
- **C#**: Separate service classes with explicit method calls via `MessageHandlerRegistry`

### 2. Error Handling
- **VS Code**: Comprehensive error handling with user-friendly messages, retry logic, and fallbacks
- **C#**: Basic try/catch with debug logging, minimal user feedback

### 3. Caching Strategy
- **VS Code**: Extensive caching for providers, agents, skills, commands, config, notifications
- **C#**: Minimal to no caching in most handlers

### 4. Session Management
- **VS Code**: Complex session tracking with multiple data structures (trackedSessionIds, sessionDirectories, streams, etc.)
- **C#**: Basic session tracking via provider methods

### 5. Retry Logic
- **VS Code**: Exponential backoff with abort controllers for cancellable retries
- **C#**: No retry logic in most handlers

### 6. Storage Integration
- **VS Code**: VS Code globalState for persistent data (variants, recents, favorites)
- **C#**: No storage integration - placeholders only

### 7. Backend Communication
- **VS Code**: SDK-based with typed clients and error handling
- **C#**: Raw HTTP calls with manual JSON parsing

---

## Missing Message Handlers

VS Code KiloProvider handles **129 message types**, while VSProvider only handles **43**. The following **88 message types** are missing from the C# implementation:

### Session/Message Handlers (Missing from VSProvider - Some in SSEHelper)
- `message.updated.1` - ✅ Handled in SSEHelper.cs (`HandleMessageUpdatedSync`)
- `message.removed.1` - ✅ Handled in SSEHelper.cs (`HandleMessageRemovedSync`)
- `message.part.updated.1` - ✅ Handled in SSEHelper.cs (`HandlePartUpdatedSync`)
- `message.part.removed.1` - ✅ Handled in SSEHelper.cs (`HandlePartRemovedSync`)
- `message.updated` - ⚠️ Commented out in SSEHelper.cs (`HandleMessageUpdatedStream`)
- `message.removed` - ⚠️ Commented out in SSEHelper.cs (`HandleMessageRemovedStream`)
- `message.part.updated` - ⚠️ Commented out in SSEHelper.cs (`HandlePartUpdatedStream`)
- `syncSession` - Missing (no handler)
- `loadSessions` - Missing (no handler)
- `requestSessionModelUsage` - Missing (no handler)
- `revertSession` - Missing (no handler)
- `unrevertSession` - Missing (no handler)

### Authentication Handlers (Missing)
- `cancelLogin` - Cancel device authentication flow
- `permissionResponse` - Handle permission response (different from permission/reply)

### Provider Handlers (Missing)
- `connectProvider` - Connect to AI provider
- `authorizeProviderOAuth` - Start OAuth authorization
- `completeProviderOAuth` - Complete OAuth flow
- `disconnectProvider` - Disconnect from provider
- `saveCustomProvider` - Save custom provider configuration

### MCP Handlers (Missing)
- `connectMcp` - Connect to MCP server (full implementation missing)
- `disconnectMcp` - Disconnect from MCP server
- `authenticateMcp` - Authenticate with MCP server

### Settings Handlers (Missing)
- `openSettingsTab` - Open specific settings tab
- `requestBrowserSettings` - Request browser settings
- `requestClaudeCompatSetting` - Request Claude compatibility setting
- `requestNotificationSettings` - Request notification settings
- `requestTimelineSetting` - Request timeline setting

### Model Handlers (Missing)
- `persistVariant` - Persist model/agent variant (placeholder in C#)
- `persistRecents` - Persist recent models (placeholder in C#)
- `requestModelSelectorExpanded` - Request model selector state
- `persistModelSelectorExpanded` - Persist model selector state

### Context Request Handlers (Missing)
- `requestFileSearch` - File search (returns empty in C#)
- `requestSessionSearch` - Session search (returns empty in C#)
- `requestFilePicker` - File picker (returns empty in C#)
- `requestTerminalContext` - Terminal context (returns empty in C#)

### Cloud Session Handlers (Missing)
- `requestCloudSessions` - Request cloud sessions (returns empty in C#)
- `requestCloudSessionData` - Request cloud session data (returns empty in C#)
- `importAndSend` - Import cloud session and send

### Agent/Tool Handlers (Missing)
- `requestAgentRequirements` - Request agent requirements
- `removeSkill` - Remove skill from config
- `removeAgent` - Remove agent from config
- `removeMcp` - Remove MCP server from config

### UI Handlers (Missing)
- `openKiloClaw` - Open KiloClaw panel (placeholder in C#)
- `openMarketplacePanel` - Open marketplace panel (placeholder in C#)
- `openSubAgentViewer` - Open sub-agent viewer (placeholder in C#)
- `openVSCodeSettings` - Open VS Code settings (placeholder in C#)
- `saveImage` - Save image to disk (placeholder in C#)
- `reload` - Reload extension (placeholder in C#)
- `openSettingsTab` - Open specific settings tab (missing)

### Config Handlers (Missing)
- `requestGlobalConfig` - Request global config (implemented in MiscRequestHandlerService)
- `updateConfig` - Update config (implemented but missing optimistic updates)

### Sandbox Handlers (Missing)
- `requestSandboxStatus` - Request sandbox status (returns not implemented in C#)
- `requestSandboxDefault` - Request sandbox default (returns false in C#)
- `setSandboxDefault` - Set sandbox default (placeholder in C#)
- `toggleSandbox` - Toggle sandbox (placeholder in C#)

### Notification Handlers (Missing)
- `dismissNotification` - Dismiss notification (placeholder in C#)
- `testNotification` - Test notification (placeholder in C#)

### Work Style Handlers (Missing)
- `applyWorkStyle` - Apply work style (placeholder in C#)
- `resetReadNotifications` - Reset read notifications (placeholder in C#)

### Fork/Compact Handlers (Missing)
- `forkSession` - Fork session (placeholder in C#)
- `compact` - Compact session context (placeholder in C#)
- `enhancePrompt` - Enhance prompt (placeholder in C#)

### Other Handlers (Missing)
- `anacondaDesktopStatus` - Anaconda desktop status
- `anacondaDesktopOpen` - Open Anaconda desktop
- `anacondaDesktopSync` - Sync Anaconda desktop
- `cancelAnacondaDesktopRequest` - Cancel Anaconda request
- `fetchCustomProviderModels` - Fetch custom provider models (returns empty in C#)
- `requestChatCompletion` - Request chat completion (returns empty in C#)
- `chatCompletionAccepted` - Chat completion accepted (placeholder in C#)
- `questionReject` - Reject question (placeholder in C#)
- `questionReply` - ✅ Reply to question (implemented in InteractionHandlerService)
- `sessionCostAlertResponse` - Handle cost alert response (placeholder in C#)
- `setLanguage` - ✅ Set language (implemented)
- `setOrganization` - ✅ Set organization (implemented)
- `toggleRemote` - Toggle remote status (placeholder in C#)
- `requestRemoteStatus` - Request remote status (returns disabled in C#)
- `requestGitRemoteUrl` - Request git remote URL (returns null in C#)
- `resetAllSettings` - ✅ Reset all settings (implemented)
- `telemetry` - Send telemetry (placeholder in C#)
- `clearLegacyData` - Clear legacy migration data
- `finalizeLegacyMigration` - Finalize legacy migration
- `skipLegacyMigration` - Skip legacy migration
- `startLegacyMigration` - Start legacy migration
- `requestMigrationData` - Request migration data

### Deviations Summary Table

| Category | VS Code Handlers | C# Handlers (VSProvider) | C# Handlers (SSEHelper) | Missing |
|----------|-----------------|---------------------|----------------------|---------|
| Session/Message | ~15 | ~8 | ~7 (sync events) | 5 |
| Authentication | ~5 | ~3 | 0 | 2 |
| Provider | ~5 | 0 | 0 | 5 |
| MCP | ~4 | ~3 | 0 | 1 |
| Settings | ~10 | ~5 | 0 | 5 |
| Models | ~6 | ~3 | 0 | 3 |
| Context Requests | ~4 | ~4 | 0 | 0 |
| Cloud Sessions | ~3 | 0 | 0 | 3 |
| Agents/Tools | ~4 | 0 | 0 | 4 |
| UI | ~8 | ~3 | 0 | 5 |
| Config | ~3 | ~2 | 0 | 1 |
| Sandbox | ~4 | ~2 | ~1 (status changed) | 2 |
| Notifications | ~2 | ~1 | 0 | 1 |
| Work Style | ~2 | ~1 | 0 | 1 |
| Fork/Compact | ~3 | ~1 | 0 | 2 |
| SSE Events | ~24 | 0 | ~20 | 4 |
| Other | ~20 | ~5 | 0 | 15 |
| **Total** | **129** | **43** | **~28** | **58** |

**Note**: The "Missing" column now accounts for handlers implemented in SSEHelper.cs. The original 88 missing handlers is reduced to 58 when including SSE event handlers.

---

## Recommendations

### Critical (Must Fix)
1. **AuthHandlerService**: Implement proper device authentication flow
2. **ModelHandlerService**: Implement actual persistence for variants and recents
3. **SettingsHandlerService**: Read from actual config instead of hardcoded values
4. **InteractionHandlerService**: Add retry logic, editor context, agent requirements

### High Priority
1. **ConfigHandlerService**: Add global/project overlay fetching and optimistic updates
2. **SessionHandlerService**: Add sandbox metadata, comprehensive pruning, all load modes
3. **UiHandlerService**: Implement sub-agent viewer, reload, save image
4. **MiscRequestHandlerService**: Integrate with storage for recents/favorites/variants
5. **McpHandlerService**: Add OAuth flow handling and refresh logic

### Medium Priority
1. Add caching mechanisms to all request handlers
2. Implement proper error handling with user feedback
3. Add abort controller support for cancellable operations
4. Implement prompt recovery logic
5. Add message cost tracking

### Low Priority
1. Add request coalescing for providers
2. Improve logging consistency
3. Add more comprehensive validation

---

## SSE Event Handlers (SSEHelper.cs)

Many message types in VS Code are triggered by **SSE (Server-Sent Events)** rather than direct webview messages. These are handled in VS Code by `mapSSEEventToWebviewMessage` function in `kilo-provider-utils.ts` and in C# by `SSEHelper.cs`.

### SSE Event Handlers in C# (SSEHelper.cs)

The C# SSEHelper implements **32 handler methods** for SSE events:

#### Sync Event Handlers (HandleSyncEvent)
| Event Type | C# Handler | VS Code Equivalent | Status |
|------------|-----------|-------------------|--------|
| `message.updated.1` | `HandleMessageUpdatedSync` | `mapSSEEventToWebviewMessage` | ✅ Implemented |
| `message.removed.1` | `HandleMessageRemovedSync` | `mapSSEEventToWebviewMessage` | ✅ Implemented |
| `message.part.updated.1` | `HandlePartUpdatedSync` | `mapPartEvent` | ✅ Implemented |
| `message.part.removed.1` | `HandlePartRemovedSync` | `mapPartEvent` | ✅ Implemented |
| `session.created.1` | `HandleSessionCreatedSync` | `mapSSEEventToWebviewMessage` | ✅ Implemented |
| `session.updated.1` | `HandleSessionUpdatedSync` | Returns null | ✅ Implemented |
| `session.deleted.1` | `HandleSessionDeletedSync` | `mapSSEEventToWebviewMessage` | ✅ Implemented |

#### Stream Event Handlers (HandleStreamEvent)
| Event Type | C# Handler | VS Code Equivalent | Status |
|------------|-----------|-------------------|--------|
| `session.status` | `HandleSessionStatus` | `mapSSEEventToWebviewMessage` | ✅ Implemented |
| `session.turn.close` | `HandleSessionTurnClosed` | `mapSSEEventToWebviewMessage` | ✅ Implemented |
| `permission.asked` | `HandlePermissionAsked` | `mapSSEEventToWebviewMessage` | ✅ Implemented |
| `permission.replied` | `HandlePermissionReplied` | `mapSSEEventToWebviewMessage` | ✅ Implemented |
| `todo.updated` | `HandleTodoUpdated` | `mapSSEEventToWebviewMessage` | ✅ Implemented |
| `question.asked` | `HandleQuestionAsked` | `mapSSEEventToWebviewMessage` | ✅ Implemented |
| `question.replied` | `HandleQuestionResolved` | `mapSSEEventToWebviewMessage` | ✅ Implemented |
| `question.rejected` | `HandleQuestionResolved` | `mapSSEEventToWebviewMessage` | ✅ Implemented |
| `suggestion.shown` | `HandleSuggestionShown` | `mapSSEEventToWebviewMessage` | ✅ Implemented |
| `suggestion.accepted` | `HandleSuggestionResolved` | `mapSSEEventToWebviewMessage` | ✅ Implemented |
| `suggestion.dismissed` | `HandleSuggestionResolved` | `mapSSEEventToWebviewMessage` | ✅ Implemented |
| `session.error` | `HandleSessionError` | `mapSSEEventToWebviewMessage` | ✅ Implemented |
| `sandbox.status.changed` | `HandleSandboxStatusChanged` | `mapSSEEventToWebviewMessage` | ✅ Implemented |
| `indexing.status` | `HandleIndexingStatus` | `mapSSEEventToWebviewMessage` | ✅ Implemented |
| `memory.status` | `HandleMemoryEvent` | Not in VS Code | ℹ️ C#-specific |
| `memory.updated` | `HandleMemoryEvent` | Not in VS Code | ℹ️ C#-specific |
| `memory.error` | `HandleMemoryEvent` | Not in VS Code | ℹ️ C#-specific |
| `global.disposed` | `HandleGlobalDisposed` | Not in VS Code | ℹ️ C#-specific |
| `server.instance.disposed` | `HandleServerInstanceDisposed` | Not in VS Code | ℹ️ C#-specific |
| `global.config.updated` | `HandleGlobalConfigUpdated` | Not in VS Code | ℹ️ C#-specific |

#### Stream Handlers (Commented Out - Not Implemented)
| Event Type | C# Handler | VS Code Equivalent | Status |
|------------|-----------|-------------------|--------|
| `session.created` | `HandleSessionCreatedStream` | `streams.push` | ❌ Commented out |
| `session.updated` | `HandleSessionUpdatedStream` | `streams.push` | ❌ Commented out |
| `session.deleted` | `HandleSessionDeletedStream` | `streams.push` | ❌ Commented out |
| `message.updated` | `HandleMessageUpdatedStream` | `streams.push` | ❌ Commented out |
| `message.removed` | `HandleMessageRemovedStream` | `streams.push` | ❌ Commented out |
| `message.part.updated` | `HandlePartUpdatedStream` | `streams.push` | ❌ Commented out |
| `message.part.delta` | `HandlePartDelta` | `streams.push` | ⚠️ Partial (no session tracking) |

### SSE Handler Deviations

| Aspect | VS Code | C# | Severity |
|--------|---------|-----|----------|
| Sync events | ✅ Full implementation | ✅ Full implementation | None |
| Stream events (core) | ✅ Full implementation | ✅ Full implementation | None |
| Stream events (delta) | ✅ Uses streams.push | ❌ Commented out | Medium |
| Memory events | ❌ Not present | ✅ C#-specific | Low |
| Global disposed | ❌ Not present | ✅ C#-specific | Low |
| Session tracking | ✅ Auto-adopt child sessions | ✅ Auto-adopt child sessions | None |
| Cost tracking | ✅ In message cost map | ✅ In message cost map | None |

### Summary of SSE Handlers

- **Total SSE event types handled**: 24
- **Fully implemented**: 20
- **Partially implemented**: 1 (message.part.delta)
- **Commented out**: 6 (stream events using old pattern)
- **C#-specific additions**: 4 (memory, global disposed, server disposed, global config updated)

The SSE event handling in C# is **largely complete** and matches VS Code functionality. The main deviation is that some stream events use the old `streams.push` pattern which is commented out in C# - these may need to be re-enabled if the webview expects those messages.

---

## Conclusion

The C# handler services provide a basic skeleton that matches the VS Code message types but lacks significant functionality. The most critical gaps are in authentication, storage integration, retry logic, and session management. The architecture follows a similar pattern with extracted service classes, but the implementation depth varies significantly between the two extensions.

**Key Findings:**
1. **Direct message handlers**: 43 implemented in VSProvider vs 129 in VS Code
2. **SSE event handlers**: 20 fully implemented in SSEHelper.cs (matches VS Code `mapSSEEventToWebviewMessage`)
3. **Missing handlers**: 58 (down from 88 when accounting for SSE handlers)
4. **Most critical missing handlers**: Provider connection, cloud sessions, sub-agent viewer, save image, reload
5. **SSE handlers are complete**: Core sync and stream events are properly implemented
