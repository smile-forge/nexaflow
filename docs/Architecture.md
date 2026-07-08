# Nexaflow Architecture

> **Freshness:** reviewed/refreshed 2026-07-08 — the saved/runtime rename is applied throughout
> (saved config = `Workspace`, runtime = `WorkspaceRuntime`; the old `Profile`/`WorkContext` names
> survive only as on-disk/IPC compat strings), the solution layout is tiered rather than enumerated
> (the feature inventory lives in the product tree), and the elevation subsystem is documented.
> Architectural findings + cleanup opportunities are tracked in
> [arch_review_2026-07.md](arch_review_2026-07.md).

## Table of Contents

1. [Solution Layout](#solution-layout)
2. [Ownership & Lifetime](#ownership--lifetime)
3. [Module Responsibilities](#module-responsibilities)
4. [Key Data Models](#key-data-models)
5. [Extensibility Points](#extensibility-points)
6. [Elevation / Privilege Bridge](#elevation--privilege-bridge)
7. [Core Flows](#core-flows)
8. [Architectural Findings](#architectural-findings)

---

## Solution Layout

52 projects in `Nexaflow.slnx`, organised in tiers. **The per-feature inventory is deliberately not
enumerated here** — a hand-copied list rots. The authoritative inventory and per-component status is
the **product tree** (`.product/tree.json`, queried via the product-folder skill; per-release export in
[product/PRODUCT.md](product/PRODUCT.md)); the annotated directory skeleton every session loads is in
the root `CLAUDE.md`. This section fixes the tiers and the rules between them.

| Tier | Projects | Role |
|------|----------|------|
| Shell | `Nexaflow.Core` | WinExe entry point; window chrome, tab strip, ribbon, breadcrumbs, AI input bar; `FeatureManager`/`FeatureCatalog`, `WorkspaceManager`, `ProviderManager`, `ConfigManager` |
| Contracts | `Nexaflow.Features.Common` | ALL feature-facing interfaces + small DTOs. Features and Core meet only here |
| Features | `Nexaflow.Features.*` (~30, + 3 `Compressed.*` codec backends) | One assembly per feature; reflection-discovered, lazily loaded |
| Shared leaves (non-contract) | `Nexaflow.Visuals.Common` (controls/converters/formatters), `Nexaflow.Visuals.Text` (markdown rendering), `Nexaflow.Visuals.Terminal` (terminal input logic), `Nexaflow.IO.Common` (encoding/line-endings, glob, hashing, base64 + `IStreamCodec`/`IArchiveHandler` codec contracts, the archive `VirtualFileSystem`, text index/transforms, file watching/splitting), `Nexaflow.IO.Terminal` (PTY host), `Nexaflow.Syntax` (tree-sitter engine) | Concrete shared code a feature may reference; never references Core or a feature |
| Providers | `Nexaflow.Providers.Common` + one project per LLM backend (Claude, Gemini, OpenAI, Ollama, Aria) | Loaded by file name at runtime; never compile-time referenced |
| Elevation | `Nexaflow.Elevation.Contracts` (pure DTO leaf) + `Nexaflow.PrivilegeBridge` (separate elevated exe) | The admin-action trust boundary — see [Elevation / Privilege Bridge](#elevation--privilege-bridge) |
| Tests | `Nexaflow.Tests.{Core,Features,Providers}` + `Nexaflow.Tests.Fixtures` | See [testing.md](testing.md) |

**Dependency rules** (mechanically enforced since 2026-07 by `Nexaflow.Tests.Features/Architecture/*`
and `Nexaflow.Tests.Providers/ArchitectureRulesTests` — a violation is a red build):

- Features depend on `Features.Common` + the shared leaves only — never on Core, never on each other.
  (The `Compressed.*` codec backends reference only `IO.Common`, where the codec contracts live —
  not even the Compressed feature; they're discovered at runtime.)
- Providers depend on `Providers.Common` only — never on Core, never on each other.
- `Nexaflow.Core` depends on all features and providers (so they ship) but never instantiates feature
  view/view-model types — all tab/viewlet creation goes through `FeatureManager`, all provider
  instances through `ProviderManager`'s pool. Features talk back only via `IShellServices`.
- Shared non-contract code goes in `Nexaflow.Visuals.*` / `Nexaflow.IO.*` / `Nexaflow.Syntax`, not
  `Features.Common`; `Elevation.Contracts` is the fourth shared leaf (referenced by `Features.Common`
  for the `IShellServices.RunElevatedAsync` signature).

---

## Ownership & Lifetime

There are nesting scopes. **Getting a thing's scope wrong is the easiest way to introduce a bug**, so this is the canonical reference for what is central to the process vs tied to a `Workspace` (saved config) vs a `WorkspaceRuntime` (runtime) vs per-feature vs per-window/tab.

The model has two halves: a **`Workspace`** (`Models/Workspace.cs`) is the saved, shared configuration shown in the dropdown — name/colour/icon, default + last-session tabsets, AI ability grid, persona, ribbon layout, provider configs, conversations; a **`WorkspaceRuntime`** (`Models/WorkspaceRuntime.cs`) is a runtime grouping of one-or-more window frames all running ONE Workspace. App/IPC launch always creates a *new* WorkspaceRuntime (launch ×3 ⇒ 3 runtimes, possibly all on one Workspace); tear-off / "open in new window" reuse the *same* runtime; switching the dropdown reconfigures the current runtime in place (`WorkspaceManager.SwitchWorkspace`); closing a runtime's last window disposes it.

> **Naming history:** before 2026-07 the saved half was named `Profile` (and earlier, `WorkContext`).
> The old names survive **only** as frozen on-disk/IPC compat strings — the `workcontexts` config
> name, the `Contexts\` data folder, the `--context` command-line/IPC flag. Never reintroduce them
> as type or member names.

### Central — one instance per process

`*.Instance` singletons created during `App.InitializeApp`, before any window:

| Singleton | Owns | NOT responsible for |
|-----------|------|---------------------|
| `ConfigManager` | Base data path; **global** config registry. `Register(cfg,name)` = global config; `LoadFrom`/`SaveTo(dir,…)` = per-workspace config in that workspace's folder | — |
| `ProviderManager` | Loads provider **assemblies** by file name; records provider/config **types**; owns the shared `ActivityManager`; owns the **global ref-counted provider instance pool** (`AcquireProviderSet`/`ReleaseProviderSet`). Each `ILlmProvider` is **model-bound** (model injected via `ProviderModel`): one *capability* instance per (type+config) for model enumeration, plus one *execution* instance per (type+config+**model**) the grid assigns — warmed on first acquire, cooled + disposed on last release | Holds provider **configs** — those live on the saved `Workspace` |
| `BackgroundActivityManager` | The one activity/notification surface (passed to ProviderManager, every window, every ShellServices) | — |
| `WorkspaceManager` | The `Workspaces` list (dropdown source) + the live `WorkspaceRuntime`s; create/switch/reconfigure/dispose lifecycle | — |
| `FeatureManager` | Consumes the cached discovery index (`FeatureCatalog`); builds feature instances **per (Type, WorkspaceRuntime)** on demand; `EvictWorkspace` drops them on reconfigure/dispose | File-system contracts (those go to `FileSystemFeatureRegistry`) |
| `FeatureCatalog` | The discovery engine: a **disk-cached** index of which feature type implements which contract, so a normal launch loads **no** feature DLLs; assemblies load + activate **lazily** (first use or post-paint background warm-up). Cache is stamped with the app version and rebuilt only on an update | — |
| `FileMapManager`, `ExternalAppRegistry`, `WhisperModelManager`, `HostCapabilityService`, `MessageCenter`, `JumpListService` | Misc app-wide services | — |

**Global configs** (registered app-level, shared by every workspace): `ShellConfig` (theme), `WorkspacesConfig` (the workspace-list metadata; `ConfigName` is `"workcontexts"` on disk — compat), `FileMapConfig`, `ExternalAppsConfig`, `VoiceConfig`.

> ⚠️ The **AI persona** (`AiPersonaConfig`: assistant name + system prompt) is **per-`Workspace`**
> (persisted under `Contexts\<name>\ai-persona`, exposed as `Workspace.Persona`) — as are the
> ability→model assignments. Feature `IFeatureConfig`s are global unless marked
> `[WorkspaceScopedConfig]`. Don't conflate the scopes.

### Config versioning & migration

Every config (global or per-workspace) persists as `…\{configName}\config_{AssemblyVersion}.json`, so the filename records the version that wrote it. On load — `ConfigManager.Register` for global, `LoadFrom` for per-workspace — when the current-version file is absent but an older one exists, `ConfigManager` **migrates it forward** rather than discarding it:

1. The newest older `config_*.json` is loaded with a **lenient field-by-field carry-over**: unknown JSON fields are skipped and missing ones keep their type defaults, so additive and removed fields need no code.
2. A shape change the carry-over can't express (a rename or restructure) opts into **`IConfigMigration`** — a tiny one-method interface mirrored in `Features.Common` and `Providers.Common` (kept parallel so the layering rule holds; Core checks both). Its `MigrateFrom(previousJson, previousVersion)` runs right after the carry-over, with the raw old JSON in hand.
3. The result is rewritten under the current version and the stale files are deleted (**write-then-delete**, so a failed write never loses the prior data).

Migrated configs are tracked apart from brand-new ones (`GetMigratedConfigs` vs `GetDefaultedConfigs`). The first-run/update **setup wizard** (`SetupWizardViewModel.Build`) therefore re-asks for a global mandatory config only when it is genuinely new **or** its migrated data still fails the required-field check (`AreRequiredPropertiesSatisfied`) — never for information already on disk; the workspace/provider/model flow is skipped because the migrated per-workspace configs keep `IsWorkspaceConfigured` true. File-type mappings (`FileMapManager.SyncBundledDefaults`) follow the same spirit through a `_defaults.json` hash manifest: a changed bundled default refreshes mappings the user hasn't touched and leaves customized ones alone, fast-pathing when the bundle is unchanged.

**Reset.** Options → About offers a danger-styled **Reset Config** that, after a window-modal confirmation, wipes the entire `%APPDATA%\Smile\nexaflow` tree and relaunches (`App.ResetAndRestart` arms a write-suppressor, drops the single-instance mutex, then starts a `--reset` process that deletes the directory **before** init — lock-safe because the fresh process holds no handle) straight into first-run.

### Per-`Workspace` — shared, saved (one instance per named workspace)

A `Workspace` (`Core/Models/Workspace.cs`) is a named, themed, saved configuration. `Name`/`Color`/`Icon` and the default/last-session tabsets persist inline (in `WorkspacesConfig`); the shared services below are runtime-only (`[JsonIgnore]`), loaded once by `Workspace.EnsureSharedServicesLoaded`, with on-disk state under `…\Smile\nexaflow\Contexts\<Name>\` (folder name kept for compat):

| Member on `Workspace` | What it scopes | On disk |
|-----------------------|----------------|---------|
| `AiConfig` | The ability → provider/model **assignments** + configured columns. Shared, so an edit shows in every runtime on this workspace | `ai-abilities/` |
| `Persona` | The assistant persona (name + system prompt) | `ai-persona/` |
| `ProviderConfigs` | The provider configs (API keys / subscriptions) used to build provider instances | provider config folders |
| `RibbonService : RibbonLayoutService` + `RibbonChanged` | The shared ribbon layout. Saving raises `RibbonChanged`, which every window/runtime on this workspace observes to reload live | `ribbon.json` |
| `ConversationsDir` | Where this workspace's conversations are stored | `Conversations/` |
| workspace-scoped feature configs | Any `IFeatureConfig` marked `[WorkspaceScopedConfig]` (saved via `WorkspaceManager.SaveWorkspaceScopedConfig`) | `<configName>/` |
| `DefaultTabs` / `LastSessionTabs` | The tabset a fresh window opens with; the last session's tabset when it differed (a toast offers one-click restore) | inline in `workcontexts` |

### Per-`WorkspaceRuntime` — runtime (one per app/IPC launch; can have many windows)

A `WorkspaceRuntime` (`Core/Models/WorkspaceRuntime.cs`) points at one `Workspace` and owns the live per-session services, built by `WorkspaceManager.CreateWorkspace` / rebuilt in place by `SwitchWorkspace`/`ReconfigureWorkspace`:

| Member on `WorkspaceRuntime` | What it scopes |
|------------------------------|----------------|
| `Providers : ProviderSet` | The live provider instances **acquired from the pool** for this runtime (deduped by config process-wide); released when the runtime is reconfigured/disposed |
| `AiService : AIService` | The agent loop + AI-input-bar routing — resolves each `AiAbility` through the workspace's assignments + this runtime's providers; reads/writes the workspace's conversations |
| `ShellServices : ShellServices` | This runtime's **window + tab registry** (every window in the runtime). Stable for the runtime's life — a workspace switch reconfigures internals, not this object |

The `IShellServices` / `IAIService` injected into a feature are the **active runtime's** instances (`FeatureManager` resolves them per `WorkspaceRuntime`). So "open a tab" and "ask the AI" always act within exactly one runtime.

### Per-window / per-tab

- A **window** (`IWindowHost`) registers into its runtime's `ShellServices`. Several windows can share one runtime (tear-off / "open in new window"); switching the dropdown reconfigures the runtime **in place** for all of its windows (`SwitchWorkspace` — tabs close, providers/AIService rebuilt against the target workspace).
- A **`Page`** (tab) is built by `FeatureManager.CreateTab(pageKind, runtime, params)` → the matching `IPageRegistration.CreatePageDefinition`. Its ViewModel + content are realized lazily by `ContentFactory` and live for the life of the tab.

### Per-feature (assembly)

- An **`IFeatureConfig`** is by default a **single, app-level instance per assembly** — registered with `ConfigManager` when its owning assembly is first **activated** (lazily, on first use or during warm-up), then never rebuilt; persisted at `…\Smile\nexaflow\<ConfigName>\`, outside `Contexts\`. Marking the config class `[WorkspaceScopedConfig]` opts it into **per-`Workspace`** persistence instead (one instance per workspace, under `Contexts\<Name>\`).

---

## Module Responsibilities

### Nexaflow.Core

The shell host. Owns the window chrome, tab strip, ribbon bar, breadcrumb bar, and the AI input row. Nothing in Core knows how to _render_ any individual page — it only knows how to _host_ them.

| File | Responsibility |
|------|----------------|
| `App.xaml.cs` | Startup (`InitializeApp`): global-config + provider-**assembly** load, `WorkspaceManager.Initialize` (saved workspaces only), feature discovery, first window (a fresh `WorkspaceRuntime`). Also the windowless `--prestart` daemon and single-instance / new-window IPC (each IPC launch = a new runtime) |
| `Models/Workspace.cs` | The saved, shared workspace config (holds `AiConfig`/`Persona`/`ProviderConfigs`/`RibbonService`+`RibbonChanged`/conversations dir + default/last-session tabsets) — see [Ownership & Lifetime](#ownership--lifetime) |
| `Models/WorkspaceRuntime.cs` | The runtime (points at a `Workspace`; holds `Providers`/`AiService`/`ShellServices`) |
| `Services/WorkspaceManager.cs` | Singleton: the `Workspaces` list + live `WorkspaceRuntime`s; `Initialize`/`CreateWorkspace`/`SwitchWorkspace`/`ReconfigureWorkspace`/`NotifyWindowClosed`, `Add`/`Clone`/`Rename`/`Remove`/`DeleteWorkspace`, orphaned-data quarantine |
| `ProviderManager.cs` | Singleton: loads provider **assemblies** + records provider/config **types**; `LoadProviderConfigs(dir)` + the ref-counted pool `AcquireProviderSet`/`ReleaseProviderSet`. Instances are model-bound (capability per config + execution per config+model); execution instances warm/cool with their pool lifetime |
| `ProviderSet.cs` | A runtime's acquired provider **instances** + the workspace's configs + assembly map + pool keys |
| `Services/AIService.cs` | `IAIService` impl — **per-`WorkspaceRuntime`**: provider registry, ability→model resolution, the agent loop, conversation history |
| `AI/AiConfig.cs` | Per-workspace AI ability config (`Columns` + ability→column `Assignments`); rendered by `AiAbilityGridControl` |
| `MainWindow.xaml.cs` | Wires shell commands; creates `ShellViewModel`; handles ESC/breadcrumb clicks |
| `ViewModels/ShellViewModel.cs` | Per-window VM: pane/split layout, overlays, AI input routing, toasts, workspace lifecycle commands; `SelectWorkspace` (in-place switch, blocked while a modal overlay is open) |
| `Services/ShellServices.cs` | `IShellServices` impl — **per-`WorkspaceRuntime`** (not an app-level singleton). Owns that runtime's window + tab registry, tearoff/cross-window moves, file watching, session capture/restore, window positioning |
| `FeatureManager.cs` | Singleton (`FeatureManager.Instance`). Delegates discovery to `FeatureCatalog` (a disk-cached index — **no** eager DLL loads on the common launch); resolves `IPageRegistration`/config/handler **types** lazily and builds instances per `WorkspaceRuntime` with scoped `IShellServices` + `IAIService` (`EvictWorkspace` drops them on reconfigure). File-system contracts are **not** here (see below) |
| `Services/FeatureCatalog.cs` | The discovery engine behind `FeatureManager`. Persists the per-assembly type index to `…\Smile\nexaflow\discovery\catalog.json` (stamped with the app version); a version match is trusted with no assembly loads, a mismatch (an app update) triggers one full rescan. Resolving a type loads + **activates** its assembly on demand (registering that assembly's global configs / archive handlers / theme contributions). A post-paint background warm-up activates the rest |
| `Services/StartupTimings.cs` | Opt-in startup profiler (`--timing` flag / `NEXAFLOW_STARTUP_TIMING=1`); writes a milestone breakdown + `FIRST_WINDOW_MS` / `WINDOW_ON_DEMAND_MS` to stderr. Zero cost when off |
| `Services/FileSystemFeatureRegistry.cs` | Discovery + matching for `IFileAction`/`IFolderAction`/`IFileCreateAction`/`IFolderViewlet` (deliberately split out of `FeatureManager`) |
| `Services/RibbonLayoutService.cs` | Serialize/deserialize ribbon items — **per-workspace** (constructed with the workspace's own folder, shared by its runtimes), not a single global `ribbon.json`. Bundled defaults live in `Ribbon/default-ribbon.json` (`LoadDefaults`) |
| `Services/WindowManager.cs` | Static DPI/monitor/cursor maths only — tab/window lifecycle moved to `ShellServices` |
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
| `IPageRegistration.cs` | Interface a feature implements to advertise one page kind: `PageKind` + the cheap `CreatePageDefinition(params)` (plus optional `Parameters`, `CanBeContextItem`, `CreatePageDefinitions`). Each impl also exposes `static string StaticPageKind` so `FeatureManager` discovers it by reflection |
| `OpenPageRequest.cs` | `(PageKind, PageParams)` carrier for shell-level open commands (breadcrumb follow-links). Core-internal MVVM glue — [arch_review_2026-07.md §C1](arch_review_2026-07.md) tracks relocating it to Core |
| `IPageView.cs` | Thin shell-lifecycle contract implemented by tab `UserControl`s: exposes `IPageViewModel? ViewModel` and `Reinitialize` |
| `IPageViewModel.cs` | AI pipeline contract implemented by ViewModels: `GetContext`, `GetClientTools`, `GetContextObject` |
| `IShellServices.cs` (under `Services/`) | The **active `WorkspaceRuntime`'s** shell handle injected into feature code (one impl per runtime, not an app singleton): `OpenTab`/`CloseTab`/`FindTab`, `QueueBackgroundTask`, `ShowError`/`ShowNotification`/`ShowPrompt`/`ShowConfirmation`, `ShowOverlay`/`CloseOverlay` (feature-defined shell-modal overlays), `PinToRibbon`, `DiscoverImplementations<T>`. Owns UI-thread access for features: `WatchFile(path, onChanged)` (shared, deduped, UI-marshalled, lifecycle-managed file watching → `IFileWatch`) and `RunOnUiAsync(...)` (marshal work to the workspace UI thread). Features use these instead of `Application.Current.Dispatcher` |
| `IFileWatch.cs` (under `Services/`) | Handle from `WatchFile`: `Enabled` (hold + coalesce while false, flush on re-enable), `Dispose` to unwatch |
| `IAIService.cs` | Per-`WorkspaceRuntime` AI service: handler scoring/disambiguation, the `RunAgentAsync` agent loop, conversation load/save, analysis + artifacts |
| `IBackgroundTask.cs` | Self-contained background work (`Description` + `RunAsync`) handed to `IShellServices.QueueBackgroundTask` |
| `IFeatureConfig.cs` | Marks a POCO as a config section; discovered and instantiated by `FeatureManager` |
| `IContext.cs` / `FileSystemContext.cs` | Typed context objects offered by ViewModels via `IPageViewModel.GetContextObject()` |
| `ClientTools/*.cs` | Agent-loop contracts: `IClientTool`/`DelegateClientTool`, `ClientToolParameter`, `ToolSafety`, `ToolCall`, `ToolResult`, `ClientPlan`, `IToolApprovalCoordinator`, the shared `ToolArgs` argument reader. **Also** currently holds the Core-only wire-protocol parser `ClientBlockParser` + `ParsedAssistantTurn` — [arch_review_2026-07.md §C1](arch_review_2026-07.md) tracks moving those two to Core |
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

**The contract is deliberately lowest-common-denominator.** `CompleteAsync` is structured-in /
**string-out**: providers stream internally (via the shared `LlmStreamRunner` — activity lifecycle,
clean cancellation, hung-stream timeout, transient retry) but return one accumulated string; there is
no token-streaming surface on the interface. Tool use is **prompt-convention**: the agent loop asks
the model for fenced ```` ```client_tool ```` blocks and parses them out of the text — no provider
sends native function-calling schemas. This keeps every backend (including local Ollama models and
the pipe-based Aria) behaviourally identical and the agent loop provider-agnostic. The accepted
trade-offs: no live token rendering, no usage/cost metadata, and tool reliability rides on
instruction-following. The future escape hatches, when appetite exists, are an optional
`CompleteStreamingAsync` overload plus usage fields on `LlmResponse`, and a native-tools capability a
provider can opt into — both additive.

All providers implement `ILlmProvider` (and optionally `IDisposable`/`IAsyncDisposable`) from `Providers.Common`. `ProviderManager` loads the plugin assemblies at startup and records the provider/config **types** they expose — it does not create instances eagerly. Provider **instances** are pooled process-wide and ref-counted: `AcquireProviderSet` hands each `WorkspaceRuntime` one model-agnostic *capability* instance per (provider type + config) for model enumeration, plus one model-bound *execution* instance per ability-grid assignment (warmed on first acquire, cooled + disposed when its last ref drops — `ReleaseProviderSet`). Provider **configs** (API keys / subscriptions) live on the saved `Workspace` (`Contexts/<name>/<configName>/`), so two workspaces can hold different subscriptions for the same provider while identical configs share one pooled instance. The acquired set lives on `WorkspaceRuntime.Providers` and registers into that runtime's `AIService`.

| Assembly | Backing service |
|----------|----------------|
| `Nexaflow.Providers.Aria` | Windows named-pipe to the local Aria service. Two layers: `AriaNamedPipeClient` (raw JSON framing) and `AriaClientService` (one-shot request/reply, parses `#focustab` instructions from responses). |
| `Nexaflow.Providers.Claude` | Anthropic Claude API over HTTPS. |
| `Nexaflow.Providers.Gemini` | Google Gemini API over HTTPS. |
| `Nexaflow.Providers.OpenAI` | OpenAI API over HTTPS. |
| `Nexaflow.Providers.Ollama` | Ollama REST API for locally-hosted models. |

---

## Key Data Models

```
Page  (ObservableObject)
  ├── Title, Icon, IsActive             (observable — drives tab strip UI)
  ├── PageKind: string?                 (e.g. "Search", "Projects")
  ├── PageParams: Dict?                 (current params, kept in sync by the page via IShellServices)
  ├── Breadcrumbs: ObservableCollection<BreadcrumbSegment>
  ├── ContentFactory: Func<UserControl>? (set by IPageRegistration.CreatePageDefinition; lazy-invoked on first activation)
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

Implement in your feature assembly. `FeatureManager` discovers it **automatically** via the cached `FeatureCatalog` index (no code-registration step); its assembly is loaded and the registration built **lazily** on first use. It injects constructor dependencies per `WorkspaceRuntime`: any `IFeatureConfig` declared in the same assembly, the scoped `IShellServices`, and `IAIService`. Expose a `static string StaticPageKind` — the catalog reads the page kind from it without instantiating (and caches it).

`CreatePageDefinition` must stay **cheap and side-effect-free** — callers build a definition speculatively just to read its `Title`/`Icon` (e.g. for a menu) and may discard it. Construct the view-model and view **inside the `ContentFactory` closure** so they're built only when the tab is first shown. Optionally advertise `Parameters` (so the shell/AI can describe how to open the page) and set `CanBeContextItem`/`CreatePageDefinitions` for pages offered in the AI "add context" and ribbon-editor menus.

```csharp
public sealed class MyPageRegistration(MyConfig config, IShellServices shellServices) : IPageRegistration
{
    public static string StaticPageKind => "MyFeature";   // read by FeatureManager via reflection
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
        [new("path", "File to open in My Feature.", Required: false)];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        return new Page
        {
            Title          = "My Feature",
            Icon           = "🔧",
            PageParams     = pageParams,
            Breadcrumbs    = { new BreadcrumbSegment { Label = "My Feature" } }, // get-only collection
            // built lazily on first activation — keep all heavy work in here, not above
            ContentFactory = () => new MyView(new MyViewModel(path, config, shellServices))
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

Implement to handle typed input before it reaches the LLM. Handlers are **auto-discovered** via the
`FeatureCatalog` index and built per `WorkspaceRuntime` — there is **no registration step**. Scope is
expressed inside `CanProcess`: the active page's ViewModel is passed in, so a page-scoped handler is
just a type check returning 0 for pages it doesn't own, while an app-wide handler scores on the input
alone. `Symbol` claims a single-character prefix for exact routing; otherwise scores compete and
`IAIService.DisambiguateToolSelection` breaks ties.

```csharp
public sealed class MyQueryHandler : IQueryHandler
{
    public string Description => "Handles my feature's input";
    public string? Symbol => null;                           // optional prefix character

    public float CanProcess(string input, IPageViewModel? pageVm = null)
        => pageVm is MyViewModel ? 0.9f : 0f;                // page-scoped via the type check

    public async Task<string?> ProcessAsync(string input, IPageViewModel? pageVm = null)
    {
        if (pageVm is not MyViewModel vm) return "No active tab.";
        await vm.DoSomethingAsync(input);
        return null;   // null = handled silently; string = shown in AI Chat
    }
}
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
shellServices.ShowOverlay(new MyOverlayVm());   // feature-defined shell-modal overlay; CloseOverlay() to dismiss
```

A feature can show its own **shell-modal overlay**: pass any view-model to `ShowOverlay`, and the single overlay host in `MainWindow` renders it via a `DataTemplate` matched on the VM's type — ship that template in a `ResourceDictionary` advertised through your `IThemeContribution`. Implement `IShellOverlay` on the VM to opt into backdrop-click dismissal. The built-in modals (Options, Manage-AI, confirmation, prompt) ride the same host.

---

### `IFeatureConfig` — Persisted settings

Implement a plain POCO. `FeatureManager` discovers it via the cached `FeatureCatalog` index and instantiates + registers it when its owning assembly is first **activated** (lazily, or during the post-paint warm-up), making it available for injection into `IPageRegistration` constructors. The Options panel renders a property grid for free using reflection and the config attributes. (Because the panel lists **every** feature's config, opening it forces a full activation of any not-yet-loaded features.)

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

## Elevation / Privilege Bridge

Nexaflow's host process runs **non-elevated** (`asInvoker`) — always. When a feature needs an admin
action (kill a protected process, write HKLM, control a service, set a machine-scope environment
variable), it calls **`IShellServices.RunElevatedAsync(ElevatedRequest)`**:

1. `Services/Elevation/ElevatedBridgeLauncher.cs` (Core) opens a private message-mode named pipe and
   launches `PrivilegeBridge.exe` **elevated** — one UAC prompt — authenticating the connection with a
   one-time token.
2. The request DTO ships over the pipe (never on the command line); the bridge dispatches to the
   matching `IElevatedOperation` (`Nexaflow.PrivilegeBridge/Operations/` — Registry, Service, Process,
   Env), replies with an `ElevatedResult`, and the bridge process exits. A declined UAC prompt surfaces
   as a typed `ElevatedErrorKind`, not an exception storm.

**Layering:** `Nexaflow.Elevation.Contracts` is a pure DTO leaf (`ElevatedRequest`/`ElevatedResult`/
`Operations`/pipe framing) referenced by `Features.Common` (for the `RunElevatedAsync` signature), Core
and the bridge. `Nexaflow.PrivilegeBridge` is a standalone `requireAdministrator` exe **outside** the
in-process layering — Core's csproj copies it beside the app (`ReferenceOutputAssembly=false`, the
`CopyPrivilegeBridge` target); features never see it.

**To add an elevated operation:** define the DTO in `Elevation.Contracts`, implement an
`IElevatedOperation` in the bridge, call it via `RunElevatedAsync`. **Never** `Process.Start` with
`runas` from a feature (a CLAUDE.md hard rule). Consumers today: Processes (kill/priority), SystemInfo
(services + machine env vars), WindowsRegistry (elevation-gated writes).

---

## Core Flows

### Startup

```
App.OnStartup
  → new BackgroundActivityManager()                         ← the one shared activity surface
  → InitializeApp(activityManager):
      → ConfigManager.Initialize(%AppData%\Smile\nexaflow)   ← base data path
      → register GLOBAL configs: ShellConfig (+ apply theme), WorkspacesConfig,
        FileMapConfig, ExternalAppsConfig, VoiceConfig
      → ProviderManager.Initialize(activityManager)         ← records the shared ActivityManager
      → for each saved workspace: temp AiConfig ← ConfigManager.LoadFrom(workspace dir)
            (need each workspace's columns to know which provider DLLs to load)
      → ProviderManager.LoadConfigured(⋃ AssemblyFileName across all workspaces' AiConfigs)
      → WorkspaceManager.Initialize(wcConfig)               ← loads the saved Workspaces list ONLY (no runtimes)
      → FileMapManager / ExternalAppRegistry init
      → FeatureManager.RegisterFeatures()                   ← FeatureCatalog.Initialize: load the cached
            discovery index (no feature DLLs loaded) or, after an app update, one full rescan. Feature
            assemblies load + activate lazily later (first use, or the post-paint background warm-up)
      → WhisperModelManager + HostCapability probe, JumpList, single-instance IPC listener
  → pick startup Workspace (Workspaces[0], or the one named by --context "Name")
  → WorkspaceManager.CreateWorkspace(workspace)             ← workspace.EnsureSharedServicesLoaded +
        bootstrap: AcquireProviderSet(pool) → new AIService(runtime, Conversations\) →
        register providers → LoadAbilityConfig → ShellServices; returns the WorkspaceRuntime
  → new MainWindow(activityManager, startupRuntime)         ← window bound to ONE runtime
      → ShellViewModel(runtime)   (CurrentRuntime = runtime)
          → ribbon binds runtime.Workspace → RibbonViewModel.SetWorkspace → Load() (shared layout)
          → OpenDefaultTabs()
  → win.Show()

  (Each subsequent app/IPC launch = a NEW WorkspaceRuntime. Tear-off / "open in new window" reuse the
   SAME runtime. --prestart launches the windowless resident daemon: InitializeApp runs, NO runtime is
   created; a later click / JumpList signal opens a window — a new runtime — via the IPC listener.)
```

### Opening a tab

```
User clicks ribbon button
  → RibbonControl.InternalRibbonActionCommand → ShellViewModel.OpenRibbonItem(item)
  → _shellServices.OpenTab(item.PageKind, item.PageParams)   ← persisted items carry only PageKind/params
  → page.GetOrCreateContent()               ← ContentFactory invoked on first activation
  → CurrentPage = page, breadcrumbs updated

Feature code calls shellServices.OpenTab("PageKind", params, callerPage)
  (shellServices = the caller's WorkspaceRuntime instance)
  → ShellServices resolves target window from callerPage / focused window
  → Searches this runtime's windows for an existing matching tab
  → If found: move to target window if needed → Reinitialize(params) → activate
  → If not found: FeatureManager.CreateTab(pageKind, runtime, params)   ← internal; returns a Page
      → matching IPageRegistration.CreatePageDefinition(params)
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
      → ProjectDetailTabRegistration.CreatePageDefinition(params)
  → IWindowHost.AddTab(tab)
```

### Opening a viewer tab from a file action

```
User selects file in file browser → clicks action button
  → FileActionManager.PerformAction(action, paths)
  → e.g. ShowMarkdownAction.PerformAction(path)
      _shellServices.OpenTab("Markdown", { "path": filePath })
  → ShellServices → MarkdownTabRegistration.CreatePageDefinition(params) → tab opens
```

### Ribbon persistence cycle

```
Save:  RibbonItems changes → CollectionChanged → RibbonViewModel.Save()
         → workspace.RibbonService.Save(items) → workspace.RaiseRibbonChanged()
         → every other window/runtime on this workspace reloads its items live
         (a closing window unhooks via RibbonViewModel.Detach — the Workspace outlives its windows)

Load:  workspace bound → RibbonViewModel.SetWorkspace → workspace.RibbonService.Load()
         items are pure metadata (PageKind + params); they open via the window's
         OpenRibbonItem command — no per-window delegate reattachment needed.
         Bundled defaults: Ribbon/default-ribbon.json via RibbonLayoutService.LoadDefaults
```

---

## Architectural Findings

Point-in-time findings do **not** live in this document — an embedded findings list rots invisibly
(the 2026-06 copy of this section was ~60% stale by 2026-07: three of its six items had already been
fixed or had moved files). Current findings, each with status tracked inline:
[arch_review_2026-07.md](arch_review_2026-07.md) (supersedes
[arch_improvements.md](arch_improvements.md)). Per-component product status (what exists, what is
tested, what is AI-ready) lives in the product tree — `.product/tree.json`, queried via the
product-folder skill.