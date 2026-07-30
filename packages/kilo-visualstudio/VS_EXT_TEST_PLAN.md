# Visual Studio Extension Test Adaptation Plan

This document outlines how to adapt VS Code extension unit tests for the Visual Studio extension. The VS Code tests serve as behavioral specifications that should be ported to validate equivalent functionality in the C# Visual Studio extension.

## Current Test Coverage Status

**As of 2026-07-30:**

- **VS Code Tests:** 252 total
- **VS Extension Tests:** 45 files (213 passing, 41 intentional failures, 254 total)
- **Critical Tests Coverage:** ~50% (35 of 71 critical tests ported)

### Latest Addition

- `DiffHashTests.cs` - Diff hash computation, image detection, diff source catalog tests (refactored)
- `MessagePageTests.cs` - Message page fetching and cursor handling tests (refactored)
- `AgentBehaviourPatchesTests.cs` - Agent settings behaviour patch tests (text/numeric overrides, default agent clearing)

### Test Files Refactored (Latest Session)

The following test files were refactored to move production code from test files into the KiloVisualStudioExtension project:

**Production Services Created:**
1. `DiffHashService.cs` - Diff hash computation
2. `DiffImageUtils.cs` - Image file detection utilities
3. `DiffSourceCatalog.cs` - Diff source display name mapping
4. `MessagePageFetcher.cs` - Message page fetching with cursor handling
5. `AgentBehaviourPatches.cs` - Agent settings value mapping utilities

**Test Files Updated:**
1. `DiffHashTests.cs` - Removed embedded production code, now uses `DiffHashService`, `DiffImageUtils`, `DiffSourceCatalog`
2. `MessagePageTests.cs` - Removed embedded production code, now uses `MessagePageFetcher`
3. `AgentBehaviourPatchesTests.cs` - Removed embedded production code, now uses `AgentBehaviourPatches`

### Test Files Added (Latest Session)

The following test files were added in the latest implementation session:

**Agent Manager Tests:**
1. `AgentManagerArchTests.cs` - Agent Manager CSS prefix and architecture tests
2. `AgentManagerCloseSessionTests.cs` - Session close behavior tests
3. `AgentManagerDiffStateTests.cs` - Diff state management tests
4. `AgentManagerI18nTests.cs` - Localization lint tests
5. `AgentManagerInitialMessageTests.cs` - Initial message handling and i18n split tests
6. `AgentManagerMcpWarmupTests.cs` - MCP warmup and memory command tests
7. `AgentManagerTerminalFontTests.cs` - Terminal font resolution tests
8. `AgentManagerToolStartTests.cs` - Tool start parsing tests

**Session & Provider Tests:**
9. `SessionQueueTests.cs` - Session queue, tab switcher, and terminal manager tests
10. `KiloProviderUtilsTests.cs` - SSE event mapping, message confirmation, agent filtering tests
11. `KiloProviderLoadMessagesTests.cs` - Load messages and background process stopping tests
12. `CloudSessionHandlerTests.cs` - Cloud session import and timeout handling tests

**Diff & Export Tests:**
13. `DiffHashTests.cs` - Diff hash computation, image detection, and preview tests
14. `ExportTranscriptTests.cs` - Export transcript, message contract, prompt drafts tests

