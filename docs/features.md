# Building a Feature

A feature is a class library (`Nexaflow.Features.MyFeature`) that references only `Nexaflow.Features.Common` and WPF. Core never imports your types directly — everything goes through the contracts in `Features.Common`.

> This is the map and the rules. For a working template, read a real sibling feature (pointers below) and the
> target interface's own XML doc-comment — those are authoritative and never drift from the code.

---

## Checklist

| Step | Required | What |
|------|----------|------|
| Create project | yes | `net10.0-windows` WPF class library, reference `Features.Common` |
| `IPageRegistration` | yes | Factory that produces a `Page` (expose `static string StaticPageKind`) |
| `IPageView` on your `UserControl` | recommended | Shell lifecycle handle: `ViewModel` property + `Reinitialize` |
| `IPageViewModel` on your ViewModel | recommended | AI pipeline contract: `GetContext`, `GetClientTools`, `GetContextObject` |
| Add project reference in `Nexaflow.Core.csproj` | yes | So the feature DLL ships and `FeatureManager` reflection-discovers it at startup |
| Add a default-button entry in `src/Nexaflow.Core/Ribbon/default-ribbon.json` | optional | Puts a button on the default toolbar (loaded by `RibbonLayoutService.LoadDefaults`) |
| `IFeatureConfig` | optional | Persisted settings, free Options panel UI |
| `IQueryHandler` | optional | Handle AI input bar text |
| `IFileAction` / `IFolderAction` | optional | File browser context actions |
| `IKeyboardHandler` / `IDropTarget` | optional | Global keyboard shortcuts / file drag-drop |

The wiring steps that used to fail silently are now **enforced**: `Nexaflow.Tests.Features/Architecture/FeatureTouchPointTests`
goes red naming any feature directory missing its `Nexaflow.Core.csproj` or `Nexaflow.Tests.Features.csproj`
ProjectReference, and any viewer sample extension missing from `default-filemap.json`; the layering rules
(no Core reference, no dispatcher) are enforced by `ArchitectureRulesTests` in the same folder. Also update
the **product tree** — add the feature's node(s) under `features` in `.product/tree.json` (see the
product-folder skill); the docs deliberately don't keep a feature inventory anywhere else.

---

## Project setup

A `net10.0-windows` WPF class library referencing **only** `Features.Common` (plus the `Nexaflow.Visuals.*` /
`Nexaflow.IO.Common` shared libs when needed). Add a `<ProjectReference>` to it from `Nexaflow.Core.csproj` so
the DLL ships and `FeatureManager` reflection-discovers it at startup. Convention: `*TabRegistration.cs` (a few
are `*PageRegistration.cs`) at the root, `ViewModels/`, `Views/`. Copy the csproj from a sibling feature (e.g.
`Nexaflow.Features.Text`).

---

## The entry point — `IPageRegistration`

The one required contract. `FeatureManager.RegisterFeatures()` discovers it by reflection (reading
`static StaticPageKind` without instantiating) and builds one instance **per `WorkspaceRuntime`**, injecting:
any `IFeatureConfig` from the **same assembly**, the runtime's `IShellServices`, and its `IAIService`.

- `CreatePageDefinition` must be **cheap and side-effect-free** — the shell builds definitions speculatively
  just to read `Title`/`Icon` (menus, the AI "add context" list) and may discard them. Build the view-model and
  view **inside the `ContentFactory` closure**, which runs lazily on first activation.
- Advertise `Parameters` so the shell/AI can describe how to open the page; set
  `CanBeContextItem` / `CreatePageDefinitions` for pages (or page variants) offered in those menus.
- Canonical examples: `Nexaflow.Features.Text/TextTabRegistration.cs` (plain single-file viewer);
  `Nexaflow.Features.WindowsFileSystem/FileSystemPageRegistration.cs` (multi-variant + context item).

---

## View & ViewModel — `IPageView` / `IPageViewModel`

`IPageView` on the tab `UserControl` exposes its `ViewModel` and the `Reinitialize(pageParams)` hook — called
on first activation **and** whenever the shell routes the tab a new param set (including re-clicking the active
tab). `IPageViewModel` on the ViewModel is the AI-pipeline contract (`GetContext`, `GetClientTools`,
`GetContextObject`, plus security-scope / readiness defaults — see its XML doc). Both interfaces have sensible
defaults, so a context-only page overrides almost nothing.

---

## AI input bar — `IQueryHandler`

