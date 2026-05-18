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
├── Nexaflow.Core/                              ← WinExe entry point; shell UI
│
├── Nexaflow.Features/
│   ├── Nexaflow.Features.Common/              ← Shared contracts (interfaces, TabEntry, FeatureManager)
│   ├── Nexaflow.Features.AIChat/              ← AI conversation tab
│   ├── Nexaflow.Features.Console/             ← PTY terminal tab
│   ├── Nexaflow.Features.Images/              ← Image viewer tab
│   ├── Nexaflow.Features.Logs/                ← Log viewer tab
│   ├── Nexaflow.Features.Markdown/            ← Markdown editor/preview tab
│   ├── Nexaflow.Features.Projects/            ← Project management tabs
│   ├── Nexaflow.Features.Scratchpad/          ← Virtual corkboard tab
│   ├── Nexaflow.Features.Text/                ← Text editor tab
│   ├── Nexaflow.Features.Web/                 ← HTML/URL viewer tab
│   └── Nexaflow.Features.WindowsSearch/       ← Windows Search integration tab
│
└── Nexaflow.Providers/
    ├── Nexaflow.Providers.Common/             ← LlmProviderRegistry, shared message types
    ├── Nexaflow.Providers.Aria/               ← Named-pipe client for Aria AI service
    ├── Nexaflow.Providers.Claude/             ← Claude API provider
    └── Nexaflow.Providers.Ollama/             ← Ollama local model provider
