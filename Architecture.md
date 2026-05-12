# Nexaflow Architecture

## Table of Contents

1. [Solution Layout](#solution-layout)
2. [Module Responsibilities](#module-responsibilities)
3. [Key Data Models](#key-data-models)
4. [Extensibility Points](#extensibility-points)
5. [Core Flows](#core-flows)
6. [Separation-of-Concerns Issues](#separation-of-concerns-issues)

---

## Solution Layout

```
nexaflow/src/
├── Nexaflow.Core/                        ← WinExe entry point; shell UI
│
├── Nexaflow.Features/
│   ├── Nexaflow.Features.Common/         ← Shared contracts (interfaces, TabEntry, FeatureManager)
│   ├── Nexaflow.Features.Console/        ← PTY terminal tab
│   ├── Nexaflow.Features.Images/         ← Image viewer tab
│   ├── Nexaflow.Features.Markdown/       ← Markdown editor/preview tab
│   ├── Nexaflow.Features.Projects/       ← Project management tabs
│   ├── Nexaflow.Features.Web/            ← HTML/URL viewer tab
│   └── Nexaflow.Features.WinFileSystem/  ← File system browser (file actions)
│
└── Nexaflow.Providers/
    ├── Nexaflow.Providers.Common/        ← Shared provider types (SimpleMessage, PipeFrame)
    ├── Nexaflow.Providers.Aria/          ← Named-pipe client for Aria AI service
    ├── Nexaflow.Providers.Claude/        ← Claude AI provider (unused in shell today)
    └── Nexaflow.Providers.Ollama/        ← Ollama AI provider (unused in shell today)
```

**Dependency rule:** Features depend on `Features.Common` only. `Nexaflow.Core` depends on all features and providers. Features never reference Core — cross-shell communication goes through `FeatureManager`.

---

## Module Responsibilities

### Nexaflow.Core

The shell host. Owns the window chrome, tab strip, ribbon bar, breadcrumb bar, and the AI input row. Nothing in Core knows how to _render_ any individual page — it only knows how to _host_ them.

| File | Responsibility |
|------|----------------|
| `App.xaml.cs` | Startup: registers feature tab factories, shows the first window |
| `MainWindow.xaml.cs` | Wires shell commands; creates `ShellViewModel`; handles ESC/breadcrumb clicks |
| `ViewModels/ShellViewModel.cs` | Tab lifecycle, ribbon lifecycle, notifications, AI routing, error toasts |
| `ViewModels/AiChatViewModel.cs` | Conversation state, message persistence, streaming flag |
| `ViewModels/FileSystemViewModel.cs` | File tree, navigation history, file-action dispatch, ITabOpener impl |
| `Services/RibbonLayoutService.cs` | Serialize/deserialize ribbon items to `%APPDATA%\Aria\Shell\ribbon.json` |
| `Services/WindowManager.cs` | Multi-window registry; tab tearoff; cross-window drag-transfer; DPI maths |
| `Services/ConversationPersistenceService.cs` | Save/load `ConversationRecord` JSON to `%APPDATA%\Aria\Shell\Conversations\` |
| `Controls/RibbonEditor.xaml.cs` | Interactive ribbon customization; local draft, commit on Done |
| `Controls/TabStrip.xaml.cs` | Renders tabs; emits tearoff drag events |
| `Controls/BreadcrumbBar.xaml.cs` | Renders segments; dispatches `TargetPageKind` clicks back to shell |
| `Models/RibbonItem.cs` | Ribbon item state + `TabFactory` delegate + serialization metadata |
| `Models/ConversationRecord.cs` | Chat conversation + message list |
| `Models/PageKinds.cs` | String constants for Core-owned page kinds (`FileSystem`, `AiChat`, `Placeholder`) |

### Nexaflow.Features.Common

The contract layer. Every feature depends on this; nothing else does.

| File | Responsibility |
|------|----------------|
| `TabEntry.cs` | Observable tab state: title, icon, breadcrumbs, `PageFactory` delegate, cached `Page` |
| `BreadcrumbSegment` (same file) | One crumb: label, same-tab `Navigate` action, or cross-tab `TargetPageKind` |
| `ITabRegistration.cs` | Interface features implement to register a page kind with the shell |
| `FeatureManager.cs` | Singleton registry: maps page-kind strings → `ITabRegistration`; raises `TabOpenRequested` event for cross-feature navigation |
| `IRefreshable.cs` | Implemented by page `UserControl`s to support re-click refresh |
| `Services/ITabOpener.cs` | Lets file actions open image/HTML/markdown tabs without referencing Core |
| `Services/IInputPromptService.cs` | Lets file actions show modal overlays without referencing Core |
| `Services/IQueryHandler.cs` | Pages can intercept the shell's AI input bar (e.g. path navigation in FileSystem) |
| `Controls/PieChart.cs` | Lightweight pie chart `FrameworkElement` used by Projects |
| `Converters/Converters.cs` | Standard WPF value converters shared across all features |

### Nexaflow.Features.Console

Hosts a PTY (pseudo-terminal) session as a tab. `ConsoleViewModel` owns a `PseudoConsoleHostService` for the lifetime of the tab. Parses VT sequences, maintains an ANSI screen buffer, exposes command history. The `>` prefix in the AI input bar routes to `ConsoleViewModel.SendCommand`.

### Nexaflow.Features.Projects

Two tabs: **Projects list** and **Project detail**.

`ProjectService` (static facade) points at a root folder (`d:\Projects` by default, persisted to settings.json). `ProjectOperations` does all heavy lifting: load/save `.project` JSON files, manage 9-state backlog items, transactional file editing (for AI use), anchor-based replacement, and `.aisummary` generation. Both `ProjectsViewModel` and `ProjectDetailViewModel` call `ProjectService.Ops` directly.

`ProjectsTabRegistration` and `ProjectDetailTabRegistration` wire `vm.OpenProjectRequested` / `vm.OpenFilesRequested` events to `FeatureManager.RequestTab(...)` so no view model holds a reference to the shell.

### Nexaflow.Features.Images / .Markdown / .Web

Single-purpose viewer/editor tabs. Each has one ViewModel owning a file path and a simple command set. These are opened by `FileSystemViewModel`'s `ITabOpener` implementation in Core (specifically `FileSystemViewModel`'s nested `TabOpenerAdapter` class), which calls `TabOpenRequested` to ask the shell to create the tab.

### Nexaflow.Providers.Aria

Two-layer transport:

- `AriaNamedPipeClient` — raw JSON framing over a Windows named pipe (`aria-comms`). Extracts Windows identity (tries UPN email, falls back to `DOMAIN\user`). Events: `MessageReceived`, `IsTyping`, `Disconnected`.
- `AriaClientService` — wraps the client; provides one-shot request/reply via `TaskCompletionSource`. Parses `#focustab TabName params` instructions from response text and strips them before returning. Throws `AriaConnectionException` on pipe failure.

---

## Key Data Models

```
TabEntry
  ├── Title, Icon, IsActive           (observable — drives tab strip UI)
  ├── Breadcrumbs: List<BreadcrumbSegment>
  ├── PageFactory: Func<UserControl>  (set by factory; lazy-created)
  └── Page: UserControl               (cached after first activation)

BreadcrumbSegment
  ├── Label
  ├── Navigate: Action?               (same-tab navigation)
  ├── TargetPageKind: string?         (cross-tab: shell opens/focuses that page kind)
  └── TargetPageParams: Dict?

RibbonItem
  ├── Kind: Button | HalfGroup | Separator
  ├── Label, Icon, IsHalf, AccentColor
  ├── PageKind: string?               (persisted to ribbon.json)
  ├── PageParams: Dict?               (persisted alongside PageKind)
  └── TabFactory: Func<TabEntry>?     (runtime only — re-attached on load via FeatureManager)

ConversationRecord
  ├── Id: Guid
  ├── Title: string                   (derived from first exchange)
  ├── StartedAt: DateTime
  └── Messages: List<ConversationMessage>
        └── { Id, Text, IsUser, Timestamp }
```

---

## Extensibility Points

### `ITabRegistration` — Adding a new page kind

Implement in your feature assembly and register in `App.xaml.cs`.

```csharp
// In your feature project
public sealed class MyTabRegistration : ITabRegistration
{
    public string PageKind => "MyFeature";

    public TabEntry CreateTab(Dictionary<string, string>? pageParams = null)
    {
        var vm  = new MyViewModel();
        var tab = new TabEntry { Title = "My Feature", Icon = "🔧",
            Breadcrumbs = [new BreadcrumbSegment { Label = "My Feature" }] };
        tab.PageFactory = () => new MyView(vm);
        return tab;
    }
}

// In App.xaml.cs → RegisterFeatures()
fm.Register(new MyTabRegistration());
```

The shell's `ReattachTabFactory`, `OpenTabForPageKind`, and `HandleFocusTabInstruction` all delegate to `FeatureManager.Instance` for any page kind not handled by the Core `switch`. The page kind string also becomes available in the ribbon editor automatically — users can add a ribbon button that persists `PageKind = "MyFeature"` and the factory is re-attached on restart.

### `IRefreshable` — Re-click refresh

Implement on your page `UserControl`. `ShellViewModel.ActivateTab` calls `Refresh()` when the already-active tab is clicked.

```csharp
public partial class MyView : UserControl, IRefreshable
{
    public void Refresh() => ViewModel.ReloadCommand.Execute(null);
}
```

### `IFileAction` — Adding a file context action

Implement in `Nexaflow.Features.WinFileSystem.FileActions`. The interface is large — key properties that control when your action appears:

| Property | Purpose |
|----------|---------|
| `SupportedFileTypes` | Comma-separated extensions (e.g. `".cs,.vb"`) or `"*"` for all |
| `AppliesToFolders` | Show when a folder is selected |
| `AppliesToRoot` / `AppliesToDrives` | Show at the This PC / drive level |
| `IsDestructive` | Prompts confirmation before execute |
| `RequiresRefresh` | Triggers tree refresh after execute |

Actions receive `IInputPromptService` (for modal input/confirm overlays) and `ITabOpener` (to open a tab) via constructor — no direct UI or shell dependency.

### `ITabOpener` — Opening media/viewer tabs from a file action

Implemented by `FileSystemViewModel` in Core and injected into each `IFileAction`.

```csharp
void OpenImageViewer(IReadOnlyList<string> imagePaths);
void OpenHtmlViewer(string filePath);
void OpenMarkdownViewer(string filePath);
```

### `IQueryHandler` — Intercepting the AI input bar

If your page wants to handle free-text input before it reaches Aria, implement this on your `UserControl` or `ViewModel` and check for it in `ShellViewModel.SendAiMessage`. Currently only `FileSystemViewModel` uses this (handles path strings like `C:\Users\...` for direct navigation). There is no automatic discovery — you must add an `is IQueryHandler` cast in `SendAiMessage` manually.

### `FeatureManager.TabOpenRequested` — Cross-feature navigation

When a feature view model needs to open another tab (e.g. Projects opening a Project Detail), it calls:

```csharp
FeatureManager.Instance.RequestTab("ProjectDetail", new() { ["folder"] = folderName });
```

`ShellViewModel` subscribes to this event in its constructor and calls `OpenTabForPageKind`. Features never hold a reference to the shell.

---

## Core Flows

### Startup

```
App.OnStartup
  → RegisterFeatures()           registers ITabRegistration instances with FeatureManager
  → new MainWindow()
      → new ShellViewModel()
          → FeatureManager.TabOpenRequested += OnFeatureTabOpenRequested
          → LoadOrBuildRibbon()
              → RibbonLayoutService.Load()
              → foreach item: ReattachTabFactory(item)   ← uses FeatureManager
      → MainWindow opens default tabs (AI Chat + Dashboard)
```

### Opening a tab via ribbon click

```
User clicks ribbon button
  → ShellViewModel.RibbonAction(item)
  → item.TabFactory()                    ← Func<TabEntry> set by ReattachTabFactory
  → ShellViewModel.OpenTab(tab)
  → tab.GetOrCreatePage()                ← PageFactory invoked on first activation
  → CurrentPage = page, breadcrumbs updated
```

### AI input bar

```
User types text and submits
  → ShellViewModel.SendAiMessage()
  → if starts with '>': route to GetOrCreateConsoleVm().SendCommand()
  → if current page is FileSystemView: TryHandleInputAsync(text)
  → else: AriaClientService.SendAsync(text)
      → AriaNamedPipeClient sends PipeFrame to aria-comms pipe
      → waits for MessageReceived (60 s timeout)
      → parses #focustab instruction from response
  → if FocusTab: HandleFocusTabInstruction → FeatureManager.CreateTab / OpenTab
  → else: GetOrCreateChatVm().AddExchangeAsync(userText, responseText)
```

### Cross-feature tab request (e.g. Projects → Project Detail)

```
ProjectsViewModel.OpenProject()
  → vm.OpenProjectRequested?.Invoke(folderName)
  → ProjectsTabRegistration lambda:
      FeatureManager.Instance.RequestTab("ProjectDetail", {folder: folderName})
  → FeatureManager.TabOpenRequested event fires
  → ShellViewModel.OnFeatureTabOpenRequested (on Dispatcher)
  → OpenTabForPageKind("ProjectDetail", params)
  → FeatureManager.Instance.CreateTab("ProjectDetail", params)
  → ProjectDetailTabRegistration.CreateTab(params)
  → ShellViewModel.OpenTab(tab)
```

### Ribbon persistence cycle

```
Save:  RibbonItems changes → CollectionChanged → RibbonLayoutService.Save()
         TabFactory delegates are not serialized (they're runtime lambdas)

Load:  App restart → RibbonLayoutService.Load() → items with null TabFactory
         → foreach: ShellViewModel.ReattachTabFactory(item)
             PageKinds.FileSystem / AiChat → Core factory methods
             any FeatureManager.IsRegistered key → FeatureManager.CreateTab lambda
             unknown → MakePlaceholderTab
```

---

## Separation-of-Concerns Issues

These are real architectural problems, ordered roughly by impact.

---

### 1. `ShellViewModel` is a god object

**Where:** `Nexaflow.Core/ViewModels/ShellViewModel.cs`

**Problem:** One class owns: tab lifecycle, ribbon lifecycle (load/save/build/pin), notification management, AI routing, error toasts, background tasks, and breadcrumb management. It also holds a live `AriaClientService` instance and directly constructs `AiChatViewModel`, `AiChatPage`, `FileSystemViewModel`, `ConsoleViewModel`, and `ConsoleView`.

**Impact:** Any change to AI routing, ribbon persistence, or tab management touches the same file. It can't be unit-tested without constructing the entire shell.

**Suggested split:**

- `TabManager` — `OpenTab`, `ActivateTab`, `CloseTab`, `ReceiveTab`, `RemoveTab`, breadcrumb sync
- `RibbonManager` — load/save/build/pin/reattach factories
- `NotificationService` — `ShowError`, `AddNotification`, `ToggleNotifications`
- `AiInputRouter` — `SendAiMessage`, `HandleFocusTabInstruction`, `GetOrCreateChatVm`
- `ShellViewModel` becomes a thin coordinator wiring those together



---

### 4. `ITabOpener` has a fixed set of viewer types

**Where:** `Nexaflow.Features.Common/Services/ITabOpener.cs`, implemented in `FileSystemViewModel`

**Problem:** `ITabOpener` only knows about three viewer tabs (image, HTML, markdown). Adding a new viewer requires changing the interface and its implementation in Core. This means Core must know about every viewer feature.

**Impact:** The interface also has a tight implementation in `FileSystemViewModel`'s nested `TabOpenerAdapter` class, which calls `TabOpenRequested` with a fully-constructed `TabEntry`. Adding a fourth viewer means touching `ITabOpener`, `TabOpenerAdapter`, and `FileSystemViewModel`.

**Suggested fix:** Replace the typed methods with a generic `void OpenTab(string pageKind, Dictionary<string, string> pageParams)` that delegates to `FeatureManager.RequestTab`. File actions then just need to know the page-kind string.

---

### 5. `WindowManager` has hardcoded layout constants

**Where:** `Nexaflow.Core/Services/WindowManager.cs`

**Problem:** Tab tearoff position calculation uses `const double TopBarHeight = 72`, `TabBarHeight = 38`, `TabStripColumnFraction = 0.45`, `DefaultWindowWidth = 1280`, `DefaultWindowHeight = 780`. These duplicate values from `MainWindow.xaml`.

**Impact:** If the shell layout changes (e.g. ribbon height changes, tab bar moves), the tearoff window positions will be wrong in ways that won't produce compiler errors.

**Fix:** Expose these as static properties on `MainWindow` or read them via `ActualHeight` / `TransformToVisual` at runtime rather than embedding them as constants.

---

### 6. `ProjectOperations` is too wide

**Where:** `Nexaflow.Features.Projects/Model/ProjectOperations.cs`

**Problem:** One class handles: project metadata (name, description, scope, objectives), backlog item CRUD, a 9-state backlog workflow, transactional file editing, anchor-based text replacement, `.aisummary` file management, and file structure enumeration. It's 527 lines and implements the `IProjectTools` interface that MCP also depends on.

**Impact:** The backlog workflow is mixed with file I/O concerns. Testing one aspect requires constructing the full service.

**Suggested split:**

- `ProjectMetadataService` — header, scope, objectives
- `BacklogService` — backlog CRUD and state machine
- `ProjectFileService` — file reads, directory tree, transactional writes, anchors
- `ProjectAiService` — `.aisummary` generation and management
- `ProjectOperations` becomes a facade delegating to the above

---

### 7. `ProjectService` uses a static singleton

**Where:** `Nexaflow.Features.Projects/Services/ProjectService.cs`

**Problem:** `ProjectService.Ops` is a static property that both `ProjectsViewModel` and `ProjectDetailViewModel` call directly. There is no way to inject an alternative implementation (e.g. a mock, a remote source, a different root path) without modifying the static.

**Impact:** ViewModels are not testable in isolation.

**Fix:** Inject `IProjectOperations` (or sub-services from item 6) into the ViewModel constructors. The static `ProjectService` can remain as a convenience composition root for production, but ViewModels should receive the interface.

---

### 8. `AriaClientService` is never disposed

**Where:** `ShellViewModel._ariaClient`

**Problem:** `AriaClientService` wraps `AriaNamedPipeClient` (which holds an open named-pipe handle) and is created as a field in `ShellViewModel`. There is no call to `DisposeAsync` when the window closes. The pipe handle leaks until the process exits.

**Fix:** Implement `IAsyncDisposable` on `ShellViewModel` (or a wrapper service) and call `await _ariaClient.DisposeAsync()` from `MainWindow.Closed`.

---

### 9. Ribbon default item list references feature page-kind strings as literals

**Where:** `ShellViewModel.BuildDefaultItems()`

**Problem:**

```csharp
MakeButton("Projects", "🗂", "Projects"),  // string literal "Projects"
MakeButton("Console",  "⌨", "Console"),   // string literal "Console"
```

These literals must match `ConsoleTabRegistration.PageKind` and `ProjectsTabRegistration.PageKind` exactly. There is no compile-time check. If a feature renames its page kind, the ribbon default silently becomes a placeholder.

**Fix:** Each `ITabRegistration` could expose a `DefaultRibbonItem` property returning a pre-built `RibbonItem`, and `BuildDefaultItems` could call `fm.GetRegistration("Console").DefaultRibbonItem`. Alternatively, expose a `public const string Kind` on each registration class and reference that constant in `BuildDefaultItems`.

---

### 10. `RibbonEditor` mixes model logic with view construction

**Where:** `Nexaflow.Core/Controls/RibbonEditor.xaml.cs` (~710 lines)

**Problem:** The editor builds its card UI entirely in code-behind using procedural `Border`/`StackPanel`/`TextBlock` construction. The drag-reorder logic, colour swatch lookup, icon grid population, and draft-clone logic are all in the same class alongside visual tree manipulation.

**Impact:** Hard to reason about, hard to change. The editor directly references `ShellViewModel` to call `BuildDefaultItems()` and `ReattachTabFactory()`, coupling a UI control to a view model.

**Fix:** Move the draft list + reorder/clone logic into a `RibbonEditorViewModel`. The control should only perform visual tree operations. The `ShellViewModel` dependency can be replaced with an `IDefaultRibbonProvider` interface.