Intercepts the AI input bar before text reaches the LLM. Handlers are **auto-discovered** (the
`FeatureCatalog` index) and built per `WorkspaceRuntime` — there is **no registration step**. Scope is
expressed inside `CanProcess(input, pageVm)`: the active page's ViewModel is passed in, so a page-scoped
handler is a type check returning 0 for pages it doesn't own. `Symbol` claims a single-char prefix
for exact routing (e.g. `>` → console); otherwise `CanProcess` returns a 0–1 score and
`IAIService.DisambiguateToolSelection` breaks ties via the LLM. `ProcessAsync` returns null (handled silently)
or a string (shown in AI Chat). Canonical example: `Nexaflow.Features.WindowsSearch`.

The terminal also shows the richer chat-bar hooks — `IChatKeyHandler` (history/Tab-completion),
`IChatInputPreview` (echo a `>` command), `IChatDropHandler` (dropped file → quoted path); see the interface
reference.

---

## Calling the shell — `IShellServices`

The active runtime's handle (injected into registrations and file actions): `OpenTab` / `CloseTab` /
`FindTab`, notifications + prompts, `QueueBackgroundTask`, `WatchFile`, `RunOnUiAsync`,
`DiscoverImplementations<T>`. To change a tab's own title/breadcrumbs/params, mutate the observable `Page`
directly. The full, documented surface is in `Services/IShellServices.cs`.

- **`OpenTab` param-matching:** the shell reuses an existing tab whose `PageKind` matches and whose
  `PageParams` are compatible (all requested keys match) — calling `Reinitialize` on it — else creates one. So
  keep `Page.PageParams` in sync with the tab's current state.
- **Viewer breadcrumbs:** a file/media viewer must **not** hand-roll its trail. Call
  `page.SetFileBreadcrumbs(path, title)` (→ `D:\temp › report.csv`) or
  `page.SetMultiFileBreadcrumbs(paths, summary)` (→ `D:\temp › 6 images`; the parent crumb shows only when all
  paths share a folder). Both clear-then-rebuild, so they're safe to re-call from `Reinitialize`. The parent
  crumb opens a file-explorer tab at that folder. See `FileBreadcrumbs.cs`.

---

## Persisted settings — `IFeatureConfig`

