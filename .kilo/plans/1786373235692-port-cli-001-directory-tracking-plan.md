# PORT-CLI-001 — Directory Tracking Mapping Plan

**Task:** PORT-CLI-001  
**Mode:** Plan  
**Date:** 2026-08-10  
**Model:** Qwen3.5-122B

---

## 1. Exact VS Code Behavior

### 1.1 `trackDirectory()` in `KiloConnectionService`

**Location:** `packages/kilo-vscode/src/services/cli-backend/connection-service.ts:531`

```typescript
private trackDirectory(dir: string): void {
  if (!dir) return
  this.rootDirectory ??= dir
  this.currentDirectory = dir
}
```

**Behavior:**
- Accepts a single directory path
- Sets `rootDirectory` only if it's not already set (first-come semantics)
- Always updates `currentDirectory` to the latest directory
- No deduplication logic (uses undefined for root, single value for current)
- No removal mechanism (directories accumulate over extension lifetime)
- No normalization (paths used as-is from caller)

**Call sites in `KiloConnectionService`:**
1. `connect(workspaceDir)` - line 153: `this.trackDirectory(workspaceDir)`
2. `getClientAsync(dir?)` - line 193: `if (dir) this.trackDirectory(dir)`
3. `getClientAsync(dir?)` - line 197: `this.trackDirectory(root)`

### 1.2 `trackDirectory()` in `KiloProvider`

**Location:** `packages/kilo-vscode/src/KiloProvider.ts:4456`

```typescript
private trackDirectory(sessionId: string, dir: string) {
  if (path.resolve(dir) === path.resolve(this.getRootDirectory())) {
    this.sessionDirectories.delete(sessionId)
    return
  }
  this.sessionDirectories.set(sessionId, dir)
}
```

**Behavior:**
- Tracks directories **per session** (maps sessionId → directory)
- Skips tracking if directory equals workspace root (normalized via `path.resolve()`)
- Uses `Map<string, string>` for session-to-directory mapping
- Removes entry if directory matches root

**Call sites in `KiloProvider`:**
1. Line 1800: `this.trackDirectory(session.id, workspaceDir)` - during session creation
2. Line 3167: `this.trackDirectory(session.id, dir)` - during session recovery
3. Line 4021: `this.trackDirectory(eventSessionID, directory)` - on SSE events
4. Line 4492: `this.trackDirectory(session.id, session.directory)` - on followup adoption

### 1.3 `getKnownDirectories()` in `KiloConnectionService`

**Location:** `packages/kilo-vscode/src/services/cli-backend/connection-service.ts:203`

```typescript
getKnownDirectories(): string[] {
  const dirs = new Set<string>()
  if (this.rootDirectory) dirs.add(this.rootDirectory)
  if (this.currentDirectory) dirs.add(this.currentDirectory)
  for (const provider of this.directoryProviders) {
    for (const dir of provider()) {
      if (dir) dirs.add(dir)
    }
  }
  return [...dirs]
}
```

**Behavior:**
- Returns union of:
  - `rootDirectory` (first tracked directory)
  - `currentDirectory` (most recently tracked directory)
  - All directories from registered `directoryProviders`
- Uses `Set` for automatic deduplication
- Filters out falsy values from providers
- Returns array (spread from Set)

**Call sites:**
1. `packages/kilo-vscode/src/services/notebook/bridge.ts:285` - canonicalize and check if directory is allowed
2. `packages/kilo-vscode/src/services/notebook/bridge.ts:327` - recover notebook requests on connection
3. `packages/kilo-vscode/src/agent-manager/orchestration-bridge.ts:320` - recover Agent Manager requests on connection

### 1.4 `directoryProviders` Mechanism

**Location:** `packages/kilo-vscode/src/services/cli-backend/connection-service.ts:524`

```typescript
registerDirectoryProvider(provider: () => string[]): () => void {
  this.directoryProviders.add(provider)
  return () => {
    this.directoryProviders.delete(provider)
  }
}
```