```

**Dependency rule:** Features depend on `Features.Common` only — never on Core or each other. `Nexaflow.Core` depends on all features and providers (for registration in `App.xaml.cs`) but never instantiates feature view or view-model types in its hot paths — all tab creation goes through `FeatureManager`. Features communicate back to the shell exclusively through `IShellServices`.

---

## Module Responsibilities

### Nexaflow.Core

The shell host. Owns the window chrome, tab strip, ribbon bar, breadcrumb bar, and the AI input row. Nothing in Core knows how to _render_ any individual page — it only knows how to _host_ them.

| File | Responsibility |
|------|----------------|
| `App.xaml.cs` | Startup: provider registration, singleton wiring, feature registration, first window |
| `MainWindow.xaml.cs` | Wires shell commands; creates `ShellViewModel`; handles ESC/breadcrumb clicks |
| `ViewModels/ShellViewModel.cs` | Tab lifecycle, ribbon lifecycle, notifications, AI routing, background tasks |
| `Services/ShellServices.cs` | `IShellServices` implementation — global tab registry, multi-window coordination |
| `Services/RibbonLayoutService.cs` | Serialize/deserialize ribbon items to `%APPDATA%\Smile\Nexaflow\ribbon.json` |
| `Services/WindowManager.cs` | Multi-window registry; tab tearoff; cross-window drag-transfer; DPI maths |
| `Services/ConversationPersistenceService.cs` | Save/load `ConversationRecord` JSON to `%APPDATA%\Smile\Nexaflow\Conversations\` |
| `FileActions/` | Core-owned file actions: copy, delete, rename, and other system-level operations |
| `Controls/RibbonEditor.xaml.cs` | Interactive ribbon customization; local draft, commit on Done |
| `Controls/TabStrip.xaml.cs` | Renders tabs; emits tearoff drag events |
| `Controls/BreadcrumbBar.xaml.cs` | Renders segments; dispatches `TargetPageKind` clicks back to shell |
| `Models/RibbonItem.cs` | Ribbon item state + `TabFactory` delegate + serialization metadata |
| `Models/PageKinds.cs` | String constants for Core-owned page kinds (`FileSystem`, `Placeholder`) |

### Nexaflow.Features.Common

The contract layer. Every feature depends on this; nothing else does.

| File | Responsibility |
|------|----------------|
| `TabEntry.cs` | Observable tab state: title, icon, breadcrumbs, `PageFactory` delegate, cached `Page` |
| `BreadcrumbSegment` (same file) | One crumb: label, drop-down children, same-tab `Navigate` action, or cross-tab `TargetPageKind` |
| `ITabRegistration.cs` | Interface features implement to register a page kind with the shell |
| `IPageView.cs` | Thin shell-lifecycle contract implemented by tab `UserControl`s: exposes `IPageViewModel? ViewModel` and `Reinitialize` |
| `IPageViewModel.cs` | AI pipeline contract implemented by ViewModels: `GetContext`, `GetAvailableActions`, `GetContextObject`, `Execute` |
| `IShellServices.cs` | Application-level singleton injected into feature code for tab management |
| `IAIService.cs` | AI disambiguation and contextual chat, injected as a singleton service |
| `IFeatureConfig.cs` | Marks a POCO as a config section; discovered and instantiated by `FeatureManager` |
| `IContext.cs` / `FileSystemContext.cs` | Typed context objects offered by ViewModels via `IPageViewModel.GetContextObject()` |
| `IActionExecutor.cs` | Implemented by ViewModels to receive and execute AI-generated JSON action payloads |
| `ConfigAttributes.cs` | `[ConfigDisplayName]`, `[FolderPath]`, `[ListSource]`, `[CustomControl]` / `ICustomConfigApply` |
| `AiResponse.cs` | `AiResponse` record and `AiResponseKind` enum returned by `IAIService.ContextChat` |
| `ConversationRecord.cs` | Chat conversation + message list |
| `Services/IQueryHandler.cs` | Intercepts the AI input bar; registered globally or inferred from the active page's ViewModel |
| `Services/IInputPromptService.cs` | Lets file actions show modal overlays without referencing Core |
| `FileActions/IFileAction.cs` | Interface for file-selection context actions |
| `FileActions/IFolderAction.cs` | Interface for folder-selection context actions |
| `FileActions/IFileCreateAction.cs` | Interface for "new file" context actions |
| `IKeyboardHandler.cs` | Claims global keyboard shortcuts; queried against the active page |
| `IDropTarget.cs` | Accepts file drag-drop; resolved from the active page by the file browser |

### Feature assemblies

Each feature assembly follows the same internal structure:

- One or more `*TabRegistration : ITabRegistration` — factory entry points, injected with `IShellServices` and any `IFeatureConfig` the assembly declares
- `ViewModels/` — MVVM Toolkit `ObservableObject` view models; never reference Core
- `Views/` — WPF `UserControl`s; implement `IPageView` to expose context to the AI pipeline
- Optional: `IQueryHandler` implementations registered globally or resolved from the active ViewModel
- Optional: `IFileAction` / `IFolderAction` implementations for file browser context menus
- Optional: `IFeatureConfig` POCO for persisted settings (auto-discovered by `FeatureManager`)

### Nexaflow.Providers.*

All providers implement `ILlmProvider` (and optionally `IAsyncDisposable`) from `Providers.Common`. Core registers them with `ProviderManager` at startup; the shell never references provider types directly — it goes through `LlmProviderRegistry` to resolve the configured basic/conversation provider.

| Assembly | Backing service |
|----------|----------------|
| `Nexaflow.Providers.Aria` | Windows named-pipe to the local Aria service. Two layers: `AriaNamedPipeClient` (raw JSON framing) and `AriaClientService` (one-shot request/reply, parses `#focustab` instructions from responses). |
| `Nexaflow.Providers.Claude` | Anthropic Claude API over HTTPS. |
| `Nexaflow.Providers.Ollama` | Ollama REST API for locally-hosted models. |

---

## Key Data Models

```
TabEntry
  ├── Title, Icon, IsActive           (observable — drives tab strip UI)
  ├── PageKind: string?               (e.g. "Search", "Projects")
  ├── PageParams: Dict?               (current params, kept in sync by the page via IShellServices)
  ├── Breadcrumbs: List<BreadcrumbSegment>
  ├── PageFactory: Func<UserControl>  (set by ITabRegistration; lazy-created on first activation)
  └── Page: UserControl               (cached after GetOrCreatePage() first call)

BreadcrumbSegment
  ├── Label
  ├── Children: List<string>          (drop-down picker items)
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
  ├── Title: string
  ├── StartedAt: DateTime
  └── Messages: List<ConversationMessage>
        └── { Id, Text, IsUser, Timestamp }
```

---

## Extensibility Points

### `ITabRegistration` — Adding a new page kind

Implement in your feature assembly. `FeatureManager` discovers it during `Register()` and injects constructor dependencies automatically: any `IFeatureConfig` declared in the same assembly, `IShellServices`, and any singleton registered via `RegisterSingletonService()`.