A plain POCO; `FeatureManager` discovers it, loads it from `%AppData%\Smile\nexaflow\{ConfigName}\`, and injects
it into your registration's constructor. The Options panel renders a property grid for free from these
attributes (all in `ConfigAttributes.cs`):

| Attribute | Effect |
|-----------|--------|
| `[ConfigDisplayName("Label")]` | Row label in the Options grid |
| `[FolderPath]` / `[FilePath(".ext"…)]` | TextBox + browse button + existence validation |
| `[ListSource(type, method)]` | ComboBox from a static `IEnumerable<string>` method |
| `[DisabledIfSet]` / `[DisabledIfNotSet]` | Grey out an editor based on a sibling property's value |
| `[CustomControl(type)]` | Replace the section with a custom `UserControl` (+ `ICustomConfigApply` to save) |

Configs are **global** by default; add `[WorkspaceScopedConfig]` for a per-workspace one, `[MandatorySetup]` to
add it to the first-run wizard. Implement `IConfigMigration` to fix shape changes across a version bump.

---

## File browser actions — `IFileAction` / `IFolderAction` / `IFileCreateAction`

Context actions discovered by `FileSystemFeatureRegistry` (in Core, **not** `FeatureManager`) across Core and
every feature. Viewer-opener actions live in the owning feature; system actions (copy/rename…) live in
`Nexaflow.Core.FileActions`. The constructor receives `IShellServices` by injection.

- `IFileAction` is matched to a file by hierarchical `ExperienceId` (e.g. `/binary/installer`) via
  `FileMapManager`; parent ids satisfy child experiences, and the user maps experiences→file-types in Options.
  Implement `ICacheable` unless the action's identity depends on constructor args (then provide
  `GetReinitParams` + a static `Rehydrate`).
- `IFolderAction` is matched **structurally** (folder-name / contained-file / contained-folder globs, with an
  optional match-percentage), not by experience id.
- Canonical examples: `Nexaflow.Features.Images/FileActions/` — `ShowImageAction` (file),
  `ImageFolderAction` (folder).
- **Double-click ("default") action** is resolved by `DefaultFileOpener`: the highest match-specificity
  internal action, else the Windows shell `open` verb (internal beats shell at the same specificity).
  A user can override this per extension in **Options → Default Actions** (internal viewer / external app /
  Windows verb), stored in `DefaultActionsConfig` and consulted first via `DefaultActionRegistry`.
- **External "Open with" apps** are user-defined launch buttons (`ExternalAppsConfig` / `ExternalAppRegistry`,
  edited in Options → External Apps). Windows-registered handlers surface as live `ShellVerbAction`s while the
  "Show Windows-registered handlers" toggle is on; turning it off can import them (deduped by exe) into the
  External Apps list. There is no HKCR file-map scan — internal viewers match by PerceivedType/ContentType
  live via `ShellTypeResolver`.

---

## Typed AI context — `IContext`

Return a strongly-typed `IContext` from `IPageViewModel.GetContextObject()` when query handlers need structured
data beyond the `GetContext()` string. Handlers gate on it with a type check — see `FileSystemContext` (in
`IContext.cs`), consumed by the Windows Search query handler to pick the search root/scope.

---

## Keyboard & drag-drop — `IKeyboardHandler` / `IDropTarget`

Implement on the `UserControl` or ViewModel. The shell calls `CanProcessKey` / `CanAcceptDrop` before acting,
so checks stay side-effect-free; it only queries the **active** page.

---

## AI client tools — `IClientTool`

Tools a page exposes to the agent via `IPageViewModel.GetClientTools()`. Use `DelegateClientTool` for
one-liners, or implement `IClientTool` for richer ones. `ToolSafety.ReadOnly` tools auto-run; mutating ones are
approved via `IToolApprovalCoordinator` before running, and each `ToolResult` is fed back to the model. Return
an error `ToolResult` (don't throw) for an expected failure. Canonical example:
`Nexaflow.Features.Video/ClientTools/VideoCaptureFrameTool.cs`.

---

## Tab parameters convention

Params are `Dictionary<string, string>` — keys lowercase, values strings. `Page.PageParams` should always
reflect the tab's current state (set it after in-tab navigation so `FindTab` and breadcrumb restore work).

| Feature | Params |
|---------|--------|
| Search | `query`, `root` |
| Markdown | `path` |
| Code | `path`, `ast` (optional — member to jump to) |
| Notebook / Model3D / Compressed | `path` |
| Images | `paths` (pipe-separated), `view` (`carousel` \| `album` \| `explore` \| `collage`), `scope` (`folder` = whole-folder view) |
| Audio | `paths` (pipe-separated queue), `index`, `autoplay`, `scope` (`folder` = whole-folder queue) |
| Font | `paths` (pipe-separated font files; omit for the standalone "System Fonts" compare mode) |
| ProductManager | `path` (folder holding/initialising `.product/`) |
| Projects | *(none)* |
| ProjectDetail | `folder` |

---

## AI capabilities — `IAIService`

Injected like `IShellServices` (the active workspace's instance). Use it from query handlers and tool code:
`DisambiguateToolSelection` (LLM picks a handler), `RunAgentAsync` (the agent loop), `RunAnalysisAsync` (a
one-shot completion), plus conversation load/save and artifacts. The full, documented surface is in
`IAIService.cs`.

---

## Interface reference — `Features.Common`

Every contract a feature implements or consumes, grouped by concern. All live under
`src/Nexaflow.Features/Nexaflow.Features.Common/` (sub-folder noted per group). Each interface's own XML
doc-comment is the authoritative, fuller description. Most are discovered by reflection and built per
`Workspace` — **except** file/folder actions, which `FileSystemFeatureRegistry` discovers (not `FeatureManager`).

### Pages & tabs
| Interface | What it's for |
|---|---|
| `IPageRegistration` | Advertises one page kind; the cheap `CreatePageDefinition` factory the shell discovers by reflection. |
| `IPageView` | Implemented by a tab `UserControl` — exposes its ViewModel and the `Reinitialize` lifecycle hook. |
| `IPageViewModel` | The AI-pipeline contract on a ViewModel: context string, client tools, typed context, security scope. |
| `IContext` (+ `FileSystemContext`) | Marker for a strongly-typed context object a page offers to query handlers. |
| `IContextItemReceiver` | A page VM that can receive another open page as a live context item (the conversation page). |
| `IAirspaceContent` | A page hosting a native HWND child (e.g. WebView2) that must hide itself while a modal overlay covers it. |

### AI pipeline
| Interface | What it's for |
|---|---|
| `IAIService` | Per-workspace AI service: the agent loop, conversation store, handler scoring, analysis. Injected like `IShellServices`. |
| `IClientTool` | A locally-executed tool a page/shell exposes to the agent (use `DelegateClientTool` for one-liners). |
| `IAIResponseHandler` | The sink the agent loop drives for all response UI (progress, approval, final). Default = shell overlay; a VM can implement it to render inline. |
| `IChatEngagement` | A page VM opting to host AI responses in an inline banner instead of the modal overlay. |
| `IQueryHandler` | Intercepts AI-input-bar text before the LLM (`Symbol` prefix / `CanProcess` score). Global or page-scoped. |

### AI input bar — key / preview / drop handlers (`Services/`)
| Interface | What it's for |
|---|---|
| `IChatKeyHandler` | Active page claims key presses in the AI bar (e.g. terminal history, Tab completion). |
| `IChatInputPreview` | Active page mirrors what's being typed (e.g. terminal echoing a `>` command at a faux prompt). |
| `IChatDropHandler` | Turns something dropped on the AI bar into inserted text (a dragged file → its quoted path). |

### Shell services & lifecycle (`Services/`)
| Interface | What it's for |
|---|---|
| `IShellServices` | The active workspace's shell handle: open/close/find tabs, notifications, prompts, `WatchFile`, `RunOnUiAsync`. |
| `IFileWatch` | Handle from `IShellServices.WatchFile`; `Enabled = false` holds + coalesces callbacks, dispose to unwatch. |
| `IBackgroundTask` | Self-contained background work handed to `IShellServices.QueueBackgroundTask` (runs off the UI thread). |
| `IShellAware` | A custom config-editor control that needs the shell handed to it (for the themed file/folder pickers). |
| `IGenericObjectHandler` | "Do the default thing with this object" (open a path / URL) — the non-drag sibling of `IDropTarget`. |
| `IKeyboardHandler` | Claims global keyboard shortcuts; queried against the active page before the key is consumed. |
| `IDropTarget` | Accepts file drag-drop; resolved from the active page by the file browser. |

### File & folder actions (`FileActions/`) — discovered by `FileSystemFeatureRegistry`
| Interface | What it's for |
|---|---|
| `IFileAction` | Context action on file(s); matched by hierarchical `ExperienceId` via `FileMapManager`. |
| `IFolderAction` | Context action on folder(s); matched **structurally** (name / contents globs), not by experience id. |
| `IFileCreateAction` | A "new file/folder of type X" action for the current folder. |
| `ICacheable` | Marker: the action has exactly one instance per `WorkspaceRuntime`, so it's cached + auto-listed. Non-cacheable actions are rehydrated instead. |

### Folder viewlets (`Viewlets/`)
| Interface | What it's for |
|---|---|
| `IFolderViewlet` | Inline view shown above the file list when the open folder matches (Git status, .NET build). |
| `IViewletController` | Host handle passed to a viewlet's `CreateView` — read/set its display mode. |
| `IViewletAiSurface` | Optional: a viewlet view feeding folder-specific context + tools into the host page's AI surface. |
| `IDynamicFolder` | Declares that certain files are browsable like folders (archives) — the explorer descends into them. |

### Ribbon pinning (`Ribbon/`)
| Interface | What it's for |
|---|---|
| `IRibbonPinHandler` | Turns a dragged foreign payload (a file action, a dropped URL) into a ribbon button (format-matched). |
| `ITabPinHandler` | Snapshots a dragged tab into a ribbon button that re-opens its exact state (matched by tab page kind). |
| `IRibbonItemExecutor` | Runs a ribbon button whose click does something other than open a tab (e.g. a pinned file action). |
| `IRibbonExecutionContext` | Shell context handed to an executor/pin-handler at click time: selection, error/confirm, self-remove. |
| `ISelectionProvider` | A page view exposing its current file/path selection so the shell can read it without the concrete type. |

### Config (`ConfigAttributes.cs`, `IFeatureConfig.cs`, `IConfigMigration.cs`)
| Interface | What it's for |
|---|---|
| `IFeatureConfig` | Marks a POCO as a config section (global by default; `[WorkspaceScopedConfig]` makes it per-workspace). |
| `IConfigMigration` | Opt-in hook to fix up shape changes when a config is migrated forward across an assembly-version bump. |
| `ICustomConfigApply` | A custom Options control participating in the Save flow (`Apply()`). |
| `IConfigChangeTracker` | A custom Options control reporting whether it has unsaved changes (else it's assumed always-dirty). |
| `IConfigValidation` | A custom Options control reporting whether its state is valid (blocks Save while any section is invalid). |

### Theming
| Interface | What it's for |
|---|---|
| `IThemeContribution` | A feature ships fallback theme resources (region tokens / `Scene.*` templates) without Core referencing it. |