**Additional Critical Tests:**
15. `RevertCheckpointTests.cs` - Checkpoint creation and retrieval tests
16. `TodoRevertTests.cs` - Todo revert functionality tests
17. `PermissionDiffUtilsTests.cs` - Permission diff comparison tests
18. `ForkHandoffTests.cs` - Fork session creation tests
19. `QuestionHandlerTests.cs` - Question formatting and validation tests
20. `ConnectionServiceQuestionTests.cs` - Question handling tests (merged into RevertCheckpointTests.cs)
21. `SessionUtilsTests.cs` - Session utility functions tests (merged)
22. `SessionModelStoreTests.cs` - Model usage tracking tests (merged)
23. `SessionTabSwitcherTests.cs` - Tab switching behavior tests (merged into SessionQueueTests.cs)
24. `SessionTerminalManagerTests.cs` - Terminal management tests (merged into SessionQueueTests.cs)
25. `SessionVariantStoreTests.cs` - Session variant state tests (merged into SessionUtilsTests.cs)
26. `CloudSessionHandlerTests.cs` - Cloud session sync tests
27. `DiffViewerCssArchTests.cs` - Diff viewer CSS architecture tests
28. `DiffSessionSourceTests.cs` - Diff session source tests
29. `DiffSourceCatalogTests.cs` - Diff source catalog tests
30. `DiffTurnSourceTests.cs` - Diff turn source tests
31. `PromptDraftsTests.cs` - Prompt draft storage tests
32. `PromptHistoryTests.cs` - Prompt history management tests

### Intentional Failures

The 37 intentional failures detect implementation gaps between the VS extension and VS Code behavior. These tests are designed to FAIL until the VS extension implements the missing features.

## Table of Contents