**Behavior:**
- Accepts a callback that returns an array of directories
- Returns unsubscribe function to unregister
- Called by `AgentManagerOrchestrationBridge` (line 106-114):
  ```typescript
  connection.registerDirectoryProvider(() => {
    const root = this.options.root()
    const dirs = this.options.state()?.getWorktrees().map((wt) => wt.path) ?? []
    return root ? [root, ...dirs] : dirs
  })
  ```

### 1.5 Data Structures Summary

| Field | Type | Purpose |
|-------|------|---------|
| `rootDirectory` | `string \| undefined` | First tracked directory (workspace root) |
| `currentDirectory` | `string \| undefined` | Most recently tracked directory |
| `directoryProviders` | `Set<DirectoryProvider>` | Dynamic directory sources (Agent Manager worktrees) |
| `sessionDirectories` (KiloProvider) | `Map<string, string>` | Per-session directory mapping |

---

## 2. Existing Visual Studio Equivalent

### 2.1 Current Implementation in `KiloConnectionService.cs`

**Location:** `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloConnectionService.cs:736-748`

```csharp
public void TrackDirectory(string directory)
{
    lock (_visibilityLock)
    {
        _knownDirectories.Add(directory);
    }
}

public HashSet<string> GetKnownDirectories()
{
    lock (_visibilityLock)
    {
        return new HashSet<string>(_knownDirectories, StringComparer.OrdinalIgnoreCase);
    }
}
```

**Current behavior:**
- Uses `HashSet<string>` (`_knownDirectories`) for storage
- `TrackDirectory()` adds to set (no root/current distinction)
- `GetKnownDirectories()` returns copy of set
- Thread-safe with lock
- Case-insensitive comparison on Windows

### 2.2 Missing VS Code Features in Current VS Implementation

| VS Code Feature | VS Implementation | Gap |
|-----------------|-------------------|-----|
| `rootDirectory` (first-tracked) | ❌ Not present | No first-tracked semantics |
| `currentDirectory` (latest) | ❌ Not present | No latest-tracked semantics |
| `directoryProviders` (dynamic) | ❌ Not present | No dynamic provider registration |
| `sessionDirectories` (per-session) | ❌ Not present | No per-session tracking |
| Path normalization (`path.resolve()`) | ❌ Not present | No path normalization |
| Root directory skip logic | ❌ Not present | No skip logic |

### 2.3 Current Callers of KiloConnectionService

**Search results:** Directory tracking methods (`TrackDirectory`, `GetKnownDirectories`, `RecordPermissionDirectory`, `RecordQuestionDirectory`) are **defined but never called** in the Visual Studio extension source code.

**Implication:** The current implementation is a direct port of the API surface but has no active usage. The plan must determine **where** these methods should be called to match VS Code behavior.

---

## 3. Identified Parity Gap

### 3.1 Core Differences

| Aspect | VS Code | Visual Studio | Impact |
|--------|---------|---------------|--------|
| Storage model | `rootDirectory` + `currentDirectory` (single values) | `HashSet<string>` (accumulating set) | **Behavioral difference** |
| Directory deduplication | `Set` in `getKnownDirectories()` | `HashSet` storage | Equivalent |
| Dynamic sources | `directoryProviders` callbacks | None | **Missing feature** |
| Per-session tracking | `sessionDirectories` Map | None | **Missing feature** |
| Path normalization | `path.resolve()` comparisons | None | **Potential correctness issue** |
| Root skip logic | Skip if equals workspace root | None | **Functional difference** |

### 3.2 Usage Pattern Differences

**VS Code:**
- `trackDirectory()` called from `connect()`, `getClientAsync()`, and `KiloProvider` session tracking
- `getKnownDirectories()` used by NotebookBridge and AgentManagerOrchestrationBridge for recovery
- `directoryProviders` used by Agent Manager to expose worktree directories

**Visual Studio:**
- Methods exist but **not called anywhere**
- No equivalent to `KiloProvider` session tracking
- No Agent Manager integration (if present, would need directory provider registration)

