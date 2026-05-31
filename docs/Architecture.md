# Nexaflow Architecture

> **Freshness:** reviewed/refreshed 2026-05-31, including the per-`WorkContext` provider/`AIService`
> model — see [Ownership & Lifetime](#ownership--lifetime) for what is central vs context-scoped.
> Architectural cleanup opportunities are tracked in [arch_improvements.md](arch_improvements.md).
>
> **Renamed since earlier drafts** (old → new): `ITabRegistration` → `IPageRegistration`
> (`CreateTab` → `CreatePage`), `TabEntry` → `Page` (`PageFactory` → `ContentFactory`,
> cached `Page` → `Content`). Removed: `IInputPromptService` (use `IShellServices.ShowPrompt`/
> `ShowConfirmation`), `IShellServices.UpdateTabMeta`. `FeatureManager` lives in **Core**, not Common.

## Table of Contents

1. [Solution Layout](#solution-layout)
2. [Ownership & Lifetime](#ownership--lifetime)
3. [Module Responsibilities](#module-responsibilities)
4. [Key Data Models](#key-data-models)
5. [Extensibility Points](#extensibility-points)
6. [Core Flows](#core-flows)
7. [Separation-of-Concerns Issues](#separation-of-concerns-issues)

---

## Solution Layout

```
nexaflow/src/
├── Nexaflow.Core/                              ← WinExe entry point; shell UI; FeatureManager
│
├── Nexaflow.Features/
│   ├── Nexaflow.Features.Common/              ← Shared contracts (interfaces + small DTOs). FeatureManager is NOT here — it's in Core.
│   ├── Nexaflow.Features.AIChat/              ← AI conversation tab (browser over saved conversations)
│   ├── Nexaflow.Features.Console/             ← PTY terminal tab
│   ├── Nexaflow.Features.Git/                 ← Git interface tab
│   ├── Nexaflow.Features.Hex/                 ← Binary / hex viewer tab
│   ├── Nexaflow.Features.Images/              ← Image viewer tab
│   ├── Nexaflow.Features.Json/                ← JSON viewer tab (seek-by-item windowing)
│   ├── Nexaflow.Features.Logs/                ← Log viewer tab (tail-first streaming)
│   ├── Nexaflow.Features.Markdown/            ← Markdown editor/preview tab
│   ├── Nexaflow.Features.Projects/            ← Project management tabs
│   ├── Nexaflow.Features.Scratchpad/          ← Virtual corkboard tab
│   ├── Nexaflow.Features.Tabular/             ← CSV/TSV/fixed-width viewer tab (shape detection + transforms)
│   ├── Nexaflow.Features.Text/                ← Text editor tab (head-first windowing)
│   ├── Nexaflow.Features.Web/                 ← HTML/URL viewer tab
│   ├── Nexaflow.Features.WindowsFileSystem/   ← File explorer tab (DirectoryTree + file list)
│   └── Nexaflow.Features.WindowsSearch/       ← Windows Search integration tab
│
├── Nexaflow.Visuals.Common/                    ← Shared WPF controls + value converters (PieChart, BoolToVisibility, …)
├── Nexaflow.Visuals.Text/                      ← Shared markdown rendering (MarkdownView / SelectableMarkdownView / MarkdownFlowDocument)
│
└── Nexaflow.Providers/
    ├── Nexaflow.Providers.Common/             ← LlmProviderRegistry, shared message types
    ├── Nexaflow.Providers.Aria/               ← Named-pipe client for Aria AI service
    ├── Nexaflow.Providers.Claude/             ← Claude API provider
    └── Nexaflow.Providers.Ollama/             ← Ollama local model provider
```

**Dependency rule:** Features depend on `Features.Common` (and the `Nexaflow.Visuals.*` UI libs) only — never on Core or each other. `Nexaflow.Core` depends on all features and providers (for registration in `App.xaml.cs`) but never instantiates feature view or view-model types in its hot paths — all tab creation goes through `FeatureManager` (which lives in Core). Features communicate back to the shell exclusively through `IShellServices`. Shared non-contract code goes in `Nexaflow.Visuals.*`, not `Features.Common`.

---

## Ownership & Lifetime

There are four nesting scopes. **Getting a thing's scope wrong is the easiest way to introduce a bug**, so this is the canonical reference for what is central to the process vs tied to a `WorkContext` vs per-feature vs per-window/tab.

### Central — one instance per process

`*.Instance` singletons created during `App.InitializeApp`, before any window:

| Singleton | Owns | NOT responsible for |
|-----------|------|---------------------|
| `ConfigManager` | Base data path; **global** config registry. `Register(cfg,name)` = global config; `LoadFrom`/`SaveTo(dir,…)` = per-context config in that context's folder | — |
| `ProviderManager` | Loads provider **assemblies** by file name; records provider/config **types**; owns the shared `ActivityManager` | Holds **no** provider instances — each context builds its own `ProviderSet` via `CreateProviderSet(contextDir)` |
| `BackgroundActivityManager` | The one activity/notification surface (passed to ProviderManager, every window, every ShellServices) | — |
| `WorkContextManager` | Registry of all `WorkContext`s (`Contexts`) + their create/clone/remove/bootstrap lifecycle | — |
| `FeatureManager` | Reflection discovery of feature **types** once at startup; builds feature instances **per (Type, WorkContext)** on demand | File-system contracts (those go to `FileSystemFeatureRegistry`) |
| `FileMapManager`, `ExternalAppRegistry`, `WhisperModelManager`, `HostCapabilityService`, `MessageCenter`, `JumpListService` | Misc app-wide services | — |

**Global configs** (registered app-level, shared by every context): `ShellConfig` (theme), **`AiPersonaConfig` (assistant name + system prompt)**, `WorkContextsConfig` (the context-list metadata), `FileMapConfig`, `ExternalAppsConfig`, `VoiceConfig`.

> ⚠️ The **AI persona** (name + system prompt) is **global**, but **which provider/model answers each ability** is **per-context**. Don't conflate them.

### Per-`WorkContext` — one instance per named context

A `WorkContext` (`Core/Models/WorkContext.cs`) is a named, themed workspace. Only `Name`/`Color`/`Icon` persist (in `WorkContextsConfig`); everything below is runtime-only (`[JsonIgnore]`), rebuilt by `WorkContextManager.BootstrapServices`, with on-disk state under `…\Smile\nexaflow\Contexts\<Name>\`:

| Field on `WorkContext` | What it scopes | On disk |
|------------------------|----------------|---------|
| `Providers : ProviderSet` | This context's LLM provider **instances** + their configs (API keys / subscriptions). Two contexts can hold different subscriptions for the same provider | provider config folders |
| `AiConfig` | The ability → provider/model **assignments** + configured columns (the AI ability grid) | `ai-abilities/` |
| `AiService : AIService` | The agent loop + AI-input-bar routing for this context — resolves each `AiAbility` through **this context's** providers/assignments; owns this context's conversation history | `Conversations/` |
| `ShellServices : ShellServices` | This context's **window registry + tab registry** (the windows/tabs currently showing this context). Preserved across re-init — holds live state | — |
| `RibbonService : RibbonLayoutService` | This context's ribbon layout — **the ribbon is per-context** | per-context file |

The `IShellServices` / `IAIService` injected into a feature are the **active context's** instances (`FeatureManager` resolves them per `WorkContext`). So "open a tab" and "ask the AI" always act within exactly one context.

### Per-window / per-tab

- A **window** (`IWindowHost`) registers into its context's `ShellServices`. Several windows can show one context; a window can **switch** context, which moves its host + tabs to the target context's `ShellServices` via `TransferWindowTo`. Tearoff / "new window" currently default to `Contexts[0]`.
- A **`Page`** (tab) is built by `FeatureManager.CreateTab(pageKind, workContext, params)` → the matching `IPageRegistration.CreatePage`. Its ViewModel + content live for the life of the tab.

### Per-feature (assembly)

- An **`IFeatureConfig`** is a **single, app-level instance per assembly** — discovered once in `RegisterFeatures`, never rebuilt. **Feature settings are global, not per-context** (persisted at `…\Smile\nexaflow\<ConfigName>\`, outside `Contexts\`). Contrast `AiConfig`, which *is* per-context. There is no per-context feature-config mechanism today — adding one would be new work, not a config-path change.

---

## Module Responsibilities

### Nexaflow.Core

The shell host. Owns the window chrome, tab strip, ribbon bar, breadcrumb bar, and the AI input row. Nothing in Core knows how to _render_ any individual page — it only knows how to _host_ them.

| File | Responsibility |
|------|----------------|
| `App.xaml.cs` | Startup (`InitializeApp`): global-config + provider-**assembly** load, per-context bootstrap via `WorkContextManager`, feature discovery, first window. Also the windowless `--prestart` daemon and single-instance / new-window IPC |
| `Models/WorkContext.cs` | The per-context workspace model (holds `Providers`/`AiConfig`/`AiService`/`ShellServices`/`RibbonService`) — see [Ownership & Lifetime](#ownership--lifetime) |
| `Services/WorkContextManager.cs` | Singleton registry of `WorkContext`s; `Initialize`/`Create`/`Clone`/`Remove`, and `BootstrapServices` (builds each context's per-context services) |
| `ProviderManager.cs` | Singleton: loads provider **assemblies** + records provider/config **types**; `CreateProviderSet(contextDir)` builds a context's instances. Holds no provider instances itself |
| `ProviderSet.cs` | A single context's provider **instances** + their configs + assembly map |
| `Services/AIService.cs` | `IAIService` impl — **per-context**: provider registry, ability→model resolution, the agent loop, conversation history |
| `AI/AiConfig.cs` | Per-context AI ability config (`Columns` + ability→column `Assignments`); rendered by `AiAbilityGridControl` |
| `MainWindow.xaml.cs` | Wires shell commands; creates `ShellViewModel`; handles ESC/breadcrumb clicks |
| `ViewModels/ShellViewModel.cs` | Tab lifecycle, ribbon lifecycle, notifications, AI routing, background tasks |
| `Services/ShellServices.cs` | `IShellServices` impl — **per-WorkContext** (not an app-level singleton, despite the older class comment). Owns that context's window + tab registry and coordinates the windows showing this context |
| `FeatureManager.cs` | Singleton (`FeatureManager.Instance`). Reflection-loads every `Nexaflow.Features.*.dll` at startup, records `IPageRegistration`/config/handler **types** without instantiating, then builds instances per `WorkContext` with scoped `IShellServices` + `IAIService`. File-system contracts are **not** here (see below) |
| `Services/FileSystemFeatureRegistry.cs` | Discovery + matching for `IFileAction`/`IFolderAction`/`IFileCreateAction`/`IFolderViewlet` (deliberately split out of `FeatureManager`) |
| `Services/RibbonLayoutService.cs` | Serialize/deserialize ribbon items — **per-context** (constructed with the context's own folder), not a single global `ribbon.json` |
| `Services/WindowManager.cs` | Multi-window registry; tab tearoff; cross-window drag-transfer; DPI maths |
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
| `Page.cs` | Observable page/tab state: `Title`, `Icon`, `IsActive`, `Breadcrumbs`, `PageKind`, `PageParams`, `ContentFactory` delegate, cached `Content`, `Closed` event |
| `BreadcrumbSegment.cs` | One crumb: label, drop-down children, same-tab `Navigate` action, or cross-tab `TargetPageKind` |
| `IPageRegistration.cs` | Interface a feature implements to advertise one page kind: `PageKind` + `CreatePage(params)`. Each impl also exposes `static string StaticPageKind` so `FeatureManager` discovers it by reflection |
| `OpenPageRequest.cs` | `(PageKind, PageParams)` carrier for shell-level open commands (breadcrumb follow-links). Core-internal MVVM glue — `arch_improvements.md` proposes relocating it to Core |
| `IPageView.cs` | Thin shell-lifecycle contract implemented by tab `UserControl`s: exposes `IPageViewModel? ViewModel` and `Reinitialize` |
| `IPageViewModel.cs` | AI pipeline contract implemented by ViewModels: `GetContext`, `GetClientTools`, `GetContextObject` |
| `IShellServices.cs` (under `Services/`) | The **active WorkContext's** shell handle injected into feature code (one impl per context, not an app singleton): `OpenTab`/`CloseTab`/`FindTab`, `QueueBackgroundTask`, `ShowError`/`ShowNotification`/`ShowPrompt`/`ShowConfirmation`, `PinToRibbon`, `DiscoverImplementations<T>` |
| `IAIService.cs` | Per-`WorkContext` AI service: handler scoring/disambiguation, the `RunAgentAsync` agent loop, conversation load/save, analysis + artifacts |
| `IBackgroundTask.cs` | Self-contained background work (`Description` + `RunAsync`) handed to `IShellServices.QueueBackgroundTask` |
| `IFeatureConfig.cs` | Marks a POCO as a config section; discovered and instantiated by `FeatureManager` |
| `IContext.cs` / `FileSystemContext.cs` | Typed context objects offered by ViewModels via `IPageViewModel.GetContextObject()` |
| `ClientTools/*.cs` | Agent-loop contracts: `IClientTool`/`DelegateClientTool`, `ClientToolParameter`, `ToolSafety`, `ToolCall`, `ToolResult`, `ClientPlan`, `IToolApprovalCoordinator`. **Also** currently holds the Core-only wire-protocol parser `ClientBlockParser` + `ParsedAssistantTurn` — `arch_improvements.md` proposes moving those two to Core |
| `ConfigAttributes.cs` | `[ConfigDisplayName]`, `[FolderPath]`, `[FilePath]`, `[ListSource]`, `[CustomControl]` / `ICustomConfigApply` / `IConfigChangeTracker` |
| `AiResponse.cs` | `AiResponse` record returned by `IAIService.RunAgentAsync` |
| `ConversationRecord.cs` | Chat conversation + message list + attachments |
| `Services/IQueryHandler.cs` | Intercepts the AI input bar; registered globally or inferred from the active page's ViewModel |
| `FileActions/IFileAction.cs` / `IFolderAction.cs` / `IFileCreateAction.cs` / `ICacheable.cs` | File/folder context-action contracts (discovered by `FileSystemFeatureRegistry`, not `FeatureManager`) |
| `Ribbon/*.cs` | `IRibbonPinHandler`, `IRibbonExecutionContext`, `ISelectionProvider`, `RibbonPinResult` — drag-to-ribbon pinning |
| `Viewlets/*.cs` | `IFolderViewlet`, `IViewletController`, `ViewletDisplayMode` — folder-display extensions |
| `IKeyboardHandler.cs` | Claims global keyboard shortcuts; queried against the active page |
| `IDropTarget.cs` | Accepts file drag-drop; resolved from the active page by the file browser |

### Feature assemblies

Each feature assembly follows the same internal structure:

- One or more `*PageRegistration : IPageRegistration` (some older ones still named `*TabRegistration`) — factory entry points, injected with `IShellServices` and any `IFeatureConfig` the assembly declares
- `ViewModels/` — MVVM Toolkit `ObservableObject` view models; never reference Core
- `Views/` — WPF `UserControl`s; implement `IPageView` to expose context to the AI pipeline
- Optional: `IQueryHandler` implementations registered globally or resolved from the active ViewModel
- Optional: `IFileAction` / `IFolderAction` implementations for file browser context menus
- Optional: `IFeatureConfig` POCO for persisted settings (auto-discovered by `FeatureManager`)

### Shared UI (Nexaflow.Visuals.*)

Non-contract UI shared across features and Core. Features may reference these (they are not `Features.Common`, but they carry no contracts — only controls/converters), and they never reference Core or a feature.

| Assembly | Holds |
|----------|-------|
| `Nexaflow.Visuals.Common` | Reusable WPF controls (`PieChart`) and the value converters (`BoolToVisibilityConverter`, `InverseBoolToVisibilityConverter`, `NullToBoolConverter`, …) used in nearly every feature view |
| `Nexaflow.Visuals.Text` | Markdown rendering: `MarkdownView` / `SelectableMarkdownView` (copy-aware) over `MarkdownFlowDocument` + `BlockRenderer`, plus Mermaid `DiagramRenderer`. Used by Core's `AiResponseOverlay` and AIChat's `ConversationView` |

**Theme/styles:** application brushes **and** shared control styles live in the app-merged `Nexaflow.Core/Themes/Styles.xaml`. Feature XAML references them by `{StaticResource <key>}` — there is no assembly reference; the lookup resolves up the tree to `Application.Resources`. Define a shared style there once rather than copy-pasting per view (`arch_improvements.md` tracks the duplicated toolbar/list/grid styles that should move here).

### Nexaflow.Providers.*

All providers implement `ILlmProvider` (and optionally `IAsyncDisposable`) from `Providers.Common`. `ProviderManager` loads the plugin assemblies at startup and records the provider/config **types** they expose — it does not create instances. Providers are **per-WorkContext**: each context builds its own `ProviderSet` via `ProviderManager.CreateProviderSet(contextDir)`, instantiating a fresh `IProviderConfig` per type loaded from that context's own folder (`Contexts/<name>/<configName>/`) and constructing the providers with it. So two contexts can hold different subscriptions for the same provider. The set lives on `WorkContext.Providers` and is registered into that context's `AIService`; the AI ability grid and provider-config editors operate on it through `WorkContext.AiConfig.Providers`.

| Assembly | Backing service |
|----------|----------------|
| `Nexaflow.Providers.Aria` | Windows named-pipe to the local Aria service. Two layers: `AriaNamedPipeClient` (raw JSON framing) and `AriaClientService` (one-shot request/reply, parses `#focustab` instructions from responses). |
| `Nexaflow.Providers.Claude` | Anthropic Claude API over HTTPS. |
| `Nexaflow.Providers.Ollama` | Ollama REST API for locally-hosted models. |

---

## Key Data Models

```
Page  (ObservableObject)
  ├── Title, Icon, IsActive             (observable — drives tab strip UI)
  ├── PageKind: string?                 (e.g. "Search", "Projects")
  ├── PageParams: Dict?                 (current params, kept in sync by the page via IShellServices)
  ├── Breadcrumbs: ObservableCollection<BreadcrumbSegment>
  ├── ContentFactory: Func<UserControl>? (set by IPageRegistration.CreatePage; lazy-invoked on first activation)
  ├── Content: UserControl              (cached after GetOrCreateContent() first call)
  └── Closed: event                     (raised on permanent close, not deactivation)

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
  └── TabFactory: Func<Page>?         (runtime only — re-attached on load via FeatureManager)

ConversationRecord
  ├── Id: Guid
  ├── Title: string
  ├── StartedAt: DateTime
  └── Messages: List<ConversationMessage>
        └── { Id, Text, IsUser, Timestamp }
```

---

## Extensibility Points

### `IPageRegistration` — Adding a new page kind

Implement in your feature assembly. `FeatureManager.RegisterFeatures()` discovers it **automatically by reflection** at startup — there is no manual `Register(typeof(...))` call. It injects constructor dependencies per `WorkContext`: any `IFeatureConfig` declared in the same assembly, the scoped `IShellServices`, and `IAIService`. Expose a `static string StaticPageKind` so discovery can read the page kind without instantiating.

```csharp
public sealed class MyPageRegistration(MyConfig config, IShellServices shellServices) : IPageRegistration
{
    public static string StaticPageKind => "MyFeature";   // read by FeatureManager via reflection
    public string PageKind => StaticPageKind;

    public Page CreatePage(Dictionary<string, string>? pageParams = null)
    {
        var vm = new MyViewModel(config, shellServices);
        return new Page
        {
            Title          = "My Feature",
            Icon           = "🔧",
            PageParams     = pageParams,
            Breadcrumbs    = { new BreadcrumbSegment { Label = "My Feature" } }, // get-only collection
            ContentFactory = () => new MyView(vm)                                // lazy-invoked on first activation
        };
    }
}
```

The page kind string is then available in the ribbon editor automatically. (Registration classes are conventionally named `*PageRegistration`; some older ones are still named `*TabRegistration` but implement the same `IPageRegistration` interface.)

---

### `IPageView` / `IPageViewModel` — Exposing context to the AI pipeline

`IPageView` is the shell's typed handle to a tab `UserControl` — implement it on your `UserControl`. It exposes the ViewModel and handles shell lifecycle. `Reinitialize` is called on first load and whenever the shell activates the tab with a new param set (including re-clicking the active tab).

`IPageViewModel` is the AI pipeline contract — implement it on your ViewModel so the shell can query context and expose **client tools** the AI agent may invoke. A client tool is a self-contained `IClientTool` (use `DelegateClientTool` for one-liners) carrying its own metadata and execution. Read-only tools (`ToolSafety.ReadOnly`) auto-run; mutating tools (`ToolSafety.RequiresApproval`) are approved first. `GetClientTools()` and `GetContextObject()` have defaults, so a page that only supplies context overrides nothing else.

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

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new DelegateClientTool(
            "refresh", "Reload the current view.", [], ToolSafety.ReadOnly,
            (args, ct) => { Reload(); return Task.FromResult(ToolResult.Ok("reloaded")); })
    ];
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

Implement in the assembly that owns the concern. `FileSystemFeatureRegistry` (in Core — **not** `FeatureManager`) discovers these across Core and the feature assemblies. Actions that open a viewer tab belong in the feature that owns the viewer; system-level actions (copy, rename…) belong in `Nexaflow.Core.FileActions`.

Actions are matched to files via `ExperienceId` — a hierarchical path (e.g. `"/image"`, `"/binary/installer"`) that `FileMapManager` resolves against the selected file. Constructor dependencies (e.g. `IShellServices`) are injected on resolution; show modal prompts via `IShellServices.ShowPrompt` / `ShowConfirmation` (the old `IInputPromptService` is gone).

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

Injected into `IPageRegistration` and `IFileAction` constructors. Provides the only approved path for features to interact with the shell. To update a page's own title/breadcrumbs, mutate the `Page` object directly (its properties are observable) — there is no `UpdateTabMeta`.

```csharp
shellServices.OpenTab("ProjectDetail", new() { ["folder"] = folder }, callerPage);
shellServices.CloseTab(page);
shellServices.FindTab("Search", new() { ["root"] = root });
shellServices.ShowError("Something went wrong");
shellServices.ShowNotification("Done");
shellServices.ShowPrompt("Rename", "New name", current, onConfirm: name => …, onCancel: () => …);
shellServices.ShowConfirmation("Delete?", "This cannot be undone.", onConfirm: () => …, onCancel: () => …);
shellServices.QueueBackgroundTask(myBackgroundTask, onComplete: ok => …);
```

---

### `IFeatureConfig` — Persisted settings

Implement a plain POCO. `FeatureManager` discovers it during `RegisterFeatures()`, instantiates it, and makes it available for injection into `IPageRegistration` constructors. The Options panel renders a property grid for free using reflection and the config attributes.

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

### `IClientTool` — AI-invokable client tools

Expose tools from a page by overriding `IPageViewModel.GetClientTools()`. Each tool is an `IClientTool` (or a `DelegateClientTool` for trivial cases) carrying its own name, parameters, `ToolSafety`, and `InvokeAsync`. During `IAIService.RunAgentAsync` the LLM emits `client_tool` blocks; the harness runs read-only tools immediately and routes mutating ones through `IToolApprovalCoordinator` before invoking them, feeding each `ToolResult` back to the model.

---

## Core Flows

### Startup

```
App.OnStartup
  → new BackgroundActivityManager()                         ← the one shared activity surface
  → InitializeApp(activityManager):
      → ConfigManager.Initialize(%AppData%\Smile\nexaflow)   ← base data path
      → register GLOBAL configs: ShellConfig (+ apply theme), AiPersonaConfig,
        WorkContextsConfig, FileMapConfig, ExternalAppsConfig, VoiceConfig
      → ProviderManager.Initialize(activityManager)         ← records the shared ActivityManager
      → for each saved context: ConfigManager.LoadFrom(ContextDir, ctx.AiConfig)
            (need each context's columns to know which provider DLLs to load)
      → ProviderManager.LoadConfigured(⋃ AssemblyFileName across all contexts' AiConfigs)
      → WorkContextManager.Initialize(wcConfig)             ← PER-CONTEXT bootstrap, for each context:
            CreateProviderSet(contextDir) → new AIService(ctx, Conversations\) →
            register providers → LoadAbilityConfig → ShellServices (preserved) → RibbonLayoutService
      → FileMapManager / ExternalAppRegistry init
      → FeatureManager.RegisterFeatures()                   ← reflection-load Nexaflow.Features.*.dll,
            record IPageRegistration / IFeatureConfig / handler TYPES (no instances yet)
      → WhisperModelManager + HostCapability probe, JumpList, single-instance IPC listener
  → pick startup WorkContext (Contexts[0], or the one named by --context "Name")
  → new MainWindow(activityManager, startupCtx)             ← window is bound to ONE context
      → ShellViewModel(ctx)   (CurrentWorkContext = ctx)
          → ctx.RibbonService.Load() → ReattachTabFactory each item
          → OpenDefaultTabs()
  → win.Show()

  (--prestart launches the windowless resident daemon: InitializeApp runs, no window is shown;
   a later click / JumpList signal opens a window via the single-instance IPC listener.)
```

### Opening a tab

```
User clicks ribbon button
  → ShellViewModel.RibbonAction(item)
  → item.TabFactory()                       ← Func<Page> set by ReattachTabFactory
  → ShellViewModel.OpenTab(page)
  → page.GetOrCreateContent()               ← ContentFactory invoked on first activation
  → CurrentPage = page, breadcrumbs updated

Feature code calls shellServices.OpenTab("PageKind", params, callerPage)
  (shellServices = the caller's WorkContext instance)
  → ShellServices resolves target window from callerPage / focused window
  → Searches this context's windows for an existing matching tab
  → If found: move to target window if needed → Reinitialize(params) → activate
  → If not found: FeatureManager.CreateTab(pageKind, workContext, params)   ← internal; returns a Page
      → matching IPageRegistration.CreatePage(params)
  → IWindowHost.AddTab(page) → prepends, sets active, loads content
```

### AI input bar

```
User submits text in AI input bar
  → ShellViewModel.SendAiMessage(input)
  → Check registered IQueryHandler list (Symbol prefix match first, then CanProcess scores)
  → If Symbol match: route directly to that handler
  → If multiple candidates score > 0: IAIService.DisambiguateToolSelection() picks one
  → If single candidate: ProcessAsync(input, currentPageVm)
  → If no handler: IAIService.RunAgentAsync(currentPageVm, input, includeContext, approval)
      → client-side agent loop: the LLM emits fenced ```client_tool / ```client_plan /
        ```client_prefill blocks (JSON bodies); the harness executes the page's IClientTool
        objects and feeds results back, looping until a final message or a prefill
      → read-only tools auto-run; mutating batches and plans need per-batch/plan approval
        via IToolApprovalCoordinator (the AiResponseOverlay)
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
      → ProjectDetailTabRegistration.CreatePage(params)
  → IWindowHost.AddTab(tab)
```

### Opening a viewer tab from a file action

```
User selects file in file browser → clicks action button
  → FileActionManager.PerformAction(action, paths)
  → e.g. ShowMarkdownAction.PerformAction(path)
      _shellServices.OpenTab("Markdown", { "path": filePath })
  → ShellServices → MarkdownTabRegistration.CreatePage(params) → tab opens
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

**Problem:** String literals must match `IPageRegistration.PageKind` exactly. No compile-time check.

**Fix:** Registrations already expose `static string StaticPageKind` (used for reflection discovery) — reference that constant from `BuildDefaultItems` instead of re-typing the literal.

---

### 6. `RibbonEditor` mixes model logic with view construction

**Where:** `Nexaflow.Core/Controls/RibbonEditor.xaml.cs`

**Problem:** ~710 lines of procedural `Border`/`StackPanel`/`TextBlock` construction. Drag-reorder logic, colour swatches, and draft-clone logic share the file. Direct `ShellViewModel` reference.

**Fix:** Extract `RibbonEditorViewModel`; replace `ShellViewModel` dependency with `IDefaultRibbonProvider`.