1. [Test Architecture Overview](#test-architecture-overview)
2. [Test Categories to Port](#test-categories-to-port)
3. [Adaptation Strategy](#adaptation-strategy)
4. [Test Mapping: VS Code → Visual Studio](#test-mapping-vs-code--visual-studio)
5. [Implementation Priority](#implementation-priority)
6. [Testing Framework Setup](#testing-framework-setup)
7. [Mocking Strategy](#mocking-strategy)

---

## Test Architecture Overview

### VS Code Test Structure

```
packages/kilo-vscode/
├── tests/
│   ├── unit/                    # Unit tests (bun:test)
│   │   ├── session-stream-scheduler.test.ts
│   │   ├── kilo-provider-load-messages.test.ts
│   │   ├── abort.test.ts
│   │   └── ... (150+ test files)
│   ├── fixtures/                # Test fixtures/helpers
│   │   └── session-tab-switcher.tsx
│   └── setup/                   # Test setup
│       └── vscode-mock.ts
└── src/test/
    └── extension.test.ts        # Integration tests (mocha)
```

### Visual Studio Test Structure (Target)

```
packages/kilo-visualstudio/
└── KiloVisualStudioExtension/
    ├── tests/                   # NEW: Unit tests (xUnit/NUnit)
    │   ├── SessionTrackingTests.cs
    │   ├── MessageLoadingTests.cs
    │   ├── AbortTests.cs
    │   └── ...
    └── test/                    # Integration tests (optional)
        └── ExtensionTests.cs
```

---

## Test Categories to Port

### Category 1: Session Management (CRITICAL)

| VS Code Test | Purpose | Visual Studio Equivalent | Priority |
|--------------|---------|-------------------------|----------|
| `session-stream-scheduler.test.ts` | Test part update throttling | `SessionStreamSchedulerTests.cs` | High |
| `kilo-provider-load-messages.test.ts` | Test message loading modes | `MessageLoadingTests.cs` | Critical |
| `abort.test.ts` | Test session abort behavior | `AbortTests.cs` | Critical |
| `kilo-provider-rename.test.ts` | Test session rename | `SessionRenameTests.cs` | Medium |
| `kilo-provider-session-refresh.test.ts` | Test session metadata refresh | `SessionRefreshTests.cs` | Medium |

### Category 2: Session Tracking

| VS Code Test | Purpose | Visual Studio Equivalent | Priority |
|--------------|---------|-------------------------|----------|
| `kilo-provider-load-messages.test.ts` (sidebar tabs) | Test draft session tracking | `SessionTrackingTests.cs` | Critical |
| `kilo-provider-load-messages.test.ts` (untrack) | Test session untracking on delete | `SessionTrackingTests.cs` | Critical |
| `abort.test.ts` (SessionAbort) | Test abort owner tracking | `AbortTrackingTests.cs` | High |

### Category 3: SSE Event Handling

| VS Code Test | Purpose | Visual Studio Equivalent | Priority |
|--------------|---------|-------------------------|----------|
| `kilo-provider-load-messages.test.ts` (sandbox status) | Test directory-filtered events | `SSEEventFilteringTests.cs` | High |
| `kilo-provider-load-messages.test.ts` (revert ordering) | Test sync event unwrapping | `SSEEventOrderingTests.cs` | Medium |
| `kilo-provider-load-messages.test.ts` (cost alerts) | Test cost threshold alerts | `CostAlertTests.cs` | Medium |

### Category 4: Abort & Process Management

| VS Code Test | Purpose | Visual Studio Equivalent | Priority |
|--------------|---------|-------------------------|----------|
| `abort.test.ts` (multi-owner) | Test abort across worktrees | `AbortMultiOwnerTests.cs` | High |
| `abort.test.ts` (pending tabs) | Test abort on tab close | `AbortOnTabCloseTests.cs` | Medium |
| `background-process.test.ts` | Test background process stopping | `BackgroundProcessTests.cs` | High |

### Category 5: State Management

| VS Code Test | Purpose | Visual Studio Equivalent | Priority |
|--------------|---------|-------------------------|----------|
| `draft-store.test.ts` | Test draft session storage | `DraftSessionTests.cs` | Medium |
| `session-preferences.test.ts` | Test session preferences | `SessionPreferencesTests.cs` | Low |
| `session-model-store.test.ts` | Test model selection persistence | `ModelSelectionTests.cs` | Low |

### Category 6: Integration Patterns

| VS Code Test | Purpose | Visual Studio Equivalent | Priority |
|--------------|---------|-------------------------|----------|
| `session-tab-switcher.test.ts` | Test tab switcher UI | N/A (UI test) | N/A |
| `connection-service-question.test.ts` | Test question handling | `QuestionHandlingTests.cs` | Medium |
| `cloud-session-handler.test.ts` | Test cloud session sync | `CloudSessionTests.cs` | Low |

---

## Adaptation Strategy

### 1. Test Framework Selection

**Recommended: xUnit.NET**

```xml
<!-- KiloVisualStudioExtension.csproj -->
<ItemGroup>
  <PackageReference Include="xunit" Version="2.9.0" />
  <PackageReference Include="xunit.runner.visualstudio" Version="2.8.0" />
  <PackageReference Include="Moq" Version="4.20.70" />
  <PackageReference Include="FluentAssertions" Version="6.12.0" />
</ItemGroup>
```

**Alternative: NUnit**

```xml
<ItemGroup>
  <PackageReference Include="NUnit" Version="4.1.0" />
  <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
  <PackageReference Include="Moq" Version="4.20.70" />
</ItemGroup>
```

### 2. Test Structure Pattern

```csharp
// VS Code pattern (bun:test)
describe("KiloProvider.handleLoadMessages", () => {
  it("stops background processes for previous session", async () => {
    const client = createClient()
    const { internal } = makeProvider(client)
    internal.currentSession = { id: "s1", directory: "/repo/old" }
    
    await internal.handleLoadMessages("s2")
    
    expect(client.stopped).toEqual([{ sessionID: "s1", directory: "/repo/old" }])
  })
})

// Visual Studio pattern (xUnit)
public class MessageLoadingTests
{
    [Fact]
    public async Task HandleLoadMessages_StopsBackgroundProcessesForPreviousSession()
    {
        // Arrange
        var client = new MockHttpClient();
        var provider = new VSProvider(webView: Mock.Of<KiloWebViewControl>(), 
                                       connectionService: CreateConnection(client));
        provider.CurrentSession = new Session { Id = "s1", Directory = @"C:\repo\old" };
        
        // Act
        await provider.HandleLoadMessagesAsync(JsonDocument.Parse("{\"sessionID\":\"s2\"}").RootElement);
        
        // Assert
        client.StoppedSessions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { SessionID = "s1", Directory = @"C:\repo\old" });
    }
}
```

### 3. Mocking Strategy

#### Mock HTTP Client

```csharp
public class MockHttpClient : HttpClient
{
    private readonly Dictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> _handlers = new();
    private readonly List<RequestRecord> _requests = new();
    
    public List<RequestRecord> Requests => _requests;
    public List<StoppedSession> StoppedSessions { get; } = new();
    
    public void RegisterHandler(string method, string path, Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        _handlers[$"{method}:{path}"] = handler;
    }
    
    public override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Add(new RequestRecord(request.Method.Method, request.RequestUri?.ToString(), request.Content));
        
        var key = $"{request.Method.Method}:{request.RequestUri?.PathAndQuery}";
        if (_handlers.TryGetValue(key, out var handler))
        {
            return await handler(request);
        }
        
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

public record RequestRecord(string Method, string? Url, HttpContent? Content);
public record StoppedSession(string SessionID, string Directory);
```

#### Mock SSE Helper

```csharp
public class MockSSEHelper : SSEHelper
{
    public List<string> TrackedSessions { get; } = new();
    public List<string> UntrackedSessions { get; } = new();
    public List<string> PostedMessages { get; } = new();
    
    public MockSSEHelper() : base(message => PostedMessages.Add(message))
    {
    }
    
    public override void TrackSession(string sessionID)
    {
        TrackedSessions.Add(sessionID);
        base.TrackSession(sessionID);
    }
    
    public override void UntrackSession(string sessionID)
    {
        UntrackedSessions.Add(sessionID);
        base.UntrackSession(sessionID);
    }
}
```

---

## Test Mapping: VS Code → Visual Studio

### Critical Tests (Port First)

#### 1. Session Tracking Tests

**Source:** `kilo-provider-load-messages.test.ts` (lines 548-568)

```csharp
// Visual Studio adaptation
public class SessionTrackingTests
{
    [Fact]
    public void TrackOpenSessions_AddsSessionsToTrackedSet()
    {
        // Arrange
        var sseHelper = new MockSSEHelper();
        var provider = CreateProvider(sseHelper);
        
        // Act
        provider.TrackOpenSessions(new[] { "s1", "s2" });
        
        // Assert
        sseHelper.TrackedSessions.Should().BeEquivalentTo("s1", "s2");
    }
    
    [Fact]
    public void TrackOpenSessions_RemovesSessionsWhenUntracked()
    {
        // Arrange
        var sseHelper = new MockSSEHelper();
        var provider = CreateProvider(sseHelper);
        provider.TrackOpenSessions(new[] { "s1", "s2" });
        
        // Act
        provider.TrackOpenSessions(new[] { "s2" });
        
        // Assert
        sseHelper.TrackedSessions.Should().Contain("s2");
        sseHelper.UntrackedSessions.Should().Contain("s1");
    }
}
```

#### 2. Message Loading Tests

**Source:** `kilo-provider-load-messages.test.ts` (lines 814-830)

```csharp
public class MessageLoadingTests
{
    [Fact]
    public async Task HandleLoadMessages_StopsProcessesForPreviousSession()
    {
        // Arrange
        var httpClient = new MockHttpClient();
        var provider = CreateProvider(httpClient);
        provider.CurrentSession = new Session { Id = "s1", Directory = @"C:\repo\old" };
        
        // Mock messages response
        httpClient.RegisterHandler("GET", "/session/s2/message", _ => 
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        
        // Act
        await provider.HandleLoadMessagesAsync(JsonDocument.Parse("{\"sessionID\":\"s2\"}").RootElement);
        
        // Assert
        httpClient.StoppedSessions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { SessionID = "s1", Directory = @"C:\repo\old" });
    }
    
    [Fact]
    public async Task HandleLoadMessages_FocusMode_ReconcilesTail()
    {
        // Arrange
        var messages = new[]
        {
            CreateMessage("m1", "user", 1),
            CreateMessage("m2", "assistant", 2),
            CreateMessage("m3", "user", 3) // Missed by SSE
        };
        var httpClient = CreateMockHttpClientWithMessages(messages);
        var provider = CreateProvider(httpClient);
        
        // Act
        await provider.HandleLoadMessagesAsync(JsonDocument.Parse("{\"sessionID\":\"s1\",\"mode\":\"focus\"}").RootElement);
        
        // Assert
        var loadedMessage = provider.PostedMessages
            .Select(m => JsonSerializer.Deserialize<MessagesLoadedMessage>(m))
            .FirstOrDefault(m => m != null);
        
        loadedMessage.Should().NotBeNull();
        loadedMessage!.Mode.Should().Be("reconcile");
        loadedMessage.Messages.Should().Contain(m => m.Id == "m3");
    }
}
```

#### 3. Abort Tests

**Source:** `abort.test.ts`

```csharp
public class AbortTests
{
    [Fact]
    public async Task HandleAbort_StopsActiveOwnerAndCurrentDirectory()
    {
        // Arrange
        var httpClient = new MockHttpClient();
        var sseHelper = new MockSSEHelper();
        var provider = CreateProvider(httpClient, sseHelper);
        
        // Observe busy state
        sseHelper.Observe("session_1", "busy", @"C:\repo");
        
        // Act
        await provider.HandleAbortAsync(JsonDocument.Parse("{\"sessionID\":\"session_1\"}").RootElement);
        
        // Assert
        httpClient.AbortedSessions.Should().ContainEquivalentOf(new { SessionID = "session_1", Directory = @"C:\repo" });
        httpClient.AbortedSessions.Should().ContainEquivalentOf(new { SessionID = "session_1", Directory = @"C:\repo\worktree" });
    }
    
    [Fact]
    public async Task HandleAbort_ForgotsOwnerWhenIdle()
    {
        // Arrange
        var httpClient = new MockHttpClient();
        var sseHelper = new MockSSEHelper();
        var provider = CreateProvider(httpClient, sseHelper);
        
        sseHelper.Observe("session_1", "busy", @"C:\repo");
        sseHelper.Observe("session_1", "idle", @"C:\repo"); // Becomes idle
        
        // Act
        await provider.HandleAbortAsync(JsonDocument.Parse("{\"sessionID\":\"session_1\"}").RootElement);
        
        // Assert - only current directory should be aborted
        httpClient.AbortedSessions.Should().HaveCount(1);
    }
}
```

---

## Implementation Priority

### Phase 1: Core Session Management (Week 1)

1. **SessionTrackingTests.cs** - Test session tracking/untracking
2. **MessageLoadingTests.cs** - Test load messages with abort
3. **AbortTests.cs** - Test abort behavior

**Expected Coverage:** 15-20 tests

### Phase 2: SSE & Event Handling (Week 2)

4. **SSEEventFilteringTests.cs** - Test directory-filtered events
5. **SSEEventOrderingTests.cs** - Test sync event handling
6. **CostAlertTests.cs** - Test cost threshold alerts

**Expected Coverage:** 10-15 tests

### Phase 3: Advanced Features (Week 3)

7. **SessionRenameTests.cs** - Test session rename
8. **SessionRefreshTests.cs** - Test metadata refresh
9. **BackgroundProcessTests.cs** - Test process stopping
10. **DraftSessionTests.cs** - Test draft session handling

**Expected Coverage:** 15-20 tests

### Phase 4: Integration Tests (Week 4)

11. **QuestionHandlingTests.cs** - Test question/answer flow
12. **CloudSessionTests.cs** - Test cloud session sync
13. **ModelSelectionTests.cs** - Test model persistence

**Expected Coverage:** 10-15 tests

---

## Testing Framework Setup

### 1. Add Test Project

```bash
# Create test project
dotnet new xunit -n KiloVisualStudioExtension.Tests -o packages/kilo-visualstudio/KiloVisualStudioExtension.Tests

# Add reference to main project
dotnet add packages/kilo-visualstudio/KiloVisualStudioExtension.Tests/KiloVisualStudioExtension.Tests.csproj reference packages/kilo-visualstudio/KiloVisualStudioExtension/KiloVisualStudioExtension.csproj
```

### 2. Configure Test Project

```xml
<!-- KiloVisualStudioExtension.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net481</TargetFramework>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.0" />
    <PackageReference Include="Moq" Version="4.20.70" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>
  
  <ItemGroup>
    <ProjectReference Include="..\KiloVisualStudioExtension\KiloVisualStudioExtension.csproj" />
  </ItemGroup>
</Project>
```

### 3. Run Tests

```bash
# Run all tests
dotnet test packages/kilo-visualstudio/KiloVisualStudioExtension.Tests/

# Run specific test file
dotnet test --filter "FullyQualifiedName~SessionTrackingTests"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

---

## Mocking Strategy

### Mock KiloConnectionService

```csharp
public class MockKiloConnectionService : KiloConnectionService
{
    public MockKiloConnectionService()
    {
        _httpClient = new MockHttpClient();
    }
    
    public MockHttpClient HttpClient => (MockHttpClient)_httpClient;
    
    public override HttpClient GetHttpClient() => _httpClient;
    
    public override Task ConnectAsync() => Task.CompletedTask;
    
    public override ConnectionState State => ConnectionState.Connected;
}
```

### Mock KiloWebViewControl

```csharp
public class MockKiloWebViewControl : KiloWebViewControl
{
    public List<string> PostedMessages { get; } = new();
    
    public override void PostMessage(string message)
    {
        PostedMessages.Add(message);
    }
}
```

### Test Helper Methods

```csharp
public class TestHelpers
{
    public static VSProvider CreateProvider(MockHttpClient? httpClient = null, MockSSEHelper? sseHelper = null)
    {
        var webView = new MockKiloWebViewControl();
        var connectionService = new MockKiloConnectionService();
        
        if (httpClient != null)
        {
            // Inject mock HTTP client
        }
        
        if (sseHelper != null)
        {
            // Inject mock SSE helper
        }
        
        return new VSProvider(webView, connectionService);
    }
    
    public static JsonElement CreateMessage(string id, string role, long time)
    {
        var doc = JsonDocument.Parse($$"""
        {
            "info": {
                "id": "{{id}}",
                "sessionID": "s1",
                "role": "{{role}}",
                "time": { "created": {{time}} }
            },
            "parts": []
        }
        """);
        return doc.RootElement;
    }
}
```

---

## Test Coverage Goals

| Category | Target Coverage | Critical Tests |
|----------|----------------|----------------|
| Session Management | 90% | create, delete, switch, clear |
| Message Loading | 85% | replace, focus, prepend, reconcile |
| Abort Handling | 90% | stop, multi-owner, pending tabs |
| SSE Events | 80% | filter, order, cost alerts |
| State Persistence | 75% | getState, setState, recovery |

---

## Notes

1. **Focus on Behavior, Not Implementation**: Tests should validate the same behavioral contracts as VS Code tests, not necessarily the same implementation details.

2. **Adapt for C# Idioms**: Use C# patterns (properties, events, async/await) instead of TypeScript patterns.

3. **Leverage Existing Mocks**: Reuse mock classes from the main project where possible.

4. **Test in Parallel**: xUnit runs tests in parallel by default - ensure tests are isolated.

5. **CI Integration**: Add test step to build pipeline:
   ```yaml
   - script: dotnet test
     displayName: 'Run Tests'
   ```

6. **Flaky Tests**: If a test is flaky, investigate the root cause rather than adding retries.

7. **Documentation**: Each test file should have XML documentation explaining what it tests and why.