---

## 4. Minimal C# Implementation Required

### 4.1 Required for Correctness

The following changes are **required** to preserve observable VS Code behavior:

#### 4.1.1 Update `TrackDirectory()` Signature and Behavior

**Current:**
```csharp
public void TrackDirectory(string directory)
{
    lock (_visibilityLock)
    {
        _knownDirectories.Add(directory);
    }
}
```

**Required change:** Match VS Code's `rootDirectory` / `currentDirectory` semantics

```csharp
private string? _rootDirectory;
private string? _currentDirectory;

public void TrackDirectory(string directory)
{
    if (string.IsNullOrEmpty(directory))
        return;
    
    lock (_visibilityLock)
    {
        _rootDirectory ??= directory;  // First-tracked semantics
        _currentDirectory = directory; // Always update latest
    }
}
```

#### 4.1.2 Update `GetKnownDirectories()` Implementation

**Current:**
```csharp
public HashSet<string> GetKnownDirectories()
{
    lock (_visibilityLock)
    {
        return new HashSet<string>(_knownDirectories, StringComparer.OrdinalIgnoreCase);
    }
}
```

**Required change:** Return union of root, current, and provider directories

```csharp
public string[] GetKnownDirectories()
{
    lock (_visibilityLock)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(_rootDirectory))
            dirs.Add(_rootDirectory);
        if (!string.IsNullOrEmpty(_currentDirectory))
            dirs.Add(_currentDirectory);
        // Note: directoryProviders not implemented yet (see below)
        return dirs.ToArray();
    }
}
```

#### 4.1.3 Add Directory Provider Registration

**Required for Agent Manager parity:**

```csharp
private readonly HashSet<Func<string[]>> _directoryProviders = new();

public Func<bool> RegisterDirectoryProvider(Func<string[]> provider)
{
    lock (_visibilityLock)
    {
        _directoryProviders.Add(provider);
    }
    return () =>
    {
        lock (_visibilityLock)
        {
            _directoryProviders.Remove(provider);
        }
    };
}
```

Update `GetKnownDirectories()` to include provider directories:

```csharp
public string[] GetKnownDirectories()
{
    lock (_visibilityLock)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(_rootDirectory))
            dirs.Add(_rootDirectory);
        if (!string.IsNullOrEmpty(_currentDirectory))
            dirs.Add(_currentDirectory);
        foreach (var provider in _directoryProviders)
        {
            foreach (var dir in provider())
            {
                if (!string.IsNullOrEmpty(dir))
                    dirs.Add(dir);
            }
        }
        return dirs.ToArray();
    }
}
```

### 4.2 Useful Robustness/Performance Behavior

#### 4.2.1 Path Normalization (Optional but Recommended)

VS Code uses `path.resolve()` to normalize paths before comparison. In C#, this would be:

```csharp
private string NormalizePath(string path)
{
    return Path.GetFullPath(path).ToLowerInvariant();
}
```

**Use case:** Skip tracking if directory equals workspace root (matches `KiloProvider.trackDirectory()` logic).

**Decision:** Defer unless a specific use case emerges in Visual Studio.

### 4.3 Unrelated VS Code Behavior (Out of Scope)

The following VS Code features are **not required** for this port:

1. **`sessionDirectories` Map in `KiloProvider`**: This is VS Code-specific session tracking that doesn't have a direct equivalent in the current Visual Studio architecture. The Visual Studio extension uses a different session management approach.

2. **Permission/Question directory tracking**: While methods exist (`RecordPermissionDirectory`, `RecordQuestionDirectory`), they are not actively used in the current Visual Studio implementation and should remain as-is.

3. **`drainPendingPrompts()`**: Deferred in PORT-CLI-001 implementation (see Section 5.3 of the main plan).

---

## 5. Files Requiring Modification

### 5.1 Primary Changes

| File | Changes | Lines Affected |
|------|---------|----------------|
| `KiloConnectionService.cs` | Update `TrackDirectory()`, `GetKnownDirectories()`, add `RegisterDirectoryProvider()` | ~736-760 |

