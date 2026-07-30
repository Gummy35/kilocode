# Visual Studio Extension Enhancement Plan

This plan outlines the steps to bring the Visual Studio extension up to parity with the VS Code extension's functionality.

## Priority: Critical (Fix Now)

### 1. Session Tracking Improvements

**Files to Modify:**
- `packages/kilo-visualstudio/KiloVisualStudioExtension/SSEHelper.cs`
- `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloProvider.cs`

**Changes:**

1. Add public `trackSession()` method to `SSEHelper`:
```csharp
public void TrackSession(string sessionID)
{
    if (!string.IsNullOrEmpty(sessionID))
    {
        _trackedSessionIds.Add(sessionID);
    }
}
```

2. Add `pruneDeletedSession()` method to `SSEHelper`:
```csharp
public void PruneDeletedSession(string sessionID)
{
    _trackedSessionIds.Remove(sessionID);
    _sessionStatusMap.Remove(sessionID);
    _revisions.Remove(sessionID);
}
```

3. Update `HandleCreateSessionAsync` to call `TrackSession`:
```csharp
_sseHelper.TrackSession(sessionID);
```

4. Update `HandleDeleteSessionAsync` to call `PruneDeletedSession`:
```csharp
_sseHelper.PruneDeletedSession(sessionID);
```

**Estimated Effort:** 2 hours

---

### 2. Directory Tracking Per Session

**Files to Modify:**
- `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloProvider.cs`

**Changes:**

1. Add `_sessionDirectories` field:
```csharp
private Dictionary<string, string> _sessionDirectories = new Dictionary<string, string>();
```

2. Add `TrackDirectory()` method:
```csharp
private void TrackDirectory(string sessionID, string directory)
{
    _sessionDirectories[sessionID] = directory;
}
```

3. Add `GetSessionDirectory()` method:
```csharp
private string GetSessionDirectory(string sessionID, object? session = null)
{
    if (_sessionDirectories.TryGetValue(sessionID, out var dir))
        return dir;
    return Environment.CurrentDirectory; // fallback
}
```

4. Call `TrackDirectory()` in `HandleCreateSessionAsync`:
```csharp
TrackDirectory(sessionID, dir);
```

**Estimated Effort:** 1 hour

---

### 3. Abort Controller for Load Messages

**Files to Modify:**
- `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloProvider.cs`

**Changes:**

1. Add `_loadMessagesAbort` field:
```csharp
private AbortController? _loadMessagesAbort;
```

2. Create `AbortController` class (or use `CancellationTokenSource`):
```csharp
public class AbortController
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    public CancellationToken Token => _cts.Token;
    public void Abort() => _cts.Cancel();
    public bool IsAborted => _cts.IsCancellationRequested;
}
```

3. Update `HandleLoadMessagesAsync` to support abort:
```csharp
if (mode == "replace")
{
    _loadMessagesAbort?.Abort();
    _loadMessagesAbort = new AbortController();
}

// Pass token to HTTP request
var responseDoc = await httpClient.GetAsync(url, _loadMessagesAbort?.Token);

// Check if aborted before processing
if (_loadMessagesAbort?.IsAborted == true) return;
```

**Estimated Effort:** 3 hours

---

### 4. Session Status Map Integration

**Files to Modify:**
- `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloProvider.cs`
- `packages/kilo-visualstudio/KiloVisualStudioExtension/SSEHelper.cs`

**Changes:**

1. Expose `SessionStatusMap` from `SSEHelper`:
```csharp
public IReadOnlyDictionary<string, SessionStatus> SessionStatusMap => _sessionStatusMap;
```

2. Add `HandleSessionStatus()` in `KiloProvider`:
```csharp
private void HandleSessionStatus(string sessionID, string status, object? extra = null)
{
    var message = new { type = "sessionStatus", sessionID, status, ...(extra ?? new { }) };
    PostMessage(JsonSerializer.Serialize(message));
}
```

3. Ensure SSE events trigger `HandleSessionStatus`:
```csharp
// In SSEHelper.HandleSessionStatus()
PostMessage(new { type = "sessionStatus", sessionID, status, ... });
```

**Estimated Effort:** 2 hours

---

### 5. Implement handleLoadSessions

**Files to Modify:**
- `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloProvider.cs`

**Changes:**

1. Add `HandleLoadSessionsAsync()` method:
```csharp
private async Task HandleLoadSessionsAsync()
{
    var httpClient = _connectionService.GetHttpClient();
    if (httpClient == null) return;
    
    try
    {
        var responseDoc = await httpClient.GetJsonAsync("/session/list");
        // Parse and send sessionsLoaded message
        var sessions = new List<object>();
        // ... parse response
        var message = new { type = "sessionsLoaded", sessions = sessions.ToArray() };
        PostMessage(JsonSerializer.Serialize(message));
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error loading sessions: {ex.Message}");
    }
}
```