```csharp
public sealed class MyTabRegistration(MyConfig config, IShellServices shellServices) : ITabRegistration
{
    public string PageKind => "MyFeature";

    public TabEntry CreateTab(Dictionary<string, string>? pageParams = null)
    {
        var vm = new MyViewModel(config, shellServices);
        var tab = new TabEntry
        {
            Title       = "My Feature",
            Icon        = "🔧",
            PageParams  = pageParams,
            Breadcrumbs = [new BreadcrumbSegment { Label = "My Feature" }]
        };
        tab.PageFactory = () => new MyView(vm);
        return tab;
    }
}
```

Register in `App.xaml.cs → RegisterFeatures()`:

```csharp
fm.Register(typeof(MyTabRegistration));
```

The page kind string is then available in the ribbon editor automatically.

---

### `IPageView` / `IPageViewModel` — Exposing context to the AI pipeline

`IPageView` is the shell's typed handle to a tab `UserControl` — implement it on your `UserControl`. It exposes the ViewModel and handles shell lifecycle. `Reinitialize` is called on first load and whenever the shell activates the tab with a new param set (including re-clicking the active tab).

`IPageViewModel` is the AI pipeline contract — implement it on your ViewModel so the shell can query context, enumerate available actions, and execute AI-selected actions.

```csharp
// The View — thin shell-lifecycle wrapper
public partial class MyView : UserControl, IPageView
{
    private readonly MyViewModel _vm;
    public MyView(MyViewModel vm) { _vm = vm; DataContext = vm; InitializeComponent(); }

    public IPageViewModel? ViewModel => _vm;
    public void Reinitialize(Dictionary<string, string> pageParams) { /* react to new params */ }
}

// The ViewModel — owns all AI pipeline logic
public partial class MyViewModel : ObservableObject, IPageViewModel
{
    public string GetContext() => "User is viewing My Feature";
    public IReadOnlyList<ActionDescriptor> GetAvailableActions() => [];
}
```

---

### `IQueryHandler` — Intercepting the AI input bar

Implement to handle typed input before it reaches the LLM. Either register globally at startup or implement on a page ViewModel (the shell checks `page?.ViewModel as IQueryHandler` for tab-scoped handling).

```csharp
public sealed class MyQueryHandler : IQueryHandler
{
    public string Description => "Handles my feature's input";
    public string? Symbol => null;                           // optional prefix character

    public float CanProcess(string input, IPageViewModel? pageVm = null)
        => pageVm is MyViewModel ? 0.9f : 0f;

    public async Task<string?> ProcessAsync(string input, IPageViewModel? pageVm = null)
    {
        if (pageVm is not MyViewModel vm) return "No active tab.";
        await vm.DoSomethingAsync(input);
        return null;   // null = handled silently; string = shown in AI Chat
    }
}
```

Global registration in `App.xaml.cs` (after `RegisterFeatures()`):

```csharp
FeatureManager.Instance.RegisterQueryHandler(new MyQueryHandler());
```

---

### `IFileAction` / `IFolderAction` / `IFileCreateAction` — File browser context actions

Implement in the assembly that owns the concern. `FileActionManager.Discover()` scans Core and all registered feature types for implementations. Actions that open a viewer tab belong in the feature that owns the viewer; system-level actions (copy, rename…) belong in `Nexaflow.Core.FileActions`.

Actions are matched to files via `ExperienceId` — a hierarchical path (e.g. `"/image"`, `"/binary/installer"`) that `FileMapManager` resolves against the selected file. Constructor dependencies (`IShellServices`, `IInputPromptService`) are injected by `FeatureManager`.

Key `IFileAction` members:

| Member | Purpose |
|--------|---------|
| `ExperienceId` | Hierarchical type path, matched by `FileMapManager` |
| `ExperienceDescription` | Human-readable, shown in the File Type Actions options panel |
| `DisplayName` / `Icon` | Button label and glyph |
| `IsDestructive` | Prompts confirmation before execute |
| `RequiresRefresh` | Triggers file tree refresh after execute |
| `SupportsMultipleFiles` | Whether the action can receive a collection |
| `CanPerformAction` | Computed gate (e.g. checks selection state) |
| `PerformAction(string)` / `PerformAction(IEnumerable<string>)` | Execute the action |

---

### `IShellServices` — Calling back to the shell

Injected into `ITabRegistration` and `IFileAction` constructors. Provides the only approved path for features to interact with the tab strip.

