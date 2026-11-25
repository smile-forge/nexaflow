# Nexaflow Architecture

## Table of Contents

1. [Solution Layout](#solution-layout)
2. [Ownership & Lifetime](#ownership--lifetime)
3. [Module Responsibilities](#module-responsibilities)
4. [Key Data Models](#key-data-models)
5. [Extensibility Points](#extensibility-points)
6. [Elevation / Privilege Bridge](#elevation--privilege-bridge)
7. [Core Flows](#core-flows)
8. [Large-file reading](#large-file-reading)
9. [Installer](#installer)
10. [Architectural Findings](#architectural-findings)

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
| Contracts | `Nexaflow.Features.Common` | ALL feature-facing interfaces + small DTOs. Features and Core meet only here. Names no UI toolkit (no WPF): what a contract hands out to be shown is a model, drawn by a `[ViewFor]` view; `ArchitectureRulesTests` keeps it so |
| Features | `Nexaflow.Features.*` (~30, + 3 `Compressed.*` codec backends) | One assembly per feature; reflection-discovered, lazily loaded |
| Input vocabulary | `Nexaflow.Interaction` | WPF-free `net10.0` leaf the contracts speak: `KeyModifiers`, `IDragPayload` + `DragKinds`, `IDropTarget`. Beneath `Features.Common` rather than in it, so a shared control can take a drop without seeing `IShellServices` |
| Shared leaves (non-contract) | `Nexaflow.Visuals.Common` (controls/converters/formatters), `Nexaflow.Visuals.Text` (markdown rendering), `Nexaflow.Visuals.Terminal` (terminal input logic), `Nexaflow.IO.Common` (encoding/line-endings, glob, hashing, base64 + `IStreamCodec`/`IArchiveHandler` codec contracts, the `VirtualFileSystem` over archives and mounts, the remote-location contracts and each runtime's `LocationFileSystem`, text index/transforms, file watching/splitting), `Nexaflow.IO.Terminal` (PTY host), `Nexaflow.Syntax` (tree-sitter engine) | Concrete shared code a feature may reference; never references Core or a feature |
| Providers | `Nexaflow.Providers.Common` and `Nexaflow.Providers.OpenAICompatible` (the chat-completions wire format OpenAI and xAI share), `Nexaflow.Providers.Local` (what a provider whose models run here offers) + one project per backend (Claude, Gemini, OpenAI, xAI, Ollama, Aria, Whisper, AiHost) | Loaded by file name at runtime; never compile-time referenced |
| Elevation | `Nexaflow.Elevation.Contracts` (pure DTO leaf) + `Nexaflow.PrivilegeBridge` (separate elevated exe) | The admin-action trust boundary — see [Elevation / Privilege Bridge](#elevation--privilege-bridge) |
| Languages | `Nexaflow.Language.<code>` under `src/Nexaflow.Languages/` — one resource-only pack per language, gathered from every project's `Localization/<code>/` | Shipped to `Languages\` and loaded by path, never referenced — see [localization.md](localization.md) |
| Initiatives | `Nexaflow.Services.Initiatives` + `Nexaflow.Services.Initiatives.Cli` (`nfi`) | WPF-free backend for the product tree, the knowledge graph, `SnaplinkValidator` and graph edit plans — never references WPF, Core or a feature. The CLI runs the same validator headlessly for the installer's build gate (exit 1 = broken links) — see [product-graph.md](product-graph.md) |
| Tests | `Nexaflow.Tests.*` (one suite per subject) + `Nexaflow.Tests.Fixtures` | See [testing.md](testing.md) |

`Nexaflow.Syntax` owns `TreeSitterLanguages` (extension → grammar), so a headless caller resolves a grammar without
WPF. The tree-sitter runtime **and** every grammar compile from pinned submodules (`external/tree-sitter-dotnet-bindings`
+ `external/tree-sitter-xml`, one row per language in `tools/tree-sitter-grammars.props`) — not the `TreeSitter.DotNet`
package, whose prebuilt natives went stale. `.xaml` parses with the xml grammar under its own id, so the extractor can
read WPF meaning (`x:Class` / `x:Name` / `x:Key` / handlers). See [externals.md](externals.md).

**Dependency rules** (mechanically enforced by `Nexaflow.Tests.Features.Architecture/Architecture/ArchitectureRulesTests`
and `Nexaflow.Tests.Providers/ArchitectureRulesTests` — a violation is a red build):

- Features depend on `Features.Common` + the shared leaves only — never on Core, never on each other.
  (The `Compressed.*` codec backends reference only `IO.Common`, where the codec contracts live —
  not even the Compressed feature; they're discovered at runtime.)
- Providers depend on `Providers.Common` only — never on Core, never on each other. A provider whose models run on this
  machine also references `Providers.Local` and the AI host SDK; `Providers.Common` holds nothing of local models, so a
  cloud provider — and a cloud-only user — carries none of it (`ArchitectureRulesTests`).
- `Nexaflow.Core` depends on all features and providers (so they ship) but never instantiates feature
  view/view-model types — all tab/viewlet creation goes through `FeatureManager`, all provider
  instances through `ProviderManager`'s pool. Features talk back only via `IShellServices`.
- Shared non-contract code goes in `Nexaflow.Visuals.*` / `Nexaflow.IO.*` / `Nexaflow.Media` / `Nexaflow.Syntax`,
  not `Features.Common`; `Elevation.Contracts` is the fifth shared leaf (referenced by `Features.Common`
  for the `IShellServices.RunElevatedAsync` signature). `Nexaflow.Media` holds media codecs Nexaflow carries as its
  own memory-safe source rather than wraps as native libraries, each in a folder with its licence.

---

## Ownership & Lifetime

There are nesting scopes. **Getting a thing's scope wrong is the easiest way to introduce a bug**, so this is the canonical reference for what is central to the process vs tied to a `Workspace` (saved config) vs a `WorkspaceRuntime` (runtime) vs per-feature vs per-window/tab.

The model has three lifetimes, nested. The **session** (`ShellHost`, `Services/ShellHost.cs`) is the process — from sign-in when Nexaflow starts with Windows, otherwise from the first window to the last — and owns everything every workspace shares: one person works across several contexts, so the file system, its watches, the AI host and the inbox are the session's. A **`Workspace`** (`Models/Workspace.cs`) is the saved, shared configuration shown in the dropdown — name/colour/icon, default + last-session tabsets, AI ability grid, persona, ribbon layout, provider configs, conversations. A **`WorkspaceRuntime`** (`Models/WorkspaceRuntime.cs`) is that workspace live: **at most one per `Workspace`** (`WorkspaceRuntimes`), shared by every window that shows it.

A launch of a workspace already live opens a window on its runtime; tear-off and "open in new window" join it too. Switching a window's dropdown moves *that window* to the other workspace's runtime and leaves its old workspace's other windows as they are. A runtime whose last window goes turns **dormant** — its providers back in the pool, so local models nothing else uses unload, while its configuration, assistant and feature instances stay — and the next window to come back resumes it at once, its tabs as they were. It ends after `ShellConfig.DormantWorkspaceMinutes` (five by default), or at once when its workspace is deleted. One whose background jobs still run stays active until they finish: the work is the person's, and needs the workspace's models.

> **Frozen names:** the on-disk and IPC strings `workcontexts` (config name), `Contexts\` (data folder) and
> `--context` (command-line/IPC flag) are compat contracts and never change. `Profile` and `WorkContext` are never
> type or member names.

### Central — the session (one per process)

`ShellHost` builds the session in three stages — what the first window needs (its constructor), what follows the first frame (`FirstWindowDrawn`), what runs in the background meanwhile (`StartBackground`) — and ends it in reverse (`DisposeAsync`): every workspace runtime, the watches, the models held ahead, the AI host. It names no UI toolkit: the UI thread (`IUiThread`), the clipboard and the folder watcher's window come from the app in a `ShellPlatform`, and `SessionLayerWpfTests` keeps the session layer so. What it has not yet handed over is still reached as a `*.Instance` singleton; `CoreSingletonRatchetTests` counts those left.

| Singleton | Owns | NOT responsible for |
|-----------|------|---------------------|
| `ConfigManager` | Base data path; **global** config registry. `Register(cfg,name)` = global config; `LoadFrom`/`SaveTo(dir,…)` = per-workspace config in that workspace's folder; what rules on a setting the app does not decide alone (`AddRulings`). One per session, made by `ShellHost` over this launch's folders (`AppDataFolders` — `%AppData%` and `%LocalAppData%\Smile\nexaflow`, or the root `NEXAFLOW_CONFIG_DIR` sets apart) and handed by constructor to whatever reads or writes a config: the feature and provider systems, the saved workspaces — which list each `Workspace` in it, so its folders and its own configs resolve — every runtime (`RuntimeSessionServices`) and window (`WindowServices`), whose Options, Configure and wizard view-models are given it | Writing down a stored value it could not read — it logs it as a fault (`ILogger<ConfigManager>`), which the crash log keeps |
| `ShellLogging` (`Services/Logging/`) | The process's logs: one `ILoggerFactory` (Microsoft.Extensions.Logging) over the destinations each entry reaches, each keeping to its own level, file and format — the crash log (`CrashLogProvider`: `Error` and above, always, a file a day `crash-yyyy-MM-dd.log` in the config root, kept 10 days, 8 MB a day, a burst of identical faults written as five traces and a count), the session's log (`SessionLogProvider`: `Information` and above while Options → Shell → Enable safety logging is on, one `shell-<time>-<pid>.log` a session under `%LocalAppData%\Smile\nexaflow\logs`, which the AI host logs beside; what startup logs before the shell config loads is held in memory, then written out or dropped), the binding errors WPF reports, under `--uiTest` only (`BindingErrorLogProvider` → `binding-errors.log` in the config root, fed by the app's `BindingErrorTrace`), and the startup profiler, under `--timing` only (`StartupTimings`, timing the milestones logged through `StartupMilestones`). Made first by `App.OnStartup` from `AppDataFolders` — before the exception hooks, the single-instance guard and every startup step — held by the app and its fault handlers, and handed to `ShellHost` (`ShellHostOptions.Logging`), which hands each service it makes an `ILogger<T>`, or the factory, by constructor; every window (`WindowServices.Logging`) and runtime (`RuntimeSessionServices.Logs`) is given it, and a feature whose constructor asks for an `ILogger<T>` is given one by the feature injection | Whether a fault the UI thread's handler caught is still worth handling — `DispatcherFaultPolicy` decides that; the logs only record |
| `ProviderManager` | Loads provider **assemblies** by file name; records provider/config **types**; owns the shared `ActivityManager`; owns the **global ref-counted provider instance pool** (`AcquireProviderSet`/`ReleaseProviderSet`). Each `ILlmProvider` is **model-bound** (model injected via `ProviderModel`): one *capability* instance per (type+config) for model enumeration, plus one *execution* instance per (type+config+**model**) the grid assigns — creating one loads nothing; cooled + disposed on last release. One per session, made by `ShellHost` with the app-wide services a provider may take, and handed to every window (`WindowServices`) and runtime (`RuntimeSessionServices`; the runtime carries it as `WorkspaceRuntime.ProviderPool`) | Holds provider **configs** — those live on the saved `Workspace` |
| `ModelResidency` | Which pooled models stay loaded: those `ModelPreloadConfig` (**global**) loads ahead, warmed after the first window draws; any other let go once idle. Counts each use in `ModelUseHistory` — **machine-local**, `model-use.json` in the AI folder beside the models, not per workspace: the models are loaded once for every workspace, and what is worth loading ahead depends on this machine | Loading a model before first paint |
| `BackgroundActivityManager` | The one activity/notification surface (passed to ProviderManager, every window, every ShellServices) | — |
| `WorkspaceManager` | The `Workspaces` list (dropdown source): saved workspaces — add, clone, rename, delete, persist. Made by the session over the saved `WorkspacesConfig` and handed to every window (`WindowServices.SavedWorkspaces`) and runtime (`RuntimeSessionServices`), and through them to the Options and Configure pages by their view-models | Live runtimes (`WorkspaceRuntimes`) |
| `WorkspaceRuntimes` | The live workspaces: one runtime per `Workspace`, opened, resumed, made dormant and ended | Building a runtime (`WorkspaceRuntimeFactory`) |
| `WorkspaceRuntimeFactory` | Builds every runtime from the session's services (`RuntimeSessionServices`), and ends those still running when the session ends | — |
| `FolderActivity` | The session's file and folder watches — one per path, however many workspaces follow it — and its busy folders, so a folder being changed shows its wait in every workspace | — |
| `FeatureManager` | The feature system, made by the session over its `FeatureCatalog`, whose index it builds as it is made: holds the features' global configs and the app-wide tools they are given (`ProvideTool`), and builds feature instances on demand, kept in each runtime's `FeatureScope`. Handed to every window (`WindowServices`) and runtime (`RuntimeSessionServices`; the runtime carries it as `WorkspaceRuntime.FeatureManager`), and through them to what opens pages, offers handlers or lists configs — an Options page by its view-model (`ConfigPageModels`) | File-system contracts (those go to `FileSystemFeatureRegistry`) |
| `FeatureCatalog` | The discovery engine, one per session (`ShellHost.Catalog`): a **disk-cached** index of which feature type implements which contract, so a normal launch loads **no** feature DLLs; assemblies load + activate **lazily** (first use or post-paint background warm-up). Cache is stamped with the app version **and** the feature-DLL set, so any changed assembly forces a rescan. **Debug builds bypass the cache entirely** (always rescan, never write, stale file deleted), so a full scan also eagerly activates every feature — never clear it by hand while developing | — |
| `LanguageManager` | The UI language: installed packs listed by file name, the active pack + English loaded lazily by path, the merged string table behind `Str` / `{loc:Str}`, and the help pages the Help pane reads. The session makes it, points `Str` at it, and hands it to every window (`WindowServices`) and runtime (`RuntimeSessionServices`). A change restarts the window like a theme — see [localization.md](localization.md) | Theme resources (`ThemeManager`) |
| `HelpLibrary` | The help pages, read through the `LanguageManager`. The Help page's registration is given it by the feature injection (an app-wide tool, `FeatureManager.ProvideTool`); every runtime's assistant gets the tools that read it (`RuntimeSessionServices.HelpTools`) | — |
| `ExternalDependencyRegistry` | The third-party components features declare (`IExternalDependency`), probed off the UI thread and cached; each finished probe announced on the session's `IUiThread`. A declaration of the session's own takes what it is judged from in its constructor — what Nexaflow's own models need, the AI host's reading of its runtime (`AiHostReading`) — and the registry builds it with the services the session made it with. Handed to every window — for About and the panel a page that could not be made is drawn with — and every runtime's `ShellServices`, which answers a feature's `GetDependencyStatus` | Installing a component |
| `PageViews` | The view each open page is drawn with, made on first show and kept until its tab closes, so a tab moved between panes or windows keeps it. Made by the app (`ShellPlatform.Views` — only the app knows the views): every window's panes draw with it, every runtime's `ShellServices` lets a closed page's go | The page's view-model (the `Page` holds it) |
| `FileMapManager`, `ExternalAppRegistry`, `MessageCenter`, `JumpListService` | Misc app-wide services | — |
| `ProviderManager` (with its `ModelAssetStore`) | Provider plugins and their pooled instances; the files local models need, under `%LOCALAPPDATA%\Smile\nexaflow\ai` (`LocalAiFolder`; `NEXAFLOW_AI_DIR` moves it), verified against each provider's catalog and downloaded with resume by the AI host SDK's store | — |

**Global configs** (registered app-level, shared by every workspace): `ShellConfig` (theme, language), `WorkspacesConfig` (the workspace-list metadata; `ConfigName` is `"workcontexts"` on disk — compat), `FileMapConfig`, `ExternalAppsConfig`. The models a voice conversation listens and speaks with are the workspace's: the ability grid's Speech In and Speech Out rows.

> ⚠️ The **AI persona** (`AiPersonaConfig`: assistant name + system prompt) is **per-`Workspace`**
> (persisted under `Contexts\<name>\ai-persona`, exposed as `Workspace.Persona`) — as are the
> ability→model assignments. Feature `IFeatureConfig`s are global unless marked
> `[WorkspaceScopedConfig]`. Don't conflate the scopes.

### Config versioning & migration

Every config (global or per-workspace) persists as `…\{configName}\config_{AssemblyVersion}.json`, so the filename records the version that wrote it. On load — `ConfigManager.Register` for global, `LoadFrom` for per-workspace — when the current-version file is absent but an older one exists, `ConfigManager` **migrates it forward** rather than discarding it:

1. The newest older `config_*.json` is loaded with a **lenient field-by-field carry-over**: unknown JSON fields are skipped and missing ones keep their type defaults, so additive and removed fields need no code.
2. A shape change the carry-over can't express (a rename or restructure) opts into **`IConfigMigration`** — a tiny one-method interface mirrored in `Features.Common` and `Providers.Common` (kept parallel so the layering rule holds; Core checks both). Its `MigrateFrom(previousJson, previousVersion)` runs right after the carry-over, with the raw old JSON in hand.
3. The result is rewritten under the current version and the stale files are deleted (**write-then-delete**, so a failed write never loses the prior data).

Every load is lenient per value, not only a migration's. A stored value that no longer reads as its property's type — written by a build where that property had another shape, such as an enum that became a string — is skipped, so the property keeps its default, and is logged as a fault through the logger the session's `ConfigManager` is made with, which the crash log keeps. A dev build and the installed release share one `config_{AssemblyVersion}.json` while their versions match, so without this either could stop the other from starting. A config implementing `IConfigMigration` is handed the raw old JSON, so recovering such a value during a migration is the hook's job and nothing is recorded.

A file that cannot be read **at all** — corrupt JSON, or one the process cannot open — is the same principle a file wide: the config keeps whatever was recovered before the read failed and defaults for the rest, the fault is recorded, and the name is tracked in `GetUnreadableConfigs` (and in `GetDefaultedConfigs`, which is what it now holds). Corrupt content is renamed aside as `config_{Version}.json.unreadable-{stamp}`, so the next launch starts from a clean file with the damaged bytes still there to recover by hand; a file that merely could not be opened is left alone, since its data is probably good. An unreadable `workcontexts` costs the workspace **list**, not the workspaces: `WorkspaceManager.RecoverWorkspacesFromDataFolders` rebuilds it from the folders under `Contexts\` — each folder name is a workspace name — so every workspace comes back pointing at its own conversations, notes, ribbon and provider config, with a fresh symbol and colour (those lived only in the lost file). `ShellHost` runs that before the provider scan, persists it at once, and reports what happened through `MessageCenter`; the setup wizard then opens to re-verify.

Migrated configs are tracked apart from brand-new ones (`GetMigratedConfigs` vs `GetDefaultedConfigs`). The first-run/update **setup wizard** (`SetupWizardViewModel.Build`) therefore re-asks for a global mandatory config only when it is genuinely new **or** its migrated data still fails the required-field check (`AreRequiredPropertiesSatisfied`) — never for information already on disk; the workspace/provider/model flow is skipped because the migrated per-workspace configs keep `IsWorkspaceConfigured` true. An **unreadable** config is the one case that overrides that: the wizard leads with what was lost and recovered, and runs the workspace/provider/model flow whether or not the workspace still looks configured, because what is there is exactly what the user is being asked to confirm. File-type mappings (`FileMapManager.SyncBundledDefaults`) follow the same spirit through a `_defaults.json` hash manifest: a changed bundled default refreshes mappings the user hasn't touched and leaves customized ones alone, fast-pathing when the bundle is unchanged.

**Reset.** Options → About offers a danger-styled **Reset Config** that, after a window-modal confirmation, wipes the entire `%APPDATA%\Smile\nexaflow` tree and relaunches (`App.ResetAndRestart` arms a write-suppressor, drops the single-instance mutex, then starts a `--reset` process that deletes the directory **before** init — lock-safe because the fresh process holds no handle) straight into first-run.

### Per-`Workspace` — shared, saved (one instance per named workspace)

A `Workspace` (`Core/Models/Workspace.cs`) is a named, themed, saved configuration. `Name`/`Color`/`Icon` and the default/last-session tabsets persist inline (in `WorkspacesConfig`); the shared services below are runtime-only (`[JsonIgnore]`), loaded once by `Workspace.EnsureSharedServicesLoaded`, with on-disk state under `…\Smile\nexaflow\Contexts\<Name>\` (folder name kept for compat):

| Member on `Workspace` | What it scopes | On disk |
|-----------------------|----------------|---------|
| `AiConfig` | The configured provider/model columns + each ability's **chain** of them (`AbilityChain`, tried in order). Shared, so an edit shows in every runtime on this workspace | `ai-abilities/` |
| `Persona` | The assistant persona (name + system prompt) | `ai-persona/` |
| `ProviderConfigs` | The provider configs (API keys / subscriptions) used to build provider instances | provider config folders |
| `RibbonService : RibbonLayoutService` + `RibbonChanged` | The shared ribbon layout. Saving raises `RibbonChanged`, which every window/runtime on this workspace observes to reload live | `ribbon.json` |
| `ConversationsDir` | Where this workspace's conversations are stored | `Conversations/` |
| `LocalDir` / `DocumentNotesDir` | What the workspace keeps on this machine alone, too large to roam with its settings: what whole-document work worked out — gists, summaries, translations — so doing it again asks only of what changed. `%LOCALAPPDATA%\Smile\nexaflow\Contexts\<name>\` (`ConfigManager.LocalDir`; beside the config base as `local\` when `NEXAFLOW_CONFIG_DIR` sets one apart), renamed and deleted with the workspace, let go of when no workspace names it | `DocumentNotes/` under the local folder |
| workspace-scoped feature configs | Any `IFeatureConfig` marked `[WorkspaceScopedConfig]` (saved via `WorkspaceManager.SaveWorkspaceScopedConfig`) | `<configName>/` |
| `DefaultTabs` / `LastSessionTabs` / `SuppressSessionRestore` | The tabset a fresh window opens with; the last session's tabset when it differed (a toast offers one-click restore); and the Configure panel's opt-out, which stops that tabset being recorded at all and drops any already held | inline in `workcontexts` |

### Per-`WorkspaceRuntime` — live (one per live `Workspace`; can have many windows)

A `WorkspaceRuntime` (`Core/Models/WorkspaceRuntime.cs`) runs one `Workspace` and owns its live services. `WorkspaceRuntimeFactory` builds it wired — assistant, shell services, file system, feature scope — and started; `WorkspaceRuntimes` holds it and moves it through its states (`WorkspaceRuntimeState`: Starting → Active ⇄ Dormant → Ended, each raised as `StateChanged`); `DisposeAsync` ends it, letting go of what it holds in order:

| Member on `WorkspaceRuntime` | What it scopes |
|------------------------------|----------------|
| `Providers : ProviderSet` | The live provider instances **acquired from the pool** for this runtime (deduped by config process-wide); released when it turns dormant or ends, acquired again when it resumes |
| `AiService : AIService` | The agent loop + AI-input-bar routing — resolves each `AiAbility` through the workspace's assignments + this runtime's providers; reads/writes the workspace's conversations. Kept while dormant, its providers unhooked |
| `ShellServices : ShellServices` | This runtime's **window + tab registry** (every window that shows it). Stable for the runtime's life |
| `Vfs : LocationFileSystem` | This runtime's **file system**: the shared `VirtualFileSystem` plus the remote locations this runtime has connected ([remote-locations.md](remote-locations.md)). Stable for the runtime's life |
| `Features : FeatureScope` | The feature instances built for it, `[FeatureService]` ones included; let go of when it ends, after which nothing more is built for it |

The `IShellServices` / `IAIService` / `IVirtualFileSystem` injected into a feature are the **active runtime's** instances (`FeatureManager` resolves them per `WorkspaceRuntime`). So "open a tab", "ask the AI" and "read that file" always act within exactly one runtime — and only the runtime's file system can see the locations it connected, which `VirtualFileSystem.Instance` cannot.

The Options and Manage-AI overlays are **modal** — they block switching. A workspace a window shows can't be removed from
the list; deleting it from its own window moves the window to another workspace first, and its runtime ends rather than
turning dormant. There is always at least one workspace.

### Per-window / per-tab

- A **window** (`IWindowHost`) registers into its runtime's `ShellServices`. Several windows can share one runtime (tear-off / "open in new window"). Switching the dropdown moves *this* window to the other workspace's runtime: it joins that runtime first — so the session is never without a window — then leaves its own, whose tabs in it close; when it was the last window there, the workspace records its session and turns dormant. The window then shows the workspace it joined: the session that workspace left, when it was dormant; its startup tabs otherwise.
- A **`Page`** (tab) is built by `FeatureManager.CreateTab(pageKind, runtime, params)` → the matching `IPageRegistration.CreatePageDefinition`. Its view-model is made lazily by `ViewModelFactory` and lives for the life of the tab; the view that draws it is Core's (`PageViews`), found by `[ViewFor]`.

### Per-feature (assembly)

- An **`IFeatureConfig`** is by default a **single, app-level instance per assembly** — registered with `ConfigManager` when its owning assembly is first **activated** (lazily, on first use or during warm-up), then never rebuilt; persisted at `…\Smile\nexaflow\<ConfigName>\`, outside `Contexts\`. Marking the config class `[WorkspaceScopedConfig]` opts it into **per-`Workspace`** persistence instead (one instance per workspace, under `Contexts\<Name>\`).

---

## Module Responsibilities

### Nexaflow.Core

The shell host. Owns the window chrome, tab strip, ribbon bar, breadcrumb bar, and the AI input row. Nothing in Core knows how to _render_ any individual page — it only knows how to _host_ them.

| File | Responsibility |
|------|----------------|
| `App.xaml.cs` | The WPF adapter over the session: opens `ShellHost` with what only WPF makes (`ShellPlatform`), applies the appearance settings, builds windows, and ends the session in `OnExit`. Also the windowless `--prestart` daemon and the window it keeps ready while none is open (`StandbyWindow`), the launch arguments (`LaunchRequest`: `--context "Name"`, and `--open "path"` from Explorer's "Open in Nexaflow"), and the single-instance IPC that forwards a later launch (a new window on the workspace's runtime; an `--open` path goes to the one workspace with windows open, else the first) |
| `Services/ShellHost.cs` | The session's composition root: builds, in stages, and owns everything every workspace shares, and ends it in reverse |
| `Services/WorkspaceRuntimes.cs` | The live workspaces: one runtime per `Workspace`, opened, resumed, made dormant and ended; a window's switch moves it between them |
| `Models/Workspace.cs` | The saved, shared workspace config (holds `AiConfig`/`Persona`/`ProviderConfigs`/`RibbonService`+`RibbonChanged`/conversations dir + default/last-session tabsets) — see [Ownership & Lifetime](#ownership--lifetime) |
| `Models/WorkspaceRuntime.cs` | The runtime (points at a `Workspace`; holds `Providers`/`AiService`/`ShellServices`/`Vfs`) |
| `Models/OpenPageRequest.cs` | `(PageKind, PageParams)` carrier for shell-level open commands (breadcrumb follow-links). Core-internal MVVM glue |
| `Services/Agent/ClientBlockParser.cs`, `ParsedAssistantTurn.cs` | The assistant wire-protocol parser the agent loop reads fenced `client_tool` blocks with. Core-only, so it lives in Core — not a feature contract |
| `Services/WorkspaceManager.cs` | One per session, made by `ShellHost`: the saved `Workspaces` list; `Add`/`Clone`/`Rename`/`Remove`/`DeleteWorkspace`, orphaned-data quarantine |
| `ProviderManager.cs` | One per session, made by `ShellHost`: loads provider **assemblies** + records provider/config **types**; `LoadProviderConfigs(dir)` + the ref-counted pool `AcquireProviderSet`/`ReleaseProviderSet`. Instances are model-bound (capability per config + execution per config+model); an execution instance is followed by `ModelResidency`, which loads its model ahead or lets it go idle, and cools with its pool lifetime |
| `ProviderSet.cs` | A runtime's acquired provider **instances** + the workspace's configs + assembly map + pool keys |
| `Services/AIService.cs` | `IAIService` impl — **per-`WorkspaceRuntime`**: provider registry, ability→model resolution, the agent loop, conversation history |
| `AI/AiConfig.cs` | Per-workspace AI ability config (`Columns` + each ability's `AbilityChain` of columns in `Assignments`); rendered by `AiAbilityGridControl` |
| `MainWindow.xaml.cs` | Wires shell commands; creates `ShellViewModel`; handles ESC/breadcrumb clicks |
| `ViewModels/ShellViewModel.cs` | Per-window VM: pane/split layout, overlays, AI input routing, toasts, workspace lifecycle commands; `SelectWorkspace` (in-place switch, blocked while a modal overlay is open) |
| `Services/ShellServices.cs` | `IShellServices` impl — **per-`WorkspaceRuntime`** (not an app-level singleton). Owns that runtime's window + tab registry, tearoff/cross-window moves, file watching, session capture/restore, window positioning |
| `FeatureManager.cs` | One per session, made by `ShellHost`. Delegates discovery to `FeatureCatalog` (a disk-cached index — **no** eager DLL loads on the common launch); resolves `IPageRegistration`/config/handler **types** lazily and builds instances per `WorkspaceRuntime` with scoped `IShellServices` + `IAIService`, kept in the runtime's `FeatureScope` until it ends. File-system contracts are **not** here (see below) |
| `Services/FeatureCatalog.cs` | The discovery engine behind `FeatureManager`. Persists the per-assembly type index to `…\Smile\nexaflow\discovery\catalog.json`, stamped with the app version **and** the feature-DLL set it was built from (`FeatureCatalogStamp`: name + length + write time per `Nexaflow.Features.*.dll` plus Core). Both must still match for the index to be trusted, and validating costs one directory enumeration with no assembly loads — so a same-version rebuild forces a rescan instead of silently serving an index that no longer describes the DLLs on disk (a stale index would render every newer page kind as an empty tab). Resolving a type loads + **activates** its assembly on demand (registering that assembly's global configs / archive handlers / theme contributions). A post-paint background warm-up activates the rest |
| `Services/StartupTimings.cs` | Opt-in startup profiler (`--timing` flag / `NEXAFLOW_STARTUP_TIMING=1`): a destination of the process's logs (`ShellLogging`) that times each startup milestone the shell logs (`StartupMilestones.StartupMilestone`); writes a milestone breakdown + `FIRST_WINDOW_MS` / `WINDOW_ON_DEMAND_MS` to stderr. Not made when off — the milestones then reach only the session's log |
| `Services/FileSystemFeatureRegistry.cs` | Discovery + matching for `IFileAction`/`IFolderAction`/`IFileCreateAction`/`IFileCreateWithOptions`/`IFolderViewlet` (deliberately split out of `FeatureManager`) |
| `Services/RibbonLayoutService.cs` | Serialize/deserialize ribbon items — **per-workspace** (constructed with the workspace's own folder, shared by its runtimes), not a single global `ribbon.json`. Bundled defaults live in `Ribbon/default-ribbon.json` (`LoadDefaults`) |
| `Services/WindowManager.cs` | Static DPI/monitor/cursor maths only — tab/window lifecycle is `ShellServices` |
| `FileActions/` | Core-owned file actions: copy, delete, rename, and other system-level operations |
| `ViewModels/RibbonEditorViewModel.cs` | Ribbon customization on a deep-cloned draft, committed and saved once on Done; its view (`Controls/RibbonEditor.xaml`) exists only while it is open |
| `Controls/RibbonItemButton.cs` | The one ribbon button visual — bar, editor strip and shape gallery — in any shape and colours |
| `Controls/TabStrip.xaml.cs` | Renders tabs; emits tearoff drag events |
| `Controls/BreadcrumbBar.xaml.cs` | Renders segments; dispatches `TargetPageKind` clicks back to shell |
| `Models/RibbonItem.cs` | Ribbon item state + `TabFactory` delegate + serialization metadata |
| `Models/PageKinds.cs` | String constants for Core-owned page kinds (`FileSystem`, `Placeholder`) |
| `Help/` | Core's own page kind, `Help`, discovered like any feature's: the ? above Options / F1 (`HelpPaneController`) opens the help for the page in use in the pane beside it — splitting if need be — follows that pane's page until the reader moves within help, and closes when pressed while showing it. `HelpLibrary` reads `help/<PageKind>.md` out of the language packs; `HelpSearchIndex` searches the page shown first, then every help page, with the same matcher the page highlights with. A `locate:` link in a page points at the screen rather than navigating: `LocateTour` lassoes each control it names, found in-process by its AutomationId |
| `Localization/` | `LanguageManager` + `AssemblyLanguagePack` — the language packs; see [localization.md](localization.md) |

### Nexaflow.Features.Common

The contract layer. Every feature depends on this; nothing else does.

| File | Responsibility |
|------|----------------|
| `Page.cs` | Observable page/tab state: `Title`, `Icon`, `IsActive`, `Breadcrumbs`, `PageKind`, `PageParams`, `ViewModelFactory` delegate, the made `ViewModel`, `Reinitialize`, `Closed` event. Holds nothing of the view |
| `BreadcrumbSegment.cs` | One crumb: label, drop-down children, same-tab `Navigate` action, or cross-tab `TargetPageKind`; the path it copies and the `Content` it stands for |
| `BreadcrumbContent.cs` · `BreadcrumbDisplay.cs` · `IBreadcrumbDisplayFilter.cs` | What a crumb stands for (`PlaceContent`), how it reads (parts, separator, copy text, tooltip), and the per-runtime filters, stacked by `Order`, that restate the content each knows before the shell shortens it |
| `IPageRegistration.cs` | Interface a feature implements to advertise one page kind: `PageKind` + the cheap `CreatePageDefinition(params)` (plus optional `Parameters`, `CanBeContextItem`, `CreatePageDefinitions`). Each impl also exposes `static string StaticPageKind` so `FeatureManager` discovers it by reflection |
| `IPageViewModel.cs` | What a page is, implemented by its view-model: `GetContext`, `GetClientTools`, `GetContextObject`, `GetContextGrant` (the files and folders it shows — `ContextGrant.cs`), `Reinitialize` (shown again with parameters) |
| `PlaceholderViewModel.cs` | A page that only says why it is empty (a missing feature, nothing to open) |
| `IShellServices.cs` (under `Services/`) | The **active `WorkspaceRuntime`'s** shell handle injected into feature code (one impl per runtime, not an app singleton): `OpenTab`/`CloseTab`/`FindTab`, `QueueBackgroundTask`, `ShowError`/`ShowNotification`/`ShowPrompt`/`ShowConfirmation`, `ShowOverlay`/`CloseOverlay` (feature-defined shell-modal overlays), `PinToRibbon`, `DiscoverImplementations<T>`. Owns UI-thread access for features: `WatchFile(path, onChanged)` (shared, deduped, UI-marshalled, lifecycle-managed file watching → `IFileWatch`), `WatchFolder(path, onChanged)` (a folder's own entries through the shell's change notifications, sync providers' included → `FolderChange`, one registration per folder, torn down with the workspace), `RunOnUiAsync(...)` (marshal work to the workspace UI thread), `CreateUiTimer` / `CreateUiDelay` (a timer that repeats, or a delay that runs once each time it is started, ticking on the UI thread → `IUiTimer`) and `Clipboard` (→ `IShellClipboard`). Features use these instead of `Application.Current.Dispatcher`, `DispatcherTimer` and `System.Windows.Clipboard`, so a view-model names no UI toolkit |
| `IFileWatch.cs` (under `Services/`) | Handle from `WatchFile`: `Enabled` (hold + coalesce while false, flush on re-enable), `Dispose` to unwatch |
| `IUiTimer.cs`, `UiTimerPriority.cs` (under `Services/`) | Handle from `CreateUiTimer` / `CreateUiDelay`: `Interval` (changeable while running), `Start`/`Stop`, `Restart` (a debounce, with a delay), `Dispose` (never ticks again). `Background` priority for polls and debounces, `Render` for a tick that draws every frame |
| `IShellClipboard.cs` (under `Services/`) | The clipboard: `Read()` gives what it holds as an `IDragPayload`, read with the same kinds and readers as a drop (`Text()`, `Image()`, `Files()`, `DragKinds.FilesCut`); `Write(payload)` puts a `ComposedPayload` (`OfText`, `OfImage`, `OfFiles`, `With`) there; `Clear()`. A write or clear returns false while another application holds the clipboard open — nothing throws. UI thread only. Windows' formats are written by `Visuals.Common`'s `WindowsDataObject`, as `WindowsDragPayload` reads them |
| `IAIService.cs` | Per-`WorkspaceRuntime` AI service: handler scoring/disambiguation, the `RunAgentAsync` agent loop, conversation load/save, analysis + artifacts |
| `IBackgroundTask.cs` | Self-contained background work (`Description` + `RunAsync`) handed to `IShellServices.QueueBackgroundTask` |
| `IFeatureConfig.cs` | Marks a POCO as a config section; discovered and instantiated by `FeatureManager` |
| `IContext.cs` / `FileSystemContext.cs` | Typed context objects offered by ViewModels via `IPageViewModel.GetContextObject()` |
| `ClientTools/*.cs` | Agent-loop contracts: `IClientTool`/`ClientToolBase`/`DelegateClientTool`, `ClientToolParameter`, `ToolSafety`, `ToolCall`, `ToolResult`, `ClientPlan`, `IToolApprovalCoordinator`, the shared `ToolArgs` argument reader. Contracts only — the Core-only wire-protocol parser (`ClientBlockParser`, `ParsedAssistantTurn`) lives in `Core/Services/Agent/` |
| `ConfigAttributes.cs` | `[ConfigDisplayName]`, `[FolderPath]`, `[FilePath]`, `[ListSource]`, `[CustomControl]` / `ICustomConfigApply` / `IConfigChangeTracker` |
| `AiResponse.cs` | `AiResponse` record returned by `IAIService.RunAgentAsync` |
| `ConversationRecord.cs` | Chat conversation + message list + attachments |
| `Services/IQueryHandler.cs` | Intercepts the AI input bar; registered globally or inferred from the active page's ViewModel |
| `FileActions/IFileAction.cs` / `IFolderAction.cs` / `IFileCreateAction.cs` / `IFileCreateWithOptions.cs` / `ICacheable.cs` | File/folder context-action contracts (discovered by `FileSystemFeatureRegistry`, not `FeatureManager`) |
| `Ribbon/*.cs` | `IRibbonPinHandler`, `IRibbonExecutionContext`, `ISelectionProvider`, `RibbonPinResult` — drag-to-ribbon pinning |
| `Viewlets/*.cs` | `IFolderViewlet`, `IViewletController`, `ViewletDisplayMode` — folder-display extensions, each a view-model drawn by its `[ViewFor]` view |
| `IContextPreview.cs` / `PagePreview.cs` / `TextContextPreview.cs` | A page's preview for the conversation's context panel, as a model: the page itself (`PagePreview<TPage>`), a text summary (`TextContextPreview`), or a feature's own |
| `IKeyboardHandler.cs` | Claims global keyboard shortcuts; queried against the active page |
| `FileViewRequest.cs` | "Show this file in its viewer" through `HandleObject` — never launches it |

### Feature assemblies

Each feature assembly follows the same internal structure:

- One or more `*PageRegistration : IPageRegistration` (a few are named `*TabRegistration` but implement the same interface) — factory entry points, injected with `IShellServices` and any `IFeatureConfig` the assembly declares
- `ViewModels/` — MVVM Toolkit `ObservableObject` view models; never reference Core
- `Views/` — WPF `UserControl`s, each marked `[ViewFor(typeof(ItsViewModel))]` and taking that view-model in its one constructor; Core makes them (`Visuals.Common/Viewing/ViewRegistry`)
- Optional: `IQueryHandler` implementations registered globally or resolved from the active ViewModel
- Optional: `IFileAction` / `IFolderAction` implementations for file browser context menus
- Optional: `IFeatureConfig` POCO for persisted settings (auto-discovered by `FeatureManager`)

### Shared UI (Nexaflow.Visuals.*)

Non-contract UI shared across features and Core. Features may reference these (they are not `Features.Common`, but they carry no contracts — only controls/converters), and they never reference Core or a feature.

| Assembly | Holds |
|----------|-------|
| `Nexaflow.Visuals.Common` | Reusable WPF controls (`PieChart`) and the value converters (`BoolToVisibilityConverter`, `InverseBoolToVisibilityConverter`, `NullToBoolConverter`, …) used in nearly every feature view; and the UI-string seam, `Localization/` — `Str.Get` / `{loc:Str Key}`, whose source Core sets at startup ([localization.md](localization.md)); and `Locate/` — the `locate:` link scheme and the animated lasso it throws round a control on screen |
| `Nexaflow.Visuals.Text` | Markdown, shown and written in: `MarkdownSurface` — read-only or not, a document or one block of a language — draws the whole document on one element over the shared layout tree. Every markdown host uses it, from AIChat's `ConversationView` to the Markdown page and the Solver's formula field |

**Theme/styles:** application brushes **and** shared control styles live in the app-merged `Nexaflow.Core/Themes/Styles.xaml`. Feature XAML references them by `{StaticResource <key>}` — there is no assembly reference; the lookup resolves up the tree to `Application.Resources`. Define a shared style there once rather than copy-pasting per view.

### Nexaflow.Providers.*

**The contract is deliberately lowest-common-denominator.** `StreamAsync` is structured-in, **events-out**:
a provider yields its reply as `LlmStreamEvent`s — today `LlmTextDelta`, the next piece of text — through the
shared `LlmStreamRunner` (activity lifecycle, clean cancellation, hung-stream timeout, transient retry before
the first event). The set of events is closed and grows by a record: a consumer switches on what it
understands, so usage figures or native tool calls arrive as new events without breaking anyone. A caller
that acts on a finished answer — tool ranking, image descriptions, analysis — reads the stream to its end
with the `CompleteAsync` extension. Aria answers whole and yields one event.

Each provider also says **where a request's content goes** (`Residency`: it leaves this machine, or stays on it —
read from the endpoint for Ollama and for a base URL set on OpenAI, xAI or Claude) and whether it **can take a
request now** (`IsAvailable`; the API-key providers are not without a key). `AIService` walks an ability's chain to
the first available provider, whatever the page. When the page's `GetContextSecurityRisk` is `High` — a conversation
is as high-risk as the riskiest page pinned in it — and a model that would read it sends data off this machine,
`IAIResponseHandler.RequestContextReleaseAsync` names those models and asks the user; a refusal cancels the request.

A provider also says **whose harness it is** (`Harness`): bridged — Nexaflow is a client of a model's own harness,
a cloud API or a local server like Ollama — or owned, where Nexaflow is the whole harness. The owned language models
run in the **AI host** (`src/Nexaflow.AiHost/`): `Nexaflow.AiHost.exe` beside the app, started on the first request
by the app's one `AiHostClient`, bound to the kill-on-close job, and replaced when it dies or stops answering
([local-ai.md](local-ai.md)). The provider for them, "Nexaflow" (`Nexaflow.Providers.AiHost`), gets the client the way
any provider gets an app-wide service: the session's `ProviderManager` is made with the services, and a provider constructor
parameter of a service's type receives it. A request says how urgent it is (`LlmUrgency`), which the host schedules
the GPU by. A new workspace's setup asks who runs its AI where the machine can run an owned model
(`WorkspaceAiDefaults`).

Tool use is **prompt-convention**: the agent loop asks the model for fenced ```` ```client_tool ```` blocks
and parses them out of the text — no provider sends native function-calling schemas. A model that writes a call in
its own framing is met halfway: an unfenced `client_tool` body naming a known tool is the call, and a known tool
called as text (`add_note(…)`, `call:add_note{…}`) is sent back as a malformed block to rewrite. This keeps every
backend (including local Ollama models and the pipe-based Aria) behaviourally identical and the agent loop
provider-agnostic; the trade-offs are no usage/cost metadata and tool reliability riding on
instruction-following. While a step streams, the loop shows its prose to the `IAIResponseHandler` as a
draft (`ShowReplyDraft`, at most every 50 ms) through `ReplyDraft`, which holds back the fences the agent
acts on and shows any other fenced block once it is closed. The step's outcome ends the draft: the final
answer replaces it, a batch run unasked keeps it as narration above its tools (`EndReplyDraft(keep: true)`),
an approval prompt or a retry takes it away.

A provider serves one kind of model (`ModelKind`) through that kind's contract — `ILlmProvider` for a language
model, `ISpeechToTextProvider` for speech to text — over the `IAiProvider` base every provider shares (name,
residency, availability, models, warm-up); one backend serving two kinds ships a type per kind under one name, as
OpenAI does. The ability grid's columns carry their kind, and a row takes only columns of its own. A provider that
needs model files declares an `IModelAssetStore` (the AI host SDK's, `AiHost.Provisioning`) in its constructor and
describes them in a catalog manifest it ships (`ModelCatalog`, `Providers.Local`); the store fetches them. A provider
that exposes its catalog (`ILocalModelCatalog`) gets a page
in Configure Workspace listing its models, their size and whether each is here, to fetch, remove, and — when its
kind serves one ability — choose the one that ability uses. Everything on the page acts at once: the choice is made
on the ability grid it shares with the AI page and saved straight away, leaving the grid's other edits for Apply.
The grid's rows are the abilities (`AiAbility`, in Features.Common so features can name one): General Intelligence —
the conversation and every one-shot piece of thinking — Disambiguation, Image Recognition, Speech In and Out, and the
picture and video rows. Setup fills them by who runs the AI (`WorkspaceAiDefaults`): Nexaflow alone puts the Local
column wherever it serves; a provider alone puts its language model in the reasoning rows and the models of its other
kinds the user picked (`ModelKindsStep`) in theirs, the Local column keeping a model chosen for each job, ticked
nowhere; both chain the two in the reasoning rows and give the Local column the rest. A grid saved by an earlier version
is brought up to date as it is read (`AiConfig.BringUpToDate`): its Conversation row becomes General Intelligence, and
Speech In starts on the Local column where it never had the row; the workspace settles that model as it loads — one of
Nexaflow's own speech models, and none where the machine runs none of them (`LocalColumns.Settle`). The mic holds a spoken
conversation where something listens, a voice speaks the language and a model answers (`AIService.CanConverse`), locally or
through a cloud service's speech models; where nothing does, the mic is not offered.

**What needs an ability.** `IAIService.StatusOf` says where an ability stands — unassigned, assigned but unreachable,
waiting on the download of a model Nexaflow runs itself, downloading, or ready (`AbilityReadiness`) — and
`AbilitiesChanged` says when that may have moved. A control that needs one binds a gate (`IAIService.Gate`, attached
with `AiAbilityGating.Gate`): disabled while nothing serves the ability, its tooltip saying where to assign a model, and
enabled while a model waits on its download, whose first use offers it (`MakeReadyAsync`) and stops there. A client tool
declares what it needs (`IClientTool.Needs`): the agent loop leaves it out where nothing serves that, and one waiting on a
download is offered behind `DownloadFirstTool`.

All providers implement `IAiProvider` through their kind's contract (and optionally `IDisposable`/`IAsyncDisposable`) from `Providers.Common`. `ProviderManager` loads the plugin assemblies at startup and records the provider/config **types** they expose — it does not create instances eagerly. Provider **instances** are pooled process-wide and ref-counted: `AcquireProviderSet` hands each `WorkspaceRuntime` one model-agnostic *capability* instance per (provider type + config) for model enumeration, plus one model-bound *execution* instance per ability-grid assignment (loading nothing when made; cooled + disposed when its last ref drops — `ReleaseProviderSet`). Whether a model is loaded before its first request is `ModelResidency`'s to decide, after the first window has drawn — [local-ai.md → What stays loaded](local-ai.md#what-stays-loaded). Provider **configs** (API keys / subscriptions) live on the saved `Workspace` (`Contexts/<name>/<configName>/`), so two workspaces can hold different subscriptions for the same provider while identical configs share one pooled instance. The acquired set lives on `WorkspaceRuntime.Providers` and registers into that runtime's `AIService`.

| Assembly | Backing service |
|----------|----------------|
| `Nexaflow.Providers.Aria` | Windows named-pipe to the local Aria service. Two layers: `AriaNamedPipeClient` (raw JSON framing) and `AriaClientService` (one-shot request/reply, parses `#focustab` instructions from responses). |
| `Nexaflow.Providers.Claude` | Anthropic Claude API over HTTPS. |
| `Nexaflow.Providers.Gemini` | Google Gemini API over HTTPS. |
| `Nexaflow.Providers.OpenAI` | OpenAI API over HTTPS: chat models, and the realtime transcription API for speech to text. |
| `Nexaflow.Providers.OpenAICompatible` | Not a provider: `ChatCompletionsLlmProvider`, the chat-completions wire format (messages, tools, the streamed reply, the model list) that the OpenAI and xAI language models derive from, each saying only its name, endpoint, key and what its models can do. Shared like `Providers.Common`, so it declares no concrete provider or config (`ArchitectureRulesTests`). |
| `Nexaflow.Providers.XAI` | xAI's Grok models over its OpenAI-compatible API. Which models it serves, which take pictures and how much each reads come from xAI's own model endpoints (`/language-models`, `/models/{id}`), not from the model's name. |

| `Nexaflow.Providers.Ollama` | Ollama REST API for locally-hosted models. |
| `Nexaflow.Providers.Local` | Not a provider: what a provider whose models run on this machine tells the app — its catalog (`ILocalModelCatalog`, `CatalogModel`, the manifest reader `ModelCatalog`), the models this machine cannot run and why, the runtime several share. The files themselves are the AI host SDK's (`Nexaflow.AiHost.Provisioning`: `ModelAsset`, `IModelAssetStore`, `ModelAssetStore`). Shared like `Providers.Common`, so it declares no concrete provider or config. |

---

## Key Data Models

```
Page  (ObservableObject)
  ├── Title, Icon, IsActive             (observable — drives tab strip UI)
  ├── PageKind: string?                 (e.g. "Search", "Projects")
  ├── PageParams: Dict?                 (current params, kept in sync by the page via IShellServices)
  ├── Breadcrumbs: ObservableCollection<BreadcrumbSegment>
  ├── ViewModelFactory: Func<IPageViewModel>? (set by IPageRegistration.CreatePageDefinition; run on first need)
  ├── ViewModel: IPageViewModel?        (made by GetOrCreateViewModel(); a failing factory is LoadException)
  └── Closed: event                     (raised on permanent close, not deactivation)

BreadcrumbSegment
  ├── Label
  ├── Children: List<string>          (drop-down picker items)
  ├── Navigate: Action?               (same-tab navigation)
  ├── TargetPageKind: string?         (cross-tab: shell opens/focuses that page kind)
  ├── TargetPageParams: Dict?
  ├── Path: string?                   (what Copy path copies, unless a display filter restates it)
  └── Content: BreadcrumbContent?     (what it stands for, read by the display filters)

RibbonItem
  ├── Kind: Button | HalfGroup | Separator
   ├── Label, Icon: IconRef (Nexaflow.Icons), IsHalf
  ├── Foreground, Background, BorderColor: ColorSpec   (theme default · swatch token · custom)
  ├── BorderWeight, Shape
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

Implement in your feature assembly. `FeatureManager` discovers it **automatically** via the cached `FeatureCatalog` index (no code-registration step); its assembly is loaded and the registration built **lazily** on first use. It injects constructor dependencies per `WorkspaceRuntime`: any `IFeatureConfig` declared in the same assembly, the scoped `IShellServices`, `IAIService`, and the runtime's `IVirtualFileSystem`. Expose a `static string StaticPageKind` — the catalog reads the page kind from it without instantiating (and caches it).

`CreatePageDefinition` must stay **cheap and side-effect-free** — callers build a definition speculatively just to read its `Title`/`Icon` (e.g. for a menu) and may discard it. Construct the view-model **inside the `ViewModelFactory` closure** so it is built only when the page is first needed. The registration never names a view: the view marked `[ViewFor]` the view-model's type draws it. Optionally advertise `Parameters` (so the shell/AI can describe how to open the page) and set `CanBeContextItem`/`CreatePageDefinitions` for pages offered in the AI "add context" and ribbon-editor menus.

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
            // built lazily on first need — keep all heavy work in here, not above
            ViewModelFactory = () => new MyViewModel(path, config, shellServices)
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

### `IPageViewModel` and `[ViewFor]` — what a page is, and what draws it

A page **is** its view-model: the shell, the AI pipeline and every other page reach it through `Page.ViewModel`,
never through a control. `Reinitialize` is called whenever the shell shows the page again with a parameter set
(routing found it open, or the user re-clicked its tab).

The view is the UI layer's concern. Mark it `[ViewFor(typeof(MyViewModel))]` and give it one constructor taking the
view-model; `FeatureCatalog` records it (in feature assemblies and in `Nexaflow.Visuals.*`), `ViewRegistry`
(`Visuals.Common/Viewing/`) makes it — a view-model no view names is drawn by the view of the type it derives from —
and Core's `PageViews` keeps one per page until the tab closes, so a tab moved between panes or windows keeps its
view. A page whose view-model or view cannot be made is drawn by `PageLoadErrorView` saying why. Because nothing in a
page's contract is WPF's, another toolkit can draw the same view-models with views of its own.

The same holds for what a contract hands out to be shown inside a feature's own surface — a page's context preview, a
folder viewlet. Those are models too, and the feature hosting them places a `ModelHost` (`Visuals.Common/Viewing/`),
which draws its model with the registry every main window hands down (`ViewRegistry.Registry`, an inherited
property), so a feature never reaches for the app's registry itself.

`IPageViewModel` is also the AI pipeline contract: the shell queries context and exposes **client tools** the AI agent may invoke. A client tool is a self-contained `IClientTool` (use `DelegateClientTool` for one-liners) carrying its own metadata and execution. Safe tools (`ToolSafety.SafeOperation` — observers, or changes the user can see / must still save) auto-run; tools with real consequence (`ToolSafety.RequiresApproval`) are approved first. `GetClientTools()` and `GetContextObject()` have defaults, so a page that only supplies context overrides nothing else.

```csharp
// The View — draws the view-model, and nothing else knows it exists
[ViewFor(typeof(MyViewModel))]
public partial class MyView : UserControl
{
    public MyView(MyViewModel vm) { DataContext = vm; InitializeComponent(); }
}

// The ViewModel — owns all AI pipeline logic
public partial class MyViewModel : ObservableObject, IPageViewModel
{
    public string GetContext() => "User is viewing My Feature";

    public void Reinitialize(Dictionary<string, string> pageParams) { /* react to new params */ }

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new DelegateClientTool(
            "refresh", "Reload the current view.", [], ToolSafety.SafeOperation,
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
alone. `Symbol` claims a single-character prefix for exact routing, and when none of its owners claims the line the
character was no prefix: whoever claims the line as typed has it (`/usr/bin/env` on a console). Otherwise scores
compete. At 0.8 or more, clear of the next by 0.2, a handler wins with no model seeing the line; anything lower is the
handler saying it is unsure, and `IAIService.DisambiguateToolSelection` decides, as it breaks ties — the console scores
a line that could be a command or a request 0.5 for exactly that.

```csharp
public sealed class MyQueryHandler : IQueryHandler
{
    public string Description => "Handles my feature's input";
    public string? Symbol => null;                           // optional prefix character

    public float CanProcess(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm is not MyViewModel) return 0f;            // page-scoped via the type check
        if (prefixed) return 1f;                             // the user chose this handler — take it
        return LooksLikeMine(input) ? 0.9f : 0f;             // heuristics compete for bare text only
    }

    public async Task<string?> ProcessAsync(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm is not MyViewModel vm) return "No active tab.";
        await vm.DoSomethingAsync(input);
        return null;   // null = handled silently; string = shown in AI Chat
    }
}
```

**The `prefixed` short-circuit is mandatory, not stylistic.** A symbol exists so the user can force a
handler; gating a prefixed call on the same heuristics used for bare input means the forced invocation
scores 0 and falls through to the agent, which is the opposite of what was asked. Anything that can't be
done gets reported from `ProcessAsync`, which can say why.

To make a page *searchable*, don't write a handler at all — implement `ISearchable`
(`Features.Common/Search`) and the shell's `?` handler routes to it, with the agent's `search_page` /
`show_search_results` tools added automatically. See [features.md](features.md) → *Searching a page*.

---

### `IFileAction` / `IFolderAction` / `IFileCreateAction` — File browser context actions

Implement in the assembly that owns the concern. `FileSystemFeatureRegistry` (in Core — **not** `FeatureManager`) discovers these across Core and the feature assemblies. Actions that open a viewer tab belong in the feature that owns the viewer; system-level actions (copy, rename…) belong in `Nexaflow.Core.FileActions`.

Actions are matched to files via `ExperienceId` — a hierarchical path (e.g. `"/image"`, `"/binary/installer"`) that `FileMapManager` resolves against the selected file. Constructor dependencies (e.g. `IShellServices`, and the runtime's `IVirtualFileSystem` for an action that touches files) are injected on resolution; show modal prompts via `IShellServices.ShowPrompt` / `ShowConfirmation`.

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
| `PerformActionAsync(string)` / `PerformActionAsync(IEnumerable<string>)` | Execute the action, answering whether it did something now — the strip's success tick. Awaitable, because on a location a rename or a delete knows how it went only after a round trip; an action that only opens a tab answers at once |

`IFileCreateAction.CreateItemAsync` answers the path of the item it made, which the browser selects.

`IFolderAction`s are matched **structurally** rather than by `ExperienceId`: `FolderNameGlob` (folder name), `ContainsFolderGlobs` (has a matching sub-folder), and `ContainsFileGlobs` (has matching files). When `ContainsFileGlobs` is set, `MinimumFileGlobMatchPercentage` (default `0` = "at least one match") requires that share of the folder's top-level files to match — e.g. the image Slideshow/Album actions require ≥30% images. The content check enumerates the folder once and bails as soon as the threshold is reached or has become unreachable. The same name/content constraints are applied to `AppliesToRoot` actions against the **currently-open folder** (no selection), so a constrained action only appears on a qualifying folder, not every folder. Contents are read only where the file system resolves the folder to this disk (an archive on it included): matching runs while the strip or a context menu is built, so on a location a folder action is matched by its name alone, as a file's magic-number rule is skipped there.

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

A feature can show its own **shell-modal overlay**: pass any view-model to `ShowOverlay`, and the overlay host in `MainWindow` renders it via a `DataTemplate` matched on the VM's type — ship that template in a `ResourceDictionary` advertised through your `IThemeContribution`. Implement `IShellOverlay` on the VM to opt into backdrop-click dismissal. The Options and Configure panels ride that same host (`OverlayCoordinator.ActiveOverlay`, one at a time, ranked feature overlay → Options → Configure).

The built-in **questions** — confirmation and prompt — get a host of their own above it (`ActiveModal`), because a panel is what raises most of them: the Options page's own Delete button asks before removing a workspace, and a question rendered in the panel's host would sit behind the panel where nobody can reach it, or replace it and take its unapplied edits down with it. So a question layers over whatever is open and hands the host back untouched when it is answered, Escape and the backdrop close the topmost thing only, and `ConfirmationVisible` / `PromptVisible` keep driving airspace coverage as before.

**Questions go to the shell; forms get a `ModalCard`.** A *question* — yes/no, or one line of text — is window-modal: ask it with `ConfirmAsync` / `ShowConfirmation` / `ShowPrompt`, and the shell renders the one dialog every feature shares (its models are `Visuals.Common`'s `ConfirmationRequest` / `PromptRequest`). A *form* — content the user works in — is tab-modal: wrap it in `Visuals.Common`'s `ModalCard` (scrim + centred card, a `Tone` for the border, Escape → `CancelCommand`), which shares the chrome and leaves the content, bindings and AutomationIds the feature's own. Not `ShowOverlay` for a form: a shell-modal wizard locks every tab, and the host holds one overlay, so a second form would replace the first. `HandRolledModalRatchetTests` fails on a scrim-wrapped card anywhere outside `Visuals.Common`. Its baseline, `hand-rolled-modals.txt`, is empty — a new one is refused, not listed.

---

### `IFeatureConfig` — Persisted settings

Implement a plain POCO. `FeatureManager` discovers it via the cached `FeatureCatalog` index and instantiates + registers it when its owning assembly is first **activated** (lazily, or during the post-paint warm-up), making it available for injection into `IPageRegistration` constructors. The Options panel renders a property grid for free using reflection and the config attributes. (Because the panel lists **every** feature's config, opening it forces a full activation of any not-yet-loaded features.)

```csharp
public sealed class MyConfig : IFeatureConfig
{
    public string ConfigName   => "myfeature";
    public string FriendlyName => Str.Get("MyFeature.Config.My");

    [ConfigDisplayName("MyFeature.Config.My.RootFolder")]
    [FolderPath]
    public string RootFolder { get; set; } = string.Empty;

    [ConfigDisplayName("MyFeature.Config.My.Provider")]
    [ListSource(typeof(LlmProviderRegistry), nameof(LlmProviderRegistry.GetProviderNames))]
    public string Provider { get; set; } = string.Empty;
}
```

The title and each label are string-table keys, so the section reads in the active language — [localization.md → Options](localization.md#options).

For a fully custom options UI, apply `[CustomControl(typeof(MyOptionsControl))]` to the config class and implement `ICustomConfigApply` on the control.
The control is made by its parameterless constructor and bound to the config. It reaches no service itself: a Core page that
shows the session — About, Shell, Workspaces — is bound instead to a view-model `ConfigPageModels` makes from the session's services,
which the window hands its Options and the app its setup wizard.

---

### `IKeyboardHandler` — page keyboard shortcuts

Implement on your page's `UserControl` **or** its ViewModel. The shell dispatches it from the window's
tunneling `PreviewKeyDown` (`MainWindow.xaml.cs`) for the **active page only** — it resolves the handler off
the active page (its view first, then its view-model) on each keystroke, so the mapping follows the focused
tab automatically. Because it's the window that dispatches, a page's shortcuts fire whichever shell control
(ribbon, breadcrumb bar, the view itself) currently has focus — not only when the view is focused.

Two rules the shell applies so it composes cleanly:

- **Skipped while a text box is focused** (`Keyboard.FocusedElement is TextBoxBase`), so typing keeps its
  native editing keys (Ctrl+C/V/A, Del, …). Guard any *non*-text-box modal state (a confirm banner, an
  inline prompt) inside your own `CanProcessKey`.
- **The active page gets first refusal, then shell globals** (Ctrl+Tab → AI input, Ctrl+W → close tab). So a
  page may claim a key the shell would otherwise take.

A key is a `ConsoleKey` (a Windows virtual key) and what is held a `KeyModifiers`; Core's `KeyTranslation` is the one
place WPF's keys become those.

```csharp
public bool CanProcessKey(ConsoleKey key, KeyModifiers modifiers)
    => modifiers == KeyModifiers.Control && key == ConsoleKey.OemPlus;

public bool ProcessKey(ConsoleKey key, KeyModifiers modifiers)
{
    ZoomIn();
    return true;
}
```

---

### `IDropTarget` — drag and drop

Every drag over a window — from Windows, another application or the app itself — goes through one `DropRouter`
(`Nexaflow.Visuals.Common/Dropping/`, attached by `MainWindow`). A feature never sets `AllowDrop` for one or handles a
drag event: it says what it takes (`AcceptedKinds`), what a drop would do (`OfferDrop`, asked as the pointer moves, so
it touches no disk) and takes it (`AcceptDrop`). Nothing in the contract is WPF's.

A target registers by where it is:

| Where | How |
|---|---|
| anywhere on a page | the page's view **or** its view-model implements `IDropTarget` |
| on one control | the control implements it |
| on an area of a view | `dropping:DropSurface.Target="{Binding …}"` |
| the ribbon | `IRibbonPinHandler` — what a drop becomes as buttons |
| the AI bar | `IChatDropHandler` — what a drop becomes as text |

The tabs are Core's own target: a file dropped there is raised as a `FileViewRequest`, a link as itself.

- **The innermost surface wins, a target or a control's own handling alike.** Words over a text box are the box's; a
  file over it, which the box refuses, goes to the targets around it. `OfferDrop` returning null passes to the surface
  around; `DropOffer.Refused` ends the asking.
- **Kinds, not formats.** `WindowsDragPayload` is the one place Windows' formats become `DragKinds`: `Files` (paths),
  `FileContents` (files that are only content — a mail's attachment, a file out of a zip), `Link`, `Text`, `Html`,
  `Image` (PNG bytes). A drag of the app's own names a kind of its own (`Page.DragKind`, `"FileAction"`).
- **The payload lives as long as the drag.** Read it inside `AcceptDrop`, before the first await. `FilesOnDisk()` gives
  paths that outlive it, writing content-only files to a staging folder (`DropStaging`) with their names made safe and
  marked as from the internet.
- **The source is never told its files were moved**, which is leave to delete them: a target that moves does the whole
  move. A drag from elsewhere that nothing takes is refused — WPF's own default answer is Move.
- **Looking never launches.** `FileViewRequest` opens the viewer for the type, or text or bytes where none is mapped.
- **A control's own gesture stays its own**: a list reordering, a layer dragged onto another. It carries a kind of its
  own, and the router leaves it with the control that set `AllowDrop` for it.
- **A native child window is outside all of it**: WebView2 takes the drops over itself.

---

### `IClientTool` — AI-invokable client tools

Expose tools from a page by overriding `IPageViewModel.GetClientTools()`. Each tool is an `IClientTool` (or a `DelegateClientTool` for trivial cases) carrying its own name, parameters, `ToolSafety`, and `InvokeAsync`. During `IAIService.RunAgentAsync` the LLM emits `client_tool` blocks; the harness runs read-only tools immediately and routes mutating ones through `IToolApprovalCoordinator` before invoking them, feeding each `ToolResult` back to the model.

#### What a tool may reach on disk

The assistant works inside what the user has put in front of it by the tabs it sees, and the user's choice of tabs is
the security boundary: a tool reaches what the user can see, and nothing the user has not shown it. Each page says what
of the disk it shows with `IPageViewModel.GetContextGrant()` — a `ContextGrant` of files given one by one (an open
document, the pictures of an image tab) and folders given whole (a file system tab's folder; This PC's drives). A
conversation's grant is everything pinned to it, with its attachments. `AiSurfaceRulesTests` names a page with a security
scope that says nothing of the disk, unless it is listed as showing nothing from it.

`PathScope` (in `Nexaflow.IO.Common`) is the one check. A path is inside when it is one of the files, or one of the
folders or anything under it — `C:\temp\special` holds `C:\temp\special\other`, never `C:\temp` or
`C:\temp\specialist`. It reads a path as the user reads it: made full, so `..` climbs no further than it says; short 8.3
names spelled out; a long-path or device prefix before a drive or share taken off; case ignored. Relative paths, named
streams, wildcards, raw devices and volumes by name are no path at all. A link inside a folder is followed as the user
can follow it there: the check keeps the context matching what the user sees, and does not shield the user from a link
they made themselves. `ResolveInFolder` is the stricter test for a new file — only a folder holds files not yet there.

- **A page's own tools** test against their page's grant: the file system tools (`FsTool.TryResolve`), Search's
  `index_coverage`, the product graph's edits (only under the product folder).
- **Core's tools** are built per run in `AIService.GatherContext` with the run's scope (`RunScope.Of(page)`): the
  generation tools read pictures only from it and save only into a folder in it; `ask_local_model` reads only files in
  it; `open_page` refuses a parameter the page does not declare and holds each declared `PageParameter.OnDisk` value to
  it (a web address is no place on disk).
- **Something made with nowhere to go** — a generation tool asked to save nowhere, the assistant's `cut_out`, a video
  frame cut out — is kept under `DraftLocation` and handed to `IShellServices.HandleObject` as a `DraftFile`. The
  viewer for its kind claims it and opens it as a draft tab: `DraftBanner` over the page, its `DraftSaving` keeping the
  file where the user picks (a picture with its edits), and the tab becomes that file. The assistant is told only that
  the tab opened. Pages converted to Markdown open as a draft the same way, and a Markdown Package is unpacked into one
  to be worked on.
- **Drafts are the app's to clear, never Windows'.** `DraftLocation.Root` is under the app's local data, not the temp
  folder: Windows' clean-up takes files from a draft still open, and an unsaved draft is the only copy. Nothing deletes a
  draft when its tab closes — tabs and their paths come back with a workspace's last session and default tabs, and the
  assistant's conversations name the files it made. Instead, a tab opening a draft marks it used (`DraftLocation.Touch`),
  and two minutes after start-up — the disk left to the starting app — then daily for as long as it runs
  (`DraftSweeping`), `DraftLocation.Sweep`, off the UI thread, lets go of the drafts no tab shows or would open again
  (`WorkspaceManager.TabPaths`) that have gone untouched for `DraftLocation.Untouched`, then the least recently used past
  `DraftLocation.Most`. What the app can make again — readings of pages, a picture tool's results, a model's notes on a
  document — is kept under `CacheLocation`, each kind held under a budget of its own (`CacheLocation.Bound`). What one
  process works on while it runs — entries opened out of archives, fonts unpacked, message bodies written out, files
  fetched from a store, a large file's unsaved edits — is kept in a folder of that session's own
  (`CacheLocation.SessionFolder`), let go of by its owner, and by the next process to ask for that kind where the session
  ended without doing so. The temp folder holds only what lives for a single operation. Saving a document names any file
  it links to that could not be found, rather than saving it without them in silence.
- The terminal's `run_command` is a shell, which no path check can confine; it stays behind approval.

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
in-process layering — Core's csproj copies it beside the app (`ReferenceOutputAssembly=false` with
`HelperFolder="PrivilegeBridge"`, which the `CopyHelperExes` target in `Directory.Build.targets` reads); features
never see it.

**To add an elevated operation:** define the DTO in `Elevation.Contracts`, implement an
`IElevatedOperation` in the bridge, call it via `RunElevatedAsync`. **Never** `Process.Start` with
`runas` from a feature (a CLAUDE.md hard rule). Consumers today: Processes (kill/priority), SystemInfo
(services + machine env vars), WindowsRegistry (elevation-gated writes), and USB driver take-over
(`usb.bind` / `usb.restore` / `usb.restoreAll`).

An operation may carry its logic in an IO leaf the bridge references rather than in the bridge itself: the USB
operations are thin adapters over `Nexaflow.IO.Usb`'s `InboxWinUsbBinder`, so the take-over policy the client
checks before prompting is the same code the bridge enforces. Such a leaf must stay free of WPF and of anything
that would widen what the elevated process loads.

The bridge has one way in besides its pipe: `--usb-restore-all`, which the uninstaller runs as SYSTEM before the
app's files go. It takes no other input and restores only what the admin-owned machine ledger holds.

---

## Core Flows

### Startup

```
App.OnStartup
  → AppDataFolders.ForThisProcess()                         ← %AppData% + %LocalAppData%\Smile\nexaflow, or the root
        NEXAFLOW_CONFIG_DIR sets apart
  → ShellLogging.Open(the folders, --uiTest, --timing)      ← the process's logs, before anything that writes to them:
        the exception hooks (the UI thread's judged by DispatcherFaultPolicy), WPF's binding trace under --uiTest,
        the single-instance guard, every startup step
  → single-instance IPC listener                            ← a later launch never waits behind start-up
  → new ShellHost(options (the folders, --reset, --uiTest, the logs), ShellPlatform(WPF UI thread, clipboard, folder
        watcher, ApplyAppearance, page views)):
      → new ConfigManager(the folders, its logger)          ← the session's configuration, handed by constructor to
            whatever reads or writes a config
      → the session's log turned on or off by ShellConfig.SafetyLogging, once it has loaded
      → the feature catalogue, its index cached under the configuration (not yet built)
      → register GLOBAL configs: ShellConfig (+ App applies the theme), WorkspacesConfig,
        FileMapConfig, ExternalAppsConfig
      → new LanguageManager(Languages\), Select(ShellConfig.Language), Str pointed at it   ← records the language;
            loads nothing — the first string or help page asked for reads the pack. After the first-run wizard
            the language is selected again, before the first window's strings resolve
      → HelpLibrary over the language, for the Help page and the assistant's help tools
      → the AI host client, model residency, local tools
      → new ProviderManager(config, Activities, model store, residency, app-wide services)   ← the provider pool
      → the component registry (ExternalDependencyRegistry) over the catalogue and the AI host's reading, and the page
            views made over both
      → for each saved workspace: temp AiConfig ← config.LoadFrom(WorkspaceManager.WorkspaceDir(config, name))
            (need each workspace's columns to know which provider DLLs to load)
      → providers.LoadConfigured(⋃ AssemblyFileName across all workspaces' AiConfigs)
      → new WorkspaceManager(wcConfig, providers, config)   ← the saved Workspaces list ONLY (no runtimes), each
            workspace listed in the configuration (Workspace.ListIn), so its folders and own configs resolve
      → new FeatureManager(catalog, config, ui, location backends, the logs)  ← FeatureCatalog.Initialize: load the cached
            discovery index (no feature DLLs loaded) or, after an app update, one full rescan. Feature
            assemblies load + activate lazily later (first use, or the post-paint background warm-up); the file
            system's activation sets up FileMapManager / ExternalAppRegistry
      → the app-wide tools provided to the feature injection: the help pages, the picture and page tools
      → WorkspaceRuntimeFactory + WorkspaceRuntimes          ← how runtimes are built, and the live ones
      → the machine's locations looked for, off the UI thread
  → JumpList
  → pick startup Workspace (Workspaces[0], or the one named by --context "Name")
  → WorkspaceRuntimes.Open(workspace)                       ← its runtime: running, resumed, or built by
        WorkspaceRuntimeFactory — workspace.EnsureSharedServicesLoaded, AIService + ShellServices +
        FeatureScope made with it, AcquireProviderSet(pool) → register providers, mount its connections
  → new MainWindow(WindowServices, runtime)                 ← window on ONE runtime at a time
      → ShellViewModel(WindowServices, runtime)   (CurrentRuntime = runtime)
          → ribbon binds runtime.Workspace → RibbonViewModel.SetWorkspace → Load() (shared layout)
          → OpenWorkspaceTabs()   ← the session it left when resumed from dormancy, its startup tabs otherwise
  → win.Show(); its first frame → ShellHost.FirstWindowDrawn (models load ahead); ShellHost.StartBackground (warm-up)
  → a throw from the session's start or the first window ends the launch (StartupFailure): recorded in the crash
        log, single-instance guard released, a message naming the log unless the launch is unattended (--prestart,
        --uiTest, --timing), exit code 1 — never a windowless process that swallows every later launch

  (A later launch of a workspace already live = a new window on ITS runtime. Tear-off / "open in new window"
   join the SAME runtime. --prestart launches the windowless resident daemon: the session opens, NO runtime is
   created; a later click / JumpList signal opens a window via the IPC listener.)

  (While the daemon has no window open it keeps a StandbyWindow: once the post-paint warm-up finishes, and
   again a moment after the last window closes, it builds Workspaces[0]'s runtime on standby
   (WorkspaceRuntimes.OpenStandby — that workspace's one runtime, no window counted, no session recorded, no model
   weighed for loading ahead) and its first window, shows that window cloaked, disabled and off the taskbar so it
   lays out and draws without being seen or taking focus, then hides it. A plain launch of that workspace claims it —
   WorkspaceRuntimes.Adopt weighs its models and the window is shown in place — so the click costs a show, not a
   build. Any other window opening discards it, ending its runtime; the setup wizard, when due, runs first and no
   standby is built ahead of it. Options → Shell → "Keep a window ready" (ShellConfig.KeepWindowReady, offered only
   beside Start with Windows) turns it off, read each time a standby would be built.)
```

### Opening a tab

```
User clicks ribbon button
  → RibbonControl.InternalRibbonActionCommand → ShellViewModel.OpenRibbonItem(item)
  → _shellServices.OpenTab(item.PageKind, item.PageParams)   ← persisted items carry only PageKind/params
  → PaneView asks its pane's PageViews.ViewOf(page)    ← ViewModelFactory run, then the [ViewFor] view made, on first show
  → ActiveTab = page, breadcrumbs updated

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
      → safe tools auto-run; approval-required batches and plans need per-batch/plan approval
        through the IAIResponseHandler (on most pages the AI bar's own line, with the reply card above it)
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
  → FileActionViewModel awaits action.PerformActionAsync(path)
  → e.g. ShowMarkdownAction.PerformActionAsync(path)
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

## Large-file reading

There are four established strategies; pick the one whose access pattern matches your data shape before inventing a
fifth. Each reader's *strategy* is deliberately feature-specific (the data structure differs), but the mechanical
leaves live in `Nexaflow.IO.Common` — `EncodingDetector` (BOM/UTF-8 sniff; Tabular's detector, the canonical one) and
`FileChangeWatcher` (the debounced `FileSystemWatcher` wrapper used by Logs and Text). Reuse those rather than
re-rolling them.

| Strategy | Canonical reader | When |
|----------|------------------|------|
| Tail-first + background head-load | `Logs/ViewModels/LogViewModel.cs` | append-only files where the recent end matters most |
| Head-first window + placeholder padding for scrollbar | `Text/ViewModels/TextViewModel.cs` | top-of-file-first text; line index built up front |
| Full-rescan per window (no byte anchors) | `Tabular/RowWindowReader.cs` | row data; `StreamReader` buffering makes `BaseStream.Position` unreliable for cross-call seeks |
| Seek-by-item via byte-offset index | `Json/JsonFileLoader.cs` | random access to structured items |
| One rectangular selection per window | `IO.Hdf5/PureHdf/PureHdfSource.cs` (`ReadBlock`) | N-D arrays: a table window is one hyperslab read, and `Hdf5Selection.Walk` streams a whole dataset in C order through bounded memory |

---

## Installer

The installer is its own solution, `NexaflowSetup.slnx`: `nexaflowSetup` builds the MSI that carries the app (and fails
on a broken product tree — `ValidateSnaplinks`), `aihostSetup` the MSI that installs the AI host as the machine's service
(installed by the bundle where its Install page's `InstallAiHost` asks — [local-ai.md](local-ai.md)), and
`nexaflowBundle` the WiX Burn bundle released as the setup exe.
The bundle's chain puts what the app needs from Windows in place before the MSI. Each prerequisite is an `ExePackage`
whose installer is downloaded only when a registry search finds the machine without it; it is permanent (uninstalling
Nexaflow leaves it) and vital (a failure stops the chain before the MSI).

| Prerequisite | Needed by | Detected by |
|---|---|---|
| .NET 10 Desktop Runtime (x64) | the app, which is framework-dependent | the bundled patch's value under `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App`, 32-bit view |
| Visual C++ v14 Redistributable (x64) | the native libraries — llama.cpp and ONNX Runtime in the AI host, whisper, assimp — which import `vcruntime140`, `msvcp140` and `vcomp140`; a clean Windows has none of them | `Installed` = 1 and a minimum `Minor` under `HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64`, 64-bit view |

A DLL needs a Visual C++ runtime at least as new as the toolset that linked it, so the minimum is the newest linker
version in those DLLs' PE headers; a native dependency built with a newer toolset raises it in `Bundle.wxs`. Every
payload is pinned by hash, size and version to a versioned download URL, never a permanent `aka.ms` link: Burn refuses
a download whose hash differs, and a permanent link moves to each new release. Exit code 1638 (a same-or-newer version
is already installed) counts as success, and 3010 schedules a reboot.

`Nexaflow.Tests.Sandbox` copies the host's Visual C++ runtime into the sandbox so it can run the app, so a missing
prerequisite does not show there; checking a clean install of the bundle takes a plain `.wsb` sandbox.

---

## Architectural Findings

Point-in-time findings do **not** live in this document — an embedded findings list rots invisibly. Current findings, each with status tracked inline:
[arch_review_2026-07.md](arch_review_2026-07.md). Per-component product status (what exists, what is
tested, what is AI-ready) lives in the product tree — `.product/tree.json`, queried via the
product-folder skill.