2. Add case handler in `ProcessMessageAsync`:
```csharp
case "loadSessions":
    await HandleLoadSessionsAsync();
    break;
```

**Estimated Effort:** 2 hours

---

## Priority: High (Next Sprint)

### 6. Session Stream Scheduler (Throttling)

**Files to Modify:**
- `packages/kilo-visualstudio/KiloVisualStudioExtension/` (new file)

**Changes:**

Create `SessionStreamScheduler.cs` to throttle part updates:
- Queue part updates per session
- Flush active session immediately
- Flush visible sessions after 100ms
- Flush background sessions after 500ms+

**Estimated Effort:** 6 hours

---

### 7. Implement handleAbort with Backend Process Stopping

**Files to Modify:**
- `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloProvider.cs`

**Changes:**

1. Update `HandleAbortAsync` to call backend:
```csharp
private async Task HandleAbortAsync(JsonElement? payload)
{
    var sessionID = payload.HasValue && payload.Value.TryGetProperty("sessionID", out var sid) && !string.IsNullOrEmpty(sid.GetString())
        ? sid.GetString()
        : _currentSessionID;
    
    if (string.IsNullOrEmpty(sessionID)) return;
    
    var httpClient = _connectionService.GetHttpClient();
    if (httpClient == null) return;
    
    try
    {
        // Call backend to stop processes
        await httpClient.PostAsync($"/background-process/stop-session/{sessionID}", new { });
        
        // Update status
        _sessionStatusMap[sessionID] = "idle";
        PostMessage(JsonSerializer.Serialize(new { type = "sessionTurnClosed", sessionID, reason = "interrupted" }));
        PostMessage(JsonSerializer.Serialize(new { type = "sessionStatus", sessionID, status = "idle" }));
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error aborting session: {ex.Message}");
    }
}
```

**Estimated Effort:** 2 hours

---

### 8. Implement handleRevertSession / handleUnrevertSession

**Files to Modify:**
- `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloProvider.cs`

**Changes:**

1. Add `HandleRevertSessionAsync()`:
```csharp
private async Task HandleRevertSessionAsync(JsonElement? payload)
{
    var sessionID = payload.Value.GetProperty("sessionID").GetString();
    var messageID = payload.Value.GetProperty("messageID").GetString();
    
    var httpClient = _connectionService.GetHttpClient();
    if (httpClient == null) return;
    
    var responseDoc = await httpClient.PostJsonAsync($"/session/revert", new { sessionID, messageID });
    // Parse response and send sessionUpdated
}
```

2. Add `HandleUnrevertSessionAsync()` similarly.

3. Add case handlers in `ProcessMessageAsync`.

**Estimated Effort:** 3 hours

---

### 9. Implement recoverPendingPrompts

**Files to Modify:**
- `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloProvider.cs`

**Changes:**

1. Add fields for pending prompts:
```csharp
private List<PendingPermission> _pendingPermissions = new List<PendingPermission>();
private List<PendingQuestion> _pendingQuestions = new List<PendingQuestion>();
```

2. Add `RecoverPendingPrompts()` method:
```csharp
private void RecoverPendingPrompts()
{
    if (!_isWebviewReady) return;
    // Fetch and resend any permissions/questions that were missed
}
```

3. Call `RecoverPendingPrompts()` after `HandleLoadMessagesAsync`.

**Estimated Effort:** 4 hours

---

## Priority: Medium (Future)

### 10. Implement handleSyncSession (Child Sessions)

### 11. Implement handleCompact (Context Summarization)

### 12. Implement handleEnhancePrompt

### 13. Implement handleForkSession

### 14. Add Missing Message Types

### 15. Implement Project Isolation for SSE Filtering

### 16. Add Cost Tracking

---

## Testing Checklist

After implementing each phase:

- [ ] Session creation works and SSE events are received
- [ ] Session switching loads correct messages
- [ ] Session deletion cleans up tracking
- [ ] Session abort stops backend processes
- [ ] Part updates are throttled (if scheduler implemented)
- [ ] Permissions/questions are recovered after reload
- [ ] Child sessions (task tool) are tracked
- [ ] Directory context is correct for each session

---

## Notes

- The VS Code extension uses `trackedSessionIds` to filter SSE events - this is critical for multi-session scenarios
- The `SessionStreamScheduler` is important for performance when multiple sessions are open
- Abort controllers are needed for canceling in-flight requests during rapid session switching
- Directory tracking is essential for Agent Manager worktree support