```csharp
shellServices.OpenTab("ProjectDetail", new() { ["folder"] = folder }, callerPage);
shellServices.CloseTab(tab);
shellServices.UpdateTabMeta(tab, title: "New Title", breadcrumbs: [...], pageParams: newParams);
shellServices.FindTab("Search", new() { ["root"] = root });
shellServices.ShowError("Something went wrong");
shellServices.ShowNotification("Done");
```

---

### `IFeatureConfig` — Persisted settings

Implement a plain POCO. `FeatureManager.Register()` discovers it, instantiates it, and makes it available for injection into `ITabRegistration` constructors. The Options panel renders a property grid for free using reflection and the config attributes.

```csharp
public sealed class MyConfig : IFeatureConfig
{
    public string ConfigName   => "myfeature";
    public string FriendlyName => "My Feature";

    [ConfigDisplayName("Root Folder")]
    [FolderPath]
    public string RootFolder { get; set; } = string.Empty;

    [ConfigDisplayName("Provider")]
    [ListSource(typeof(LlmProviderRegistry), nameof(LlmProviderRegistry.GetProviderNames))]
    public string Provider { get; set; } = string.Empty;
}
```

For a fully custom options UI, apply `[CustomControl(typeof(MyOptionsControl))]` to the config class and implement `ICustomConfigApply` on the control.

---

### `IKeyboardHandler` — Global keyboard shortcuts

Implement on your `UserControl` or ViewModel. The shell calls `CanProcessKey` before consuming the event.

```csharp
public bool CanProcessKey(Key key, ModifierKeys modifiers)
    => modifiers == ModifierKeys.Control && key == Key.OemPlus;

public bool ProcessKey(Key key, ModifierKeys modifiers)
{
    ZoomIn();
    return true;
}
```

---

### `IDropTarget` — File drag-drop

Implement on your `UserControl`. The file browser resolves this from the active page for drop operations.

---

### `IActionExecutor` — AI-generated action payloads

Implement on your ViewModel when you want the AI to drive actions beyond the `ActionDescriptor` system. Called by `IAIService.ContextChat` when the LLM returns a JSON action payload and no `IQueryHandler` claimed the input.

---

## Core Flows

### Startup

```
App.OnStartup
  → new BackgroundActivityManager()
  → ProviderManager.Register(Aria, Ollama, Claude providers)
  → ConfigManager.Register(ShellConfig, FileMapConfig)
  → FileMapManager.Initialize()
  → new AIService() → FeatureManager.RegisterSingletonService(IAIService, ...)
  → new ShellServices() → FeatureManager.SetShellServices(shellServices)
  → RegisterFeatures()
      → fm.Register(typeof(ConsoleTabRegistration))
      → fm.Register(typeof(ProjectsTabRegistration))
      → ... (one call per feature assembly)
      Each Register():
        - discovers IFeatureConfig implementations → instantiates → ConfigManager.Register
        - discovers ITabRegistration implementations → resolves constructor deps → instantiates
        - collects IFileAction / IFolderAction / IFileCreateAction / IKeyboardHandler / IDropTarget types
        - discovers IQueryHandler implementations → instantiates resolvable ones globally
  → new MainWindow(activityManager, aiService, shellServices)
      → new ShellViewModel()
          → LoadOrBuildRibbon() → RibbonLayoutService.Load() → ReattachTabFactory each item
          → OpenDefaultTabs()
  → win.Show()
```

### Opening a tab

```
User clicks ribbon button
  → ShellViewModel.RibbonAction(item)
  → item.TabFactory()                       ← Func<TabEntry> set by ReattachTabFactory
  → ShellViewModel.OpenTab(tab)
  → tab.GetOrCreatePage()                   ← PageFactory invoked on first activation
  → CurrentPage = page, breadcrumbs updated

Feature code calls shellServices.OpenTab("PageKind", params, callerPage)
  → ShellServices resolves target window from callerPage / focused window
  → Searches globally for existing matching tab
  → If found: move to target window if needed → Reinitialize(params) → activate
  → If not found: FeatureManager.CreateTab(pageKind, params)
      → matching ITabRegistration.CreateTab(params)
  → IWindowHost.AddTab(tab) → prepends, sets active, loads page
```

### AI input bar

```
User submits text in AI input bar
  → ShellViewModel.SendAiMessage(input)
  → Check registered IQueryHandler list (Symbol prefix match first, then CanProcess scores)
  → If Symbol match: route directly to that handler
  → If multiple candidates score > 0: IAIService.DisambiguateToolSelection() picks one
  → If single candidate: ProcessAsync(input, currentPageVm)
  → If no handler: IAIService.ContextChat(currentPageVm, input)
      → LLM decides: execute ActionDescriptor, prefill input, or reply conversationally
  → Response text (if any) added to AI Chat conversation
```

