# Nexaflow Architecture

> **Freshness:** reviewed/refreshed 2026-05-31, including the `Profile` (saved config) vs `Workspace`
> (runtime) split and the ref-counted provider pool — see [Ownership & Lifetime](#ownership--lifetime)
> for what is central vs profile-scoped vs workspace-scoped.
> Architectural cleanup opportunities are tracked in [arch_improvements.md](arch_improvements.md).

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
│   ├── Nexaflow.Features.Dotnet/             ← .NET folder viewlet — AI context + dotnet client tools
│   ├── Nexaflow.Features.Git/                 ← Git folder viewlet — AI context + git client tools
│   ├── Nexaflow.Features.Hex/                 ← Binary / hex viewer tab
│   ├── Nexaflow.Features.Images/              ← Image viewer tab
│   ├── Nexaflow.Features.Json/                ← JSON viewer tab (seek-by-item windowing)
│   ├── Nexaflow.Features.Logs/                ← Log viewer tab (tail-first streaming)
│   ├── Nexaflow.Features.Markdown/            ← Markdown editor/preview tab
│   ├── Nexaflow.Features.Processes/           ← Process Explorer (live tree + per-process detail tabs; elevated kill/priority)
│   ├── Nexaflow.Features.Projects/            ← Project management tabs
│   ├── Nexaflow.Features.Scratchpad/          ← Virtual corkboard tab
│   ├── Nexaflow.Features.SystemInfo/          ← System info dashboard (WMI; Services/EnvVars via privilege bridge)
│   ├── Nexaflow.Features.Tabular/             ← CSV/TSV/fixed-width viewer tab (shape detection + transforms)
│   ├── Nexaflow.Features.Text/                ← Text editor tab (head-first windowing)
│   ├── Nexaflow.Features.Web/                 ← HTML/URL viewer tab
│   ├── Nexaflow.Features.WindowsApps/         ← Installed-apps manager + AI query handler
│   ├── Nexaflow.Features.WindowsFileSystem/   ← File explorer tab (DirectoryTree + file list)
│   ├── Nexaflow.Features.WindowsRegistry/     ← Registry browser/editor tab + AI tools
│   └── Nexaflow.Features.WindowsSearch/       ← Windows Search integration tab
│
├── Nexaflow.Visuals.Common/                    ← Shared WPF controls + converters + formatters (PieChart, BoolToVisibility, SizeFormatter, BytesToTextConverter, …)
├── Nexaflow.Visuals.Text/                      ← Shared markdown rendering (MarkdownView / SelectableMarkdownView / MarkdownFlowDocument)
├── Nexaflow.IO.Common/                         ← Shared file-reading leaves: EncodingDetector + debounced FileChangeWatcher (net10.0, no WPF)
│
└── Nexaflow.Providers/
    ├── Nexaflow.Providers.Common/             ← LlmProviderRegistry, shared message types
    ├── Nexaflow.Providers.Aria/               ← Named-pipe client for Aria AI service
    ├── Nexaflow.Providers.Claude/             ← Claude API provider
    ├── Nexaflow.Providers.Gemini/             ← Google Gemini API provider
    ├── Nexaflow.Providers.Ollama/             ← Ollama local model provider
    └── Nexaflow.Providers.OpenAI/             ← OpenAI API provider
```

**Dependency rule:** Features depend on `Features.Common` (and the shared `Nexaflow.Visuals.*` UI libs / `Nexaflow.IO.Common` utility lib) only — never on Core or each other. `Nexaflow.Core` depends on all features and providers (for registration in `App.xaml.cs`) but never instantiates feature view or view-model types in its hot paths — all tab creation goes through `FeatureManager` (which lives in Core). Features communicate back to the shell exclusively through `IShellServices`. Shared non-contract code goes in `Nexaflow.Visuals.*` (UI) or `Nexaflow.IO.Common` (file-reading utilities), not `Features.Common`.

---

## Ownership & Lifetime

There are nesting scopes. **Getting a thing's scope wrong is the easiest way to introduce a bug**, so this is the canonical reference for what is central to the process vs tied to a `Profile` (saved config) vs a `Workspace` (runtime) vs per-feature vs per-window/tab.

The model has two halves: a **`Profile`** is the saved, shared configuration shown in the dropdown (name/colour/icon + ribbon layout + AI ability grid + provider configs + conversations); a **`Workspace`** is a runtime grouping of one-or-more window frames all running ONE profile. App/IPC launch always creates a *new* Workspace (launch ×3 ⇒ 3 Workspaces, possibly all on one profile); tear-off / "open in new window" reuse the *same* Workspace; switching the dropdown reconfigures the current Workspace in place; closing a Workspace's last window disposes it.

### Central — one instance per process

`*.Instance` singletons created during `App.InitializeApp`, before any window:

| Singleton | Owns | NOT responsible for |
|-----------|------|---------------------|
| `ConfigManager` | Base data path; **global** config registry. `Register(cfg,name)` = global config; `LoadFrom`/`SaveTo(dir,…)` = per-profile config in that profile's folder | — |
| `ProviderManager` | Loads provider **assemblies** by file name; records provider/config **types**; owns the shared `ActivityManager`; owns the **global ref-counted provider instance pool** (`AcquireProviderSet`/`ReleaseProviderSet`). Each `ILlmProvider` is **model-bound** (model injected via `ProviderModel`): one *capability* instance per (type+config) for model enumeration, plus one *execution* instance per (type+config+**model**) the grid assigns — warmed on first acquire, cooled on last release | Holds provider **configs** — those live on the `Profile` |
| `BackgroundActivityManager` | The one activity/notification surface (passed to ProviderManager, every window, every ShellServices) | — |
| `WorkspaceManager` | The `Profiles` list (dropdown source) + the live `Workspace`s; create/switch/reconfigure/dispose lifecycle | — |
| `FeatureManager` | Reflection discovery of feature **types** once at startup; builds feature instances **per (Type, Workspace)** on demand; `EvictWorkspace` drops them on reconfigure/dispose | File-system contracts (those go to `FileSystemFeatureRegistry`) |
| `FileMapManager`, `ExternalAppRegistry`, `WhisperModelManager`, `HostCapabilityService`, `MessageCenter`, `JumpListService` | Misc app-wide services | — |

**Global configs** (registered app-level, shared by every profile): `ShellConfig` (theme), **`AiPersonaConfig` (assistant name + system prompt)**, `WorkspacesConfig` (the profile-list metadata; `ConfigName` is `"workcontexts"` on disk), `FileMapConfig`, `ExternalAppsConfig`, `VoiceConfig`.

> ⚠️ The **AI persona** (name + system prompt) is **global**, but **which provider/model answers each ability** is **per-profile**. Don't conflate them.

### Config versioning & migration

Every config (global or per-profile) persists as `…\{configName}\config_{AssemblyVersion}.json`, so the filename records the version that wrote it. On load — `ConfigManager.Register` for global, `LoadFrom` for per-profile — when the current-version file is absent but an older one exists, `ConfigManager` **migrates it forward** rather than discarding it:

1. The newest older `config_*.json` is loaded with a **lenient field-by-field carry-over**: unknown JSON fields are skipped and missing ones keep their type defaults, so additive and removed fields need no code.
2. A shape change the carry-over can't express (a rename or restructure) opts into **`IConfigMigration`** — a tiny one-method interface mirrored in `Features.Common` and `Providers.Common` (kept parallel so the layering rule holds; Core checks both). Its `MigrateFrom(previousJson, previousVersion)` runs right after the carry-over, with the raw old JSON in hand.
3. The result is rewritten under the current version and the stale files are deleted (**write-then-delete**, so a failed write never loses the prior data).

Migrated configs are tracked apart from brand-new ones (`GetMigratedConfigs` vs `GetDefaultedConfigs`). The first-run/update **setup wizard** (`SetupWizardViewModel.Build`) therefore re-asks for a global mandatory config only when it is genuinely new **or** its migrated data still fails the required-field check (`AreRequiredPropertiesSatisfied`) — never for information already on disk; the workspace/provider/model flow is skipped because the migrated per-profile configs keep `IsWorkspaceConfigured` true. File-type mappings (`FileMapManager.SyncBundledDefaults`) follow the same spirit through a `_defaults.json` hash manifest: a changed bundled default refreshes mappings the user hasn't touched and leaves customized ones alone, fast-pathing when the bundle is unchanged.

**Reset.** Options → About offers a danger-styled **Reset Config** that, after a window-modal confirmation, wipes the entire `%APPDATA%\Smile\nexaflow` tree and relaunches (`App.ResetAndRestart` arms a write-suppressor, drops the single-instance mutex, then starts a `--reset` process that deletes the directory **before** init — lock-safe because the fresh process holds no handle) straight into first-run.

### Per-`Profile` — shared, saved (one instance per named profile)

A `Profile` (`Core/Models/Profile.cs`) is a named, themed, saved workspace configuration. `Name`/`Color`/`Icon` persist (in `WorkspacesConfig`); the shared services below are runtime-only (`[JsonIgnore]`), loaded once by `Profile.EnsureSharedServicesLoaded`, with on-disk state under `…\Smile\nexaflow\Contexts\<Name>\` (the on-disk folder is named `Contexts`):

| Member on `Profile` | What it scopes | On disk |
|---------------------|----------------|---------|
| `AiConfig` | The ability → provider/model **assignments** + configured columns. Shared, so an edit shows in every Workspace on this profile | `ai-abilities/` |
| `ProviderConfigs` | The provider configs (API keys / subscriptions) used to build provider instances | provider config folders |
| `RibbonService : RibbonLayoutService` + `RibbonChanged` | The shared ribbon layout. Saving raises `RibbonChanged`, which every window/Workspace on this profile observes to reload live | `ribbon.json` |
| `ConversationsDir` | Where this profile's conversations are stored | `Conversations/` |

### Per-`Workspace` — runtime (one per app/IPC launch; can have many windows)

A `Workspace` (`Core/Models/Workspace.cs`) points at one `Profile` and owns the live per-session services, built by `WorkspaceManager.BootstrapServices` / rebuilt by `ReconfigureWorkspace`:

| Member on `Workspace` | What it scopes |
|-----------------------|----------------|
| `Providers : ProviderSet` | The live provider instances **acquired from the pool** for this workspace (deduped by config across all workspaces/profiles); released when the workspace is reconfigured/disposed |
| `AiService : AIService` | The agent loop + AI-input-bar routing — resolves each `AiAbility` through this profile's assignments + this workspace's providers; reads/writes the profile's conversations |
| `ShellServices : ShellServices` | This workspace's **window + tab registry** (every window in the workspace). Stable for the workspace's life — a profile switch reconfigures internals, not this object |

The `IShellServices` / `IAIService` injected into a feature are the **active workspace's** instances (`FeatureManager` resolves them per `Workspace`). So "open a tab" and "ask the AI" always act within exactly one workspace.

### Per-window / per-tab

- A **window** (`IWindowHost`) registers into its context's `ShellServices`. Several windows can show one context; a window can **switch** context, which moves its host + tabs to the target context's `ShellServices` via `TransferWindowTo`. Tearoff / "new window" currently default to `Contexts[0]`.
- A **`Page`** (tab) is built by `FeatureManager.CreateTab(pageKind, workContext, params)` → the matching `IPageRegistration.CreatePage`. Its ViewModel + content live for the life of the tab.

### Per-feature (assembly)

- An **`IFeatureConfig`** is a **single, app-level instance per assembly** — discovered once in `RegisterFeatures`, never rebuilt. **Feature settings are global, not per-profile** (persisted at `…\Smile\nexaflow\<ConfigName>\`, outside `Contexts\`). Contrast `AiConfig`, which *is* per-profile. There is no per-profile feature-config mechanism today — adding one would be new work, not a config-path change.

---

## Module Responsibilities

### Nexaflow.Core

The shell host. Owns the window chrome, tab strip, ribbon bar, breadcrumb bar, and the AI input row. Nothing in Core knows how to _render_ any individual page — it only knows how to _host_ them.

| File | Responsibility |
|------|----------------|
| `App.xaml.cs` | Startup (`InitializeApp`): global-config + provider-**assembly** load, `WorkspaceManager.Initialize` (profiles only), feature discovery, first window (a fresh `Workspace`). Also the windowless `--prestart` daemon and single-instance / new-window IPC (each IPC launch = a new `Workspace`) |
| `Models/Profile.cs` | The saved, shared profile (holds `AiConfig`/`ProviderConfigs`/`RibbonService`+`RibbonChanged`/conversations dir) — see [Ownership & Lifetime](#ownership--lifetime) |
| `Models/Workspace.cs` | The runtime workspace (points at a `Profile`; holds `Providers`/`AiService`/`ShellServices`) |
| `Services/WorkspaceManager.cs` | Singleton: the `Profiles` list + live `Workspace`s; `Initialize`/`CreateWorkspace`/`SwitchProfile`/`ReconfigureWorkspace`/`NotifyWindowClosed`, `Add`/`Clone`/`RemoveProfile`, `BootstrapServices` |
| `ProviderManager.cs` | Singleton: loads provider **assemblies** + records provider/config **types**; `LoadProviderConfigs(dir)` + the ref-counted pool `AcquireProviderSet`/`ReleaseProviderSet`. Instances are model-bound (capability per config + execution per config+model); execution instances warm/cool with their pool lifetime |
| `ProviderSet.cs` | A workspace's acquired provider **instances** + the profile's configs + assembly map + pool keys |
| `Services/AIService.cs` | `IAIService` impl — **per-Workspace**: provider registry, ability→model resolution, the agent loop, conversation history |
| `AI/AiConfig.cs` | Per-profile AI ability config (`Columns` + ability→column `Assignments`); rendered by `AiAbilityGridControl` |
| `MainWindow.xaml.cs` | Wires shell commands; creates `ShellViewModel`; handles ESC/breadcrumb clicks |
| `ViewModels/ShellViewModel.cs` | Tab lifecycle, ribbon lifecycle, notifications, AI routing, background tasks; `SelectProfile` (in-place switch, blocked while a modal overlay is open) |
| `Services/ShellServices.cs` | `IShellServices` impl — **per-Workspace** (not an app-level singleton). Owns that workspace's window + tab registry |
| `FeatureManager.cs` | Singleton (`FeatureManager.Instance`). Reflection-loads every `Nexaflow.Features.*.dll` at startup, records `IPageRegistration`/config/handler **types** without instantiating, then builds instances per `Workspace` with scoped `IShellServices` + `IAIService` (`EvictWorkspace` drops them on reconfigure). File-system contracts are **not** here (see below) |
| `Services/FileSystemFeatureRegistry.cs` | Discovery + matching for `IFileAction`/`IFolderAction`/`IFileCreateAction`/`IFolderViewlet` (deliberately split out of `FeatureManager`) |
| `Services/RibbonLayoutService.cs` | Serialize/deserialize ribbon items — **per-profile** (constructed with the profile's own folder, shared by its workspaces), not a single global `ribbon.json` |
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
| `IShellServices.cs` (under `Services/`) | The **active Workspace's** shell handle injected into feature code (one impl per workspace, not an app singleton): `OpenTab`/`CloseTab`/`FindTab`, `QueueBackgroundTask`, `ShowError`/`ShowNotification`/`ShowPrompt`/`ShowConfirmation`, `PinToRibbon`, `DiscoverImplementations<T>`. Owns UI-thread access for features: `WatchFile(path, onChanged)` (shared, deduped, UI-marshalled, lifecycle-managed file watching → `IFileWatch`) and `RunOnUiAsync(...)` (marshal work to the workspace UI thread). Features use these instead of `Application.Current.Dispatcher` |
| `IFileWatch.cs` (under `Services/`) | Handle from `WatchFile`: `Enabled` (hold + coalesce while false, flush on re-enable), `Dispose` to unwatch |
| `IAIService.cs` | Per-`Workspace` AI service: handler scoring/disambiguation, the `RunAgentAsync` agent loop, conversation load/save, analysis + artifacts |
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

- One or more `*PageRegistration : IPageRegistration` (a few are named `*TabRegistration` but implement the same interface) — factory entry points, injected with `IShellServices` and any `IFeatureConfig` the assembly declares
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

Implement in your feature assembly. `FeatureManager.RegisterFeatures()` discovers it **automatically by reflection** at startup. It injects constructor dependencies per `Workspace`: any `IFeatureConfig` declared in the same assembly, the scoped `IShellServices`, and `IAIService`. Expose a `static string StaticPageKind` so discovery can read the page kind without instantiating.

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

The page kind string is then available in the ribbon editor automatically. (Registration classes are conventionally named `*PageRegistration`; a few are named `*TabRegistration` but implement the same `IPageRegistration` interface.)

---

### `IThemeContribution` — Extending theming from a feature

Optional. A feature ships a `ResourceDictionary` of region-token defaults and/or `Scene.{Region}`
templates and advertises its pack URIs via `IThemeContribution`; `FeatureManager` discovers it by
reflection (same path as `IPageRegistration`) and `ThemeManager` merges it below the active theme as a
fallback. The coupling between features, shell and themes is only string resource keys, so all three stay
independently shippable. Full model — region tokens, `ThemedRegion` scenes, authoring a theme — in
[theming.md](theming.md).

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

Actions are matched to files via `ExperienceId` — a hierarchical path (e.g. `"/image"`, `"/binary/installer"`) that `FileMapManager` resolves against the selected file. Constructor dependencies (e.g. `IShellServices`) are injected on resolution; show modal prompts via `IShellServices.ShowPrompt` / `ShowConfirmation`.

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

`IFolderAction`s are matched **structurally** rather than by `ExperienceId`: `FolderNameGlob` (folder name), `ContainsFolderGlobs` (has a matching sub-folder), and `ContainsFileGlobs` (has matching files). When `ContainsFileGlobs` is set, `MinimumFileGlobMatchPercentage` (default `0` = "at least one match") requires that share of the folder's top-level files to match — e.g. the image Slideshow/Album actions require ≥30% images. The content check enumerates the folder once and bails as soon as the threshold is reached or has become unreachable. The same name/content constraints are applied to `AppliesToRoot` actions against the **currently-open folder** (no selection), so a constrained action only appears on a qualifying folder, not every folder.

---

### `IShellServices` — Calling back to the shell

Injected into `IPageRegistration` and `IFileAction` constructors. Provides the only approved path for features to interact with the shell. To update a page's own title/breadcrumbs, mutate the `Page` object directly (its properties are observable).

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
        WorkspacesConfig, FileMapConfig, ExternalAppsConfig, VoiceConfig
      → ProviderManager.Initialize(activityManager)         ← records the shared ActivityManager
      → for each saved profile: temp AiConfig ← ConfigManager.LoadFrom(ProfileDir)
            (need each profile's columns to know which provider DLLs to load)
      → ProviderManager.LoadConfigured(⋃ AssemblyFileName across all profiles' AiConfigs)
      → WorkspaceManager.Initialize(wcConfig)               ← loads the Profiles list ONLY (no runtime workspaces)
      → FileMapManager / ExternalAppRegistry init
      → FeatureManager.RegisterFeatures()                   ← reflection-load Nexaflow.Features.*.dll,
            record IPageRegistration / IFeatureConfig / handler TYPES (no instances yet)
      → WhisperModelManager + HostCapability probe, JumpList, single-instance IPC listener
  → pick startup Profile (Profiles[0], or the one named by --context "Name")
  → WorkspaceManager.CreateWorkspace(profile)               ← profile.EnsureSharedServicesLoaded +
        BootstrapServices: AcquireProviderSet(pool) → new AIService(ws, Conversations\) →
        register providers → LoadAbilityConfig → ShellServices
  → new MainWindow(activityManager, startupWs)              ← window bound to ONE workspace
      → ShellViewModel(ws)   (CurrentWorkspace = ws)
          → ribbon binds ws.Profile → RibbonViewModel.SetProfile → Load() (shared layout)
          → OpenDefaultTabs()
  → win.Show()

  (Each subsequent app/IPC launch = a NEW Workspace. Tear-off / "open in new window" reuse the SAME
   workspace. --prestart launches the windowless resident daemon: InitializeApp runs, NO workspace is
   created; a later click / JumpList signal opens a window — a new workspace — via the IPC listener.)
```

### Opening a tab

```
User clicks ribbon button
  → RibbonControl.InternalRibbonActionCommand → ShellViewModel.OpenRibbonItem(item)
  → _shellServices.OpenTab(item.PageKind, item.PageParams)   ← persisted items carry only PageKind/params
  → page.GetOrCreateContent()               ← ContentFactory invoked on first activation
  → CurrentPage = page, breadcrumbs updated

Feature code calls shellServices.OpenTab("PageKind", params, callerPage)
  (shellServices = the caller's Workspace instance)
  → ShellServices resolves target window from callerPage / focused window
  → Searches this workspace's windows for an existing matching tab
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
Save:  RibbonItems changes → CollectionChanged → RibbonViewModel.Save()
         → profile.RibbonService.Save(items) → profile.RaiseRibbonChanged()
         → every other window/Workspace on this profile reloads its items live

Load:  profile bound → RibbonViewModel.SetProfile → profile.RibbonService.Load()
         items are pure metadata (PageKind + params); they open via the window's
         OpenRibbonItem command — no per-window delegate reattachment needed
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