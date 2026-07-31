# Visual Studio Extension - Native WPF UI Plan

## Goal

Port the JetBrains extension's native Swing UI architecture to Visual Studio using WPF, eliminating the webview entirely. This follows the JetBrains Model/Controller/View pattern and reuses existing backend infrastructure.

## Current State

### What Exists (VS Extension)
- ✅ `CliBackendManager` - CLI process lifecycle
- ✅ `HttpClientWrapper` - HTTP API client with Basic Auth  
- ✅ `SseClient` - SSE event stream with reconnection
- ✅ `KiloConnectionService` - Connection lifecycle management
- ❌ `KiloWebViewControl` - WebView2 host (to be removed)
- ❌ Webview files (index.html, vscode-api.js, webview.js) - to be removed

### What Exists (JetBrains - Target Pattern)
- **Model**: `SessionModel` - Single source of truth for session state
- **Controller**: `SessionController` - Owns model, handles RPC calls, manages SSE events
- **View**: `SessionUi` / `SessionView` - Swing components that listen to model events
- **Layout**: `Stack`, `Align` - Reusable layout primitives
- **Styling**: `UiStyle`, `SessionUiStyle` - Theme-aware colors/fonts
- **Markdown**: `MdView` - Rich text rendering (code blocks, diffs, terminals)

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│ Visual Studio Extension (C# + WPF)                          │
│                                                             │
│  KiloToolWindow                                             │
│  └── SessionView (WPF UserControl)                         │
│       ├── SessionHeaderPanel                               │
│       ├── SessionMessageListPanel                          │
│       │    └── MessageCard (per message)                   │
│       └── PromptPanel                                      │
│            └── PromptEditorTextField                       │
│                                                             │
│  SessionController (owns lifecycle)                         │
│  ├── SessionModel (state)                                  │
│  ├── KiloConnectionService (backend)                       │
│  └── SSE event handlers → model updates                   │
│                                                             │
│  HttpClientWrapper + SseClient (existing)                  │
└─────────────────────────────────────────────────────────────┘
```

## Key Decisions

### UI Technology: WPF
- Visual Studio 2022+ tool windows support WPF
- Better text rendering than WinForms (needed for code blocks)
- Data binding simplifies Model/View synchronization
- Theme integration via `VsBrushes` and `VsFonts`

### Architecture: Model/Controller/View
- Model fires events → View updates (no two-way binding complexity)
- Controller owns backend connection and RPC calls
- Clear separation of concerns, easy to test

### Markdown Rendering: Custom WPF Control
- No WebView2 (defeats purpose of removing webview)
- Parse markdown to AST, render as WPF elements
- Basic syntax highlighting via regex (expand later)

### Theme: Visual Studio Brushes
- Use `VsBrushes.*` and `VsFonts.*` for theme integration
- Automatic dark/light mode switching
- Matches Visual Studio look-and-feel

## Component Mapping

| JetBrains | VS Equivalent | Notes |
|---|---|---|
| `JBLabel` | `TextBlock` | Use `VsFonts.DefaultFont()` |
| `JBTextArea` | `TextBox` (readonly) | For prompt input |
| `JScrollPane` | `ScrollViewer` | Standard WPF scrolling |
| `JPanel` | `Grid` / `StackPanel` | Layout containers |
| `Stack` | `StackPanel` | Same concept |
| `UiStyle.Gap` | `Thickness` constants | DPI-aware spacing |
| `UiStyle.Colors` | `VsBrushes.*` | Theme-aware colors |
| `SessionModel` | `SessionModel` (C#) | Same structure |
| `SessionController` | `SessionController` (C#) | Same logic |
| `MdView` | `MarkdownView` (custom) | Markdown rendering |

## Implementation Tasks

### Phase 1: Foundation

1. **Remove WebView2 dependencies**
   - Delete `KiloWebViewControl.cs`
   - Delete webview files (index.html, vscode-api.js, webview.js, webview.css)
   - Remove WebView2 package from `.csproj`
   - Update `KiloToolWindow.cs` to use WPF content

2. **Create WPF Tool Window structure**
   - Create `SessionView.xaml` - main WPF UserControl
   - Set up XAML namespace for Visual Studio theme brushes
   - Implement basic layout: header, message list, prompt input

3. **Implement SessionModel (C#)**
   - Port `SessionModel.kt` to C#
   - Define message/part/diff data structures
   - Implement event firing mechanism
   - Add `loadHistory()` and `clear()` methods

4. **Implement SessionController (C#)**
   - Port `SessionController.kt` to C#
   - Own `SessionModel` and `KiloConnectionService`
   - Subscribe to SSE events → update model
   - Implement `prompt()`, `replyPermission()`, `replyQuestion()`

### Phase 2: Core UI Components

5. **Implement SessionHeaderPanel**
   - Show session title, model selector, agent mode picker
   - Add timeline/activity indicators

6. **Implement SessionMessageListPanel**
   - Virtualized list of message cards
   - Each card: role, timestamp, content, actions (copy, revert)

7. **Implement MessageCard**
   - Render message content (text, code, tool calls)
   - Copy button, expand/collapse for long messages

8. **Implement PromptPanel**
   - Multi-line text input with mentions support
   - Send button, attachment strip
   - Keyboard shortcuts (Ctrl+Enter to send)

### Phase 3: Markdown Rendering

9. **Implement MarkdownParser**
   - Port parsing logic from JetBrains `MdView`
   - Parse to structured AST (text, code, inline-code, links, lists)

10. **Implement MarkdownView control**
    - Render AST to WPF elements
    - Basic syntax highlighting for code blocks

11. **Implement diff rendering**
    - Render unified diffs with +/- indicators
    - Add "Show Changes" button

### Phase 4: Integration

12. **Connect Controller to View**
    - Wire up `SessionController` → `SessionModel` → `SessionView`
    - Test prompt → SSE → response flow

13. **Implement permission/question UI**
    - Show approval dialogs (modal or inline)
    - Handle user responses

14. **Theme integration**
    - Replace hardcoded colors with `VsBrushes`
    - Replace hardcoded fonts with `VsFonts`
    - Test dark/light mode switching

15. **Error handling and loading states**
    - Loading spinner during connection
    - Error banners for backend errors
    - Handle reconnection gracefully

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| WPF in VS tool windows | Medium | Test on VS 2022; verify full WPF support |
| Markdown rendering performance | Medium | Use UI virtualization; limit rendered content |
| Syntax highlighting complexity | High | Start with basic regex highlighting; expand later |
| Theme brush compatibility | Low | Test all brushes on dark/light themes |

## Validation

### Manual Testing
1. Build VSIX
2. Install in Visual Studio 2022+
3. Open tool window via `Tools > Kilo Code > Open Kilo Code`
4. Verify UI renders without errors
5. Test prompt → response flow
6. Test code block rendering
7. Test dark/light theme switching

### Unit Tests
- `SessionModel` event firing
- `SessionController` SSE event handling
- Markdown parsing

## Open Questions

1. **Syntax highlighting**: Start with regex-based for common languages (bash, python, javascript, typescript), or integrate TextMate grammar parser?
   - **Recommendation**: Start with regex-based; add TextMate later if needed

2. **Diff viewer**: Use Visual Studio's built-in `IVsDiffMerge` or implement inline diff view?
   - **Recommendation**: Start with inline diff view; integrate VS diff viewer later

3. **Mentions/attachments**: How complex is the mentions system (file paths, symbols)?
   - **Action**: Review JetBrains `MentionNavigator` to understand scope before implementing

## Next Steps

1. Verify WPF support in Visual Studio 2022 tool windows
2. Start with Phase 1, Task 1 (remove WebView2)
3. Build components in order, testing each before moving to next
4. Defer syntax highlighting and mentions for MVP