### Cross-feature tab navigation

```
Feature ViewModel (e.g. ProjectsViewModel) calls:
  _shellServices.OpenTab("ProjectDetail", { "folder": folderName }, callerPageView)

ShellServices:
  → resolves target window from callerPageView
  → FindTab("ProjectDetail", params) — check if already open
  → FeatureManager.CreateTab("ProjectDetail", params) if not found
      → ProjectDetailTabRegistration.CreateTab(params)
  → IWindowHost.AddTab(tab)
```

### Opening a viewer tab from a file action

```
User selects file in file browser → clicks action button
  → FileActionManager.PerformAction(action, paths)
  → e.g. ShowMarkdownAction.PerformAction(path)
      _shellServices.OpenTab("Markdown", { "path": filePath })
  → ShellServices → MarkdownTabRegistration.CreateTab(params) → tab opens
```

### Ribbon persistence cycle

```
Save:  RibbonItems changes → CollectionChanged → RibbonLayoutService.Save()
         TabFactory delegates not serialized (runtime lambdas)

Load:  App restart → RibbonLayoutService.Load() → items with null TabFactory
         → ReattachTabFactory(item):
             PageKinds.FileSystem → Core factory
             FeatureManager.IsRegistered key → FeatureManager.CreateTab lambda
             unknown → MakePlaceholderTab
```

---

## Separation-of-Concerns Issues

Ordered by impact.

---

### 1. `ShellViewModel` is a god object

**Where:** `Nexaflow.Core/ViewModels/ShellViewModel.cs`

**Problem:** One class owns tab lifecycle, ribbon lifecycle (load/save/build/pin), notification management, AI routing, background tasks, and breadcrumb management.

**Impact:** Any change to AI routing, ribbon persistence, or tab management touches the same file.

**Suggested split:**
- `TabManager` — `OpenTab`, `ActivateTab`, `CloseTab`, `ReceiveTab`
- `RibbonManager` — load/save/build/pin/reattach factories
- `NotificationService` — `ShowError`, `AddNotification`, `ToggleNotifications`
- `AiInputRouter` — `SendAiMessage`, handler selection, `GetOrCreateChatVm`

---

### 2. `WindowManager` has hardcoded layout constants

**Where:** `Nexaflow.Core/Services/WindowManager.cs`

**Problem:** Tab tearoff position calculation uses `const double TopBarHeight = 72`, `TabBarHeight = 38`, etc. These duplicate values from `MainWindow.xaml`.

**Fix:** Expose these as static properties on `MainWindow` or read them via `ActualHeight` / `TransformToVisual` at runtime.

---

### 3. `ProjectOperations` is too wide

**Where:** `Nexaflow.Features.Projects/Model/ProjectOperations.cs`

**Problem:** Handles project metadata, backlog CRUD, 9-state workflow, transactional file editing, anchor-based text replacement, and `.aisummary` management in one class.

**Suggested split:** `ProjectMetadataService`, `BacklogService`, `ProjectFileService`, `ProjectAiService` — with `ProjectOperations` as a facade.

---

### 4. `ProjectService` uses a static singleton

**Where:** `Nexaflow.Features.Projects/Services/ProjectService.cs`

**Problem:** `ProjectService.Ops` is accessed directly by both ViewModels. No injection point for testing.

**Fix:** Inject `IProjectOperations` into ViewModel constructors.

---

### 5. Ribbon default items reference page-kind strings as literals

**Where:** `ShellViewModel.BuildDefaultItems()`

**Problem:** String literals must match `ITabRegistration.PageKind` exactly. No compile-time check.

**Fix:** Expose `public const string Kind` on each registration class and reference the constant in `BuildDefaultItems`.

---

### 6. `RibbonEditor` mixes model logic with view construction

**Where:** `Nexaflow.Core/Controls/RibbonEditor.xaml.cs`

**Problem:** ~710 lines of procedural `Border`/`StackPanel`/`TextBlock` construction. Drag-reorder logic, colour swatches, and draft-clone logic share the file. Direct `ShellViewModel` reference.

**Fix:** Extract `RibbonEditorViewModel`; replace `ShellViewModel` dependency with `IDefaultRibbonProvider`.