### 5.2 Potential Future Changes (Not Required Now)

| File | When Needed | Purpose |
|------|-------------|---------|
| `AgentManagerProvider.cs` | If Agent Manager worktree support is added | Register directory provider for worktree paths |
| `VSProvider.cs` | If session tracking is implemented | Call `TrackDirectory()` on session creation |
| `SessionCreatorService.cs` | If session tracking is implemented | Call `TrackDirectory()` on session init |

---

## 6. Required Tests

### 6.1 Unit Tests (If Test Infrastructure Available)

**Note:** The test project has pre-existing compilation errors (see PORT-CLI-001 Section 5.1). Tests should be added when test infrastructure is fixed.

1. **TrackDirectory first-tracked semantics**: Verify `_rootDirectory` is set on first call and not changed on subsequent calls
2. **TrackDirectory current tracking**: Verify `_currentDirectory` is updated on every call
3. **GetKnownDirectories deduplication**: Verify root and current are deduplicated when they're the same value
4. **RegisterDirectoryProvider lifecycle**: Verify provider directories are included in `GetKnownDirectories()` and excluded after unregister
5. **Thread safety**: Verify concurrent calls to `TrackDirectory()` and `GetKnownDirectories()` don't cause race conditions

### 6.2 Integration Tests

None required at this stage. Directory tracking is a supporting feature for NotebookBridge and AgentManagerOrchestrationBridge, which don't exist in the Visual Studio extension yet.

---

## 7. Out of Scope

The following behaviors are explicitly **out of scope** for this port:

1. **`KiloProvider.sessionDirectories` per-session tracking**: The Visual Studio extension doesn't have an equivalent to `KiloProvider`. Session tracking would need to be designed separately.

2. **Path normalization with `Path.GetFullPath()`**: VS Code uses this to compare directories, but the Visual Studio extension may not need this level of normalization. Add only if a specific use case emerges.

3. **Permission/Question directory pruning**: The `prunePermissionDirectories()` and `pruneQuestionDirectories()` methods exist in VS Code but are not actively used in the current Visual Studio implementation.

4. **NotebookBridge integration**: The Visual Studio extension doesn't currently have notebook support.

5. **Agent Manager worktree directory providers**: If Agent Manager worktree support is added in the future, this would require implementing `RegisterDirectoryProvider()` and calling it from the Agent Manager provider.

---

## 8. Implementation Checklist

- [ ] Update `TrackDirectory()` to use `_rootDirectory` / `_currentDirectory` pattern
- [ ] Update `GetKnownDirectories()` to return union of root, current, and provider directories
- [ ] Add `_directoryProviders` field and `RegisterDirectoryProvider()` method
- [ ] Remove `_knownDirectories` HashSet (no longer needed)
- [ ] Verify build succeeds with `dotnet build`
- [ ] Document changes in PORT-CLI-001 execution history

---

## 9. Decision Log

| Decision | Rationale |
|----------|-----------|
| Use `_rootDirectory ??= directory` pattern | Matches VS Code's first-tracked semantics |
| Always update `_currentDirectory` | Matches VS Code's latest-tracked semantics |
| Add `RegisterDirectoryProvider()` | Required for future Agent Manager worktree support |
| Defer path normalization | No current use case; can add later if needed |
| Keep permission/question tracking as-is | Not actively used; no changes required |

---

## 10. Validation Steps

1. **Build verification**: `dotnet build` should succeed with 0 errors
2. **Code review**: Verify changes match VS Code semantics
3. **API compatibility**: Ensure `TrackDirectory()` and `GetKnownDirectories()` signatures remain compatible with existing callers (currently none)
4. **Thread safety**: Verify lock usage is consistent with existing patterns in `KiloConnectionService`

---

**Plan status:** Ready for Code execution  
**Estimated effort:** Small (1 file, ~30 lines changed)  
**Risk level:** Low (no active callers, backward-compatible API)
