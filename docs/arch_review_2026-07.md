# Architecture Review — 2026-07

_Review date: 2026-07-08. **Open rows re-verified against the code 2026-09-05** — see the freshness rule
below. Scope: full re-examination — product tree (843 nodes), all docs, and the implemented code across
the 52-project solution. This file supersedes [arch_improvements.md](arch_improvements.md) (2026-05-31,
updated 2026-06-20); the still-open items from that review are carried forward here (§C1)._

**Reviewing lens.** 95%+ of changes are made by Claude, so "maintainability" is scored for an AI agent, not a human. For an agent the priorities invert from a human codebase: **docs are load-bearing** (an agent reads them as ground truth every session — a wrong doc is worse than no doc), **rules need mechanical enforcement** (an agent can't accumulate tribal knowledge; a red test is the only reliable guardrail), **silent failure modes matter most** (a green build hiding a broken feature wastes whole sessions), and **context-window economy** counts (god files force loading 70 KB to change one concern).

## What's healthy — verified, don't re-audit

The codebase is in noticeably better shape than its own docs claim. Confirmed clean in this review:

- **Layering holds everywhere.** No feature references Core or another feature (all 29 csproj checked); providers reference only Providers.Common; Core touches concrete feature types only in `App.xaml.cs` registration. The Compressed backend split is textbook — the three codec backends reference only `IO.Common` (where `IArchiveHandler`/`IStreamCodec` live), not the Compressed feature, and are discovered by reflection.
- **Dispatcher rule: zero violations.** No `Application.Current.Dispatcher` / `Dispatcher.CurrentDispatcher` in any feature. View code-behind uses its own inherited `Dispatcher` (allowed); VMs marshal via `RunOnUiAsync`.
- **Colour rule: ~95% compliant** — theme-token-with-fallback and alpha-wash patterns applied well (e.g. `VirtualizedRowsControl` *throws* if a token is missing). Five localized violation clusters remain (§E4).
- **Prior extractions landed**: `TbBtn`/`TbToggle`/`TbSep` live once in `Styles.xaml` (referenced by 15 views); `SizeFormatter` used by ~12 sites; `ToolArgs` used by all tool features (one residual, §C4); `EncodingDetector`/`FileChangeWatcher`/`WatchFile` adopted; `ProjectService.Ops` static locator is gone.
- **Resource discipline is real**: lazy feature loading via `FeatureCatalog`, tab-close disposal applied consistently, media/timers pause on deactivate, WMI always off-UI, virtualization on every big data surface (file list, search, JSON tree, tabular, logs), no blocking `.Result`/`.Wait()` on UI paths, zero TODO/HACK markers in Core.
- **Contract XML docs are genuinely authoritative** — threading contracts, failure modes, and gotchas documented on `IShellServices`, `IPageRegistration`, `IPageViewModel`, `IClientTool`. This is the single strongest AI affordance in the repo; protect the bar (one thin spot: §H4).
- **The agent loop** (`AIService.RunAgentAsync`, ~188 lines) is a clean, commented, single-concern state machine with sensible guards (max steps, repeated-batch detection, tool ranking). Leave it alone.

## How to read this — the status column rots, so every row now says how to re-check it

This file was written as a dated snapshot and then used as a live tracker, which is the one thing a
snapshot cannot be. By 2026-09 two rows said "open" for work that had shipped and one said "958 lines"
about a file that had grown to 1,120 — **the table was wrong in both directions, and nothing in the repo
would ever have said so.**

The fix is not a fresher table. It is that **an open row carries the command that re-answers it**, so
the state is derived rather than remembered, and a stale row costs one command to catch instead of a
reading of the code. `$nfi` below is `tools/graph-cli/nfi.exe`; a check that prints nothing (or `No graph
nodes match`) means the row is still open.

Two things deliberately do **not** live here:

- **Product-shaped work** — a missing capability, a panel with no tests, an un-themed view — belongs in
  the product tree, which is already gated and already forward-looking (`$nfi query --status should`).
  Nothing below is product-shaped; this file is for structure that no node names.
- **Closed rows.** They moved to [§Closed](#closed--kept-for-the-reasoning-not-the-status) so the live
  table stays short enough to read. The reasoning behind each still sits in its lettered section.

Priority bands: **P1** = do first (bugs, actively-misleading docs, lock-in-now guardrails); **P2** =
high-value structure; **P3** = worthwhile polish.

## Open — every row re-verified 2026-09-05

| # | Finding | State on 2026-09-05 | Re-check with | Effort | Band |
|---|---------|---------------------|---------------|--------|------|
| C3 | Two hand-rolled tool aggregators duplicate security-context routing | open — no merger exists | `$nfi graph search ClientToolMerger` | M | P3 |
| C5 | `IShellServices` is a 33-member god-interface | open — neither facet peeled | `$nfi graph search IShellDialogs` | M | P3 |
| C6 | Viewlet quiesce runs through a **public settable** delegate — left null it silently no-ops a *safety* contract | open — still `{ get; set; }`, still wired from `FileSystemView.RefreshViewlets` | `$nfi graph grep "QuiesceFolderHandler" --mode content` | S | P3 |
| C7 | `Page.SecurityRisk` denormalized; `GetOrCreateContent()` returns a blank `UserControl`; `BreadcrumbSegment`'s exclusivity unencoded | open | read `Features.Common/Page.cs` | S | P3 |
| C8 | No one table mapping contract → discovery surface → instantiation rule | open — nothing in Architecture.md | `grep -n "discovery surface" docs/Architecture.md` | S | P3 |
| D1 | `ShellViewModel`: the AI-input cluster is still in the god object | open — `OverlayCoordinator` landed, `AiInputRouter` did not. **1,503 lines** (was 1,545) | `$nfi graph search AiInputRouter` | M | P2 |
| D2 | `ShellServices` — the second god object | **worse: 1,120 lines** (was 958 at review). Three leaf concerns still unsplit | `wc -l src/Nexaflow.Core/Services/ShellServices.cs` | M | P2 |
| D3 | RibbonEditor: procedural code-behind | open — **749 lines**, unchanged | `wc -l src/Nexaflow.Core/Controls/RibbonEditor.xaml.cs` | L | P3 |
| D4 | Window-position constants duplicated | open, and **three copies now**: the `const` in `PositionWindow`, `MainWindow.xaml.cs`, and a `TopBarHeight` GridLength in every `Colors.*.xaml` — the theme resource is the obvious single home | `$nfi graph grep "TopBarHeight" --mode content` | S | P3 |
| E1 | Modal-overlay scaffold copy-pasted across 6 features | ◐ primitive shipped and spreading — `ConfirmationRequest` at 13 sites; the rest migrate on touch | `$nfi graph grep "ConfirmationRequest" --mode content` | M | P2 |
| E4 | Colour literals outside the theme layer | **nearly closed** — all five original clusters are done. What is left is 3 sites, and the biggest is `Core/MainWindow.xaml` itself: the shell hard-codes an accent while every feature is forbidden to | `$nfi graph grep "#[0-9A-Fa-f]{6}\"" --mode content --limit 400`, then **exclude** `Themes/` · `Tokens.xaml` · `Colors.*.xaml` · tests — a raw count reads the theme layer as debt | S | P3 |
| E5 | 5 process-global mutable registries in WindowsFileSystem | open — all five still `static … Instance` | `$nfi graph grep "public static .* Instance" --from product:win-file-system --scope owned --mode content` | M | P3 |
| E6 | ViewModel outliers | open and grown: FileSystem **2,019**, Json **1,499**, Text **1,344**, Product **964** | `wc -l` on the four | M–L | P3 |
| E7 | Dynamic breadcrumb rebuild hand-rolled ~5× | open — no shared helper | `$nfi graph search SetNavBreadcrumbs` | M | P3 |
| F7 | Aria: `[Required]` `ApiKey` is collected, stored and **never sent**; AD email PII per frame; no correlation id | open — `AriaConfig` is constructed only by its tests, so nothing carries it to the provider | `$nfi graph grep "AriaConfig" --mode content` | S–M | P3 |
| F8 | The `LlmMessage` → SDK mapping has no test seam | open — no `InternalsVisibleTo` on any provider | `$nfi graph grep "InternalsVisibleTo" --mode content` | M | P3 |
| G1 | Image album non-virtualized | ◐ mitigated 2026-07-08 (batched marshalling + 1024-thumb cap); UI virtualization still open | read `ImageView.xaml` for a `VirtualizingStackPanel` | M | P2 |
| G5 | Chat timeline realizes every message | open — `MessageList` is still a bare `ItemsControl` | `$nfi graph grep "MessageList" --mode content` | S–M | P3 |
| G6–G8 | Eager conversation bodies, pre-paint startup reads, hex paint churn, WMI double-query, `async void` | open — `QueueNugetCheck` is still `async void` | `$nfi graph grep "async void QueueNugetCheck" --mode content` | S–M | P3 |
| H1 | "AI Ready": no recipe for making a page AI-ready | open — nothing in features.md | `grep -n "AI-ready" docs/features.md` | M | P2 |
| H4 | `IFolderViewlet` members carry no `<summary>` | open | `$nfi graph code code:src/Nexaflow.Features/Nexaflow.Features.Common/Viewlets/IFolderViewlet.cs#T:IFolderViewlet` | S | P3 |
| B4 | Colour-literal Roslyn analyzer | **deferred on purpose** — do E4 first so the allow-list is small | — | M | — |

### Closed — kept for the reasoning, not the status

All verified done; each one's *why* is still in its lettered section below.

**2026-07-08:** A1–A5 (the docs truth pass) · B1–B3 (architecture tests, touch-point tests, Providers in
CI) · C1 (`ClientBlockParser`/`ParsedAssistantTurn` → `Core/Services/Agent/`, `OpenPageRequest` →
`Core/Models/`) · C2 (`ClientToolBase`) · C4 (`*_file` names, `ToolArgs.IntOrNull`) · E2/E3 (converters
hoisted — re-verified 2026-09-05, one copy each) · F1 (cancellation ×4 + Gemini `ct`) · F2 (Aria dispose)
· F3 (`LlmStreamRunner`) · F4 (provider contract documented as deliberate) · F5 (per-model capabilities)
· F6 (DPAPI secrets) · G2 (ribbon leak) · G3 (`WindowMinimizeWatcher`) · G4 (VFS 512 MB LRU) · H3
(add-feature skill + fast-loop docs).

**Since, and never recorded here:** **H2's test-project split has happened** — the monolithic
`Tests.Features` is now seven suites (`.Viewers`, `.WindowsOS`, `.Architecture`, `.Common`, plus
`Tests.Components` and `Tests.Initiatives`), which is the **L**-effort fix this file deferred as too
expensive. The per-feature `[TestCategory]` mitigation it proposed instead is moot.

---

## A. Docs are wrong in ways that will misroute an agent (P1)

> **✅ Actioned 2026-07-08 (whole band).** CLAUDE.md + Architecture.md rewritten in the
> `Workspace`/`WorkspaceRuntime` vocabulary (with a "naming history" note pinning the frozen compat
> strings); dead instructions removed (features.md checklist → `default-ribbon.json`;
> `RegisterQueryHandler` language deleted from both docs *and* `IQueryHandler.cs` itself); the five
> missing projects documented and the solution layout re-tiered; an Elevation section + CLAUDE.md
> hard rule added; the stale SoC list replaced with a pointer here. Beyond the fixes, the docs were
> **de-listed**: feature inventory, per-component status, and test coverage now defer to the product
> tree (`.product/tree.json`) as the single source of truth, and the product-folder skill gained
> ready-made fast-query recipes so an agent can answer inventory/status questions without loading the
> ~900 KB tree into context. Two stale claims found during the rewrite were also fixed: the AI persona
> is per-`Workspace` (not global), and `[WorkspaceScopedConfig]` per-workspace feature configs exist.

For this codebase, doc accuracy **is** architecture: CLAUDE.md tells every session to read the docs before exploring, so a confidently-wrong doc gets injected into every change.

### A1. The Workspace vocabulary is inverted

Commits `501e351`/`9cf53a5` renamed logical `Profile` → `Workspace` and runtime `Workspace` → `WorkspaceRuntime`. The rename is **complete in code** (zero leftover `Profile` identifiers; `Models/Workspace.cs` = saved config, `Models/WorkspaceRuntime.cs` = runtime; `WorkspaceManager` API is `SwitchWorkspace`/`CloneWorkspace`/`RemoveWorkspace`). But `CLAUDE.md` §"Profile / Workspace scoping" and `docs/Architecture.md` (§Ownership, §Module Responsibilities, §Providers) still teach **Profile = saved, Workspace = runtime** — the same word now means the *opposite* thing in docs vs code. Architecture.md also cites `Models/Profile.cs` (deleted), `ShellViewModel.SelectProfile` (now `SelectWorkspace`), and `ProviderManager.CreateProviderSet` (replaced by the pooled `AcquireProviderSet`).

This is the most dangerous drift found: the docs themselves say "getting scope wrong is the easiest way to add a bug," and they currently instruct the agent to put runtime state in the persisted, shared object. **Fix:** rewrite both docs in the new vocabulary (Workspace = saved/shared; WorkspaceRuntime = per-launch runtime). Keep the deliberate on-disk compat names (`"workcontexts"`, `Contexts\` folder, `--context` IPC flag) and label them as frozen wire/disk contracts. While there, sweep the ~7 stale `WorkContext` code comments (`App.xaml.cs:160,300`, `ProviderManager.cs:67`, `JumpListService.cs:15,33`, `SingleInstanceService.cs:42,50`, `VoiceConfig.cs:9`). **Effort: M.**

### A2. Dead instructions an agent will follow

- `docs/features.md` checklist + `docs/Architecture.md` SoC #5 direct ribbon defaults to `ShellViewModel.BuildDefaultItems()` — **that method no longer exists**. Defaults live in `src/Nexaflow.Core/Ribbon/default-ribbon.json`, loaded by `RibbonLayoutService.LoadDefaults()`.
- `IQueryHandler.cs`'s own XML doc, `features.md`, and `Architecture.md` describe a two-mode registration model — global `FeatureManager.Instance.RegisterQueryHandler(...)` or implement on a page DataContext. **Neither exists**: there are zero `RegisterQueryHandler` calls, no such method on `FeatureManager`, and no VM implements `IQueryHandler`. All 8 handlers are standalone classes auto-discovered via `FeatureCatalog` and gated by the `pageVm` argument to `CanProcess`. State the one real rule and delete the rest. **Effort: S.**

### A3. The test-coverage table is false

`docs/testing.md` claimed Console, Projects, WindowsRegistry had no tests and `Tests.Providers` was an empty placeholder. All four had real tests (Console 7 files, Projects 4, WindowsRegistry 6, Providers ~10 incl. Aria wire-protocol tests). **Fixed 2026-07-08:** rather than regenerating a table that would rot again, the coverage table was removed and testing.md now redirects to the product tree's `tests` concern (in-app Product tab / `.product/tree.json` / the `PRODUCT.md` concern tally) as the single live record, with "update the node's `tests` concern" as the maintenance step; the Providers section was rewritten to describe actual coverage and the remaining `CompleteAsync`-mapping gap (§F8). The reflection self-check test this section once listed as remaining landed the same day (§H2, `Architecture/CoverageGuardTests`). **Closed.**

### A4. Undocumented subsystems

The layout in CLAUDE.md/Architecture.md omits four shared projects — `Nexaflow.Syntax` (tree-sitter engine, referenced by Code/Notebook/ProductManager/Visuals.Text), `Nexaflow.IO.Terminal` (PTY host), `Nexaflow.Visuals.Terminal` (terminal input logic), and the **elevation subsystem**: `Nexaflow.Elevation.Contracts` (pure DTO leaf, referenced by Features.Common and Core) + `Nexaflow.PrivilegeBridge` (a separate `requireAdministrator` exe, build-order-only reference, spoken to over an authenticated named pipe via `IShellServices.RunElevatedAsync`). `IO.Common`'s description ("EncodingDetector + FileChangeWatcher") is 2 files out of 16 — it now also owns Glob, Hashing, Base64/stream-codec contracts, `IArchiveHandler`, the VirtualFileSystem, TextLineIndex, and transforms. An agent that can't find these **will re-implement them in a feature**.

**Fix:** add all five to the layout tables with one-line roles; describe IO.Common by capability group; add an "Elevation" section to Architecture.md (trust model + "to add an elevated op: DTO in Contracts, `IElevatedOperation` in the bridge, route via `RunElevatedAsync`") and a hard rule to CLAUDE.md: *never `Process.Start("runas")` from a feature — use `IShellServices.RunElevatedAsync`*. **Effort: M.**

### A5. The Separation-of-Concerns list is ~60% resolved

Architecture.md's six SoC findings vs reality: #1 ShellViewModel — three of the four recommended extractions already landed (notifications → `MessageCenter`, tabs → `ShellServices`, ribbon → `RibbonViewModel`/`RibbonLayoutService`); what remains is different (§D1). #2 — the layout constants moved from `WindowManager` (now a 136-line static DPI helper) to `ShellServices.PositionWindow:943`. #4 — `ProjectService.Ops` is gone (one stale doc-comment left in `TransactionalFileService.cs:9`). #5 — `BuildDefaultItems` no longer exists. #6 — RibbonEditor's `ShellViewModel` dependency was removed (defaults via `RibbonLayoutService.LoadDefaults`); the procedural-UI half still stands. Refresh the section so future reviews don't re-do finished work. **Effort: S.**

---

## B. Mechanical enforcement — lock the rules in while they hold (P1)

> **✅ Actioned 2026-07-08.** B1: `Tests.Features/Architecture/ArchitectureRulesTests` (no feature→Core, no feature→feature, dispatcher-singleton source scan) + `Tests.Providers/ArchitectureRulesTests` (no provider→Core / provider→provider). B2: `FeatureTouchPointTests` (every feature dir referenced by Core.csproj **and** Tests.Features.csproj; every `ViewerMap` sample extension mapped in `default-filemap.json` — `ViewerBySet` was extracted to the shared `Fixtures/ViewerMap` so the UI smoke and the guard consume one map). B3: `Tests.Providers` now builds and runs in ci.yml (per-project TFM map). The viewer↔filemap guard landed 2026-07-08 using the marker that already existed — `IFileAction.OpensViewer` (what the Define-New wizard filters on) + the `StaticExperienceId` convention — and on its **first run caught a real regression**: commit `bfed811` moved `ShowJsonAction` to `/text/json` without updating the filemap, silently breaking `.json` default-open on a fresh map (fixed). Still open: B4's colour analyzer. A `Directory.Build.props` now exists (x64 pinning, PR #123) — a natural home for future analyzer wiring.

At review time: **no** `Directory.Build.props`, no `.editorconfig`, no analyzers, no banned-API list, no architecture tests anywhere. Every hard rule was prose. The rules held — which is exactly when enforcement is cheap. For an agent-authored repo this is the highest-leverage structural change in this review: the failure mode isn't malice, it's a plausible wrong turn (`using Nexaflow.Core;`, a `<ProjectReference>` to Core, an `Application.Current.Dispatcher` because that's the WPF idiom the model knows best) — none of which produced a compile error.

### B1. Architecture tests (S)

One test class in `Nexaflow.Tests.Features` (it already references all 29 feature projects — no new deps, no NuGet):

- For each loaded `Nexaflow.Features.*` assembly: `GetReferencedAssemblies()` contains neither `Nexaflow.Core` nor any other `Nexaflow.Features.*` (except `Features.Common`).
- Source scan: no feature `.cs` contains `Application.Current.Dispatcher` / `Dispatcher.CurrentDispatcher`.
- Provider variant in `Tests.Providers`: no provider references Core or a sibling provider.

### B2. Convert the silent new-feature failure modes into red tests (M)

Adding a viewer feature touches 8 places; 5 fail **silently** (build green, feature absent): the `<ProjectReference>` in `Nexaflow.Core.csproj` (DLL never ships → discovery never sees it), the reference in `Nexaflow.Tests.Features.csproj`, the `default-filemap.json` entry, the `SampleFileViewerTests.ViewerBySet` row, and the registration itself. Add completeness tests: every directory under `src/Nexaflow.Features/` has a matching ProjectReference in both csprojs; every discovered `IPageRegistration.StaticPageKind` that is a file viewer has a filemap entry and a `ViewerBySet` row. This turns "remember 8 things" into "the build tells you what you missed."

### B3. CI gap (S)

`.github/workflows/ci.yml` runs only the Core and Features test exes — `Tests.Providers` (which now has real tests, §A3) never runs. Add it.

### B4. Colour-literal enforcement — defer (M)

A Roslyn analyzer for `#RRGGBB`/`Color.FromRgb`/`Brushes.X` literals is the right eventual answer but needs allow-listing for the sanctioned exceptions (fallback-after-resource-read, scrims, `Transparent`, theme-definition XAML). Not worth blocking on; the §E4 cleanup shrinks the noise first.

---

## C. Contract layer (`Features.Common`)

### C1. Execute the 2026-05 blessed moves — still open (S)

Unchanged since the last review, re-verified: `ClientTools/ClientBlockParser.cs` + `ClientTools/ParsedAssistantTurn.cs` are consumed only by `Core/Services/AIService.cs:349` (+ their tests), and `OpenPageRequest.cs` only by `Core/Controls/PaneView.xaml.cs:143` → `ShellViewModel.OpenPage:849`. Move the first two to `Core/Services/Agent/`, `OpenPageRequest` to Core; `Nexaflow.Tests.Core` already references Core so the tests move cleanly. (`ParsedAssistantTurn` referencing `ClientPlan`, which stays, is fine — Core→Common is legal.)

**Not-a-leak verdicts (recorded so the next review doesn't re-flag):** `ContextImage` (named in `ToolResult`, produced by Web/Video/Model3D), `ContextSecurityRisk` (named in `IPageViewModel`+`Page`, consumed by ≥5 features), `ConversationRecord`+`ContextRef` (AIChat ↔ Core store), `AiResponse` (`RunAgentAsync` return). `FileBreadcrumbs` is a concrete helper but operates purely on Common's own `Page`/`BreadcrumbSegment` types — sanctioned exception; note it as such in Architecture.md.

### C2. `ClientToolBase` — the boilerplate ~30 tool classes keep re-typing (M)

There are ~65 client tools across ~17 sites. The ~36 class-based ones each re-declare `Name`/`Description`/`Parameters`/`Safety`/`Parallelizable` around one method — and SystemInfo already invented a private `ControlToolBase` (`ServiceTools.cs:110`) to cope, which is the codebase telling you the seam exists. Add an optional `abstract class ClientToolBase : IClientTool` in `Common/ClientTools` (defaults: `Safety => ReadOnly`, `Parallelizable => false`; `protected abstract Task<ToolResult> RunAsync(...)`), migrate opportunistically. This is also the main **AI Ready** lever (§H1): the cheaper a tool is to declare, the faster the 317 open concern links close.

### C3. Two hand-rolled tool aggregators (M)

`AIChat/ConversationViewModel.GetClientTools():719` (merge across pinned pages + `MultiContextClientTool` disambiguation by security context) and `WindowsFileSystem/FileSystemViewModel.GetClientTools():1202` (merge own + viewlet `IViewletAiSurface` tools) independently implement "collect from N sub-surfaces, de-dupe by name+context." The security-context routing is subtle enough that duplicating it is a wrong-scope-action risk. Extract a `ClientToolMerger` into `Common/ClientTools`.

### C4. Tool-surface polish (S)

- **Naming:** all ~65 tools are snake_case, but WindowsFileSystem ships bare `copy`/`move`/`rename`/`delete` (`FileWriteTools.cs:84–215`) while everything else namespaces (`git_status`, `registry_set_value`). Bare generic verbs are the top collision risk when AIChat aggregates tools across pinned pages → rename to `copy_file` etc.
- **Arg reader:** one residual hand-roll — `ProcessTools.cs:231` keeps a private nullable `Int` because `ToolArgs.Int` demands a fallback. Add `ToolArgs.IntOrNull` and delete it.
- **Threading contract:** `IClientTool.InvokeAsync` docs promise the harness keeps the UI context; only WindowsRegistry wraps mutations in `RunOnUiAsync` (`RegistryTools.cs:62,135`). Confirm the guarantee, then either drop Registry's wrappers or fix the doc — one of the two is wrong today.

### C5. `IShellServices` is a 33-member god-interface (M)

Nine unrelated concerns; every feature mock carries all of them. Don't shatter it — peel the two cohesive facets features rarely need together: `IShellDialogs` (prompt/confirm/pick ×3) and `IFolderBusyTracker` (4 members), with `IShellServices` composing both so no caller breaks. `IAIService` (15 members) has a clean `IConversationStore` cluster (8 members) if it grows again — note, don't act yet.

### C6. Viewlet quiesce protocol — fix the half-wireable seam (S)

The `IViewletQuiescible` protocol (commit `bd79a97`) is a well-factored opt-in, but `ViewletHost.QuiesceFolderAsync` invokes a **public settable** `QuiesceFolderHandler` delegate that `FileSystemView` wires from outside — left null it silently no-ops a *safety* contract. Pass the fan-out as a constructor argument instead.

### C7. Small contract notes (S, optional)

`Page.SecurityRisk` is a denormalized copy of `IPageViewModel.GetContextSecurityRisk()` kept fresh "by whoever pins the page" — derive the badge from the live VM or document the copy point. `Page.GetOrCreateContent()` silently returns an empty `UserControl` when `ContentFactory` is null — a mis-registered page renders as a blank tab; consider logging/throwing in DEBUG. `BreadcrumbSegment` carries three mutually-exclusive click behaviours with nothing encoding the exclusivity — comment it.

### C8. One discovery table (S)

A feature advertises capability through three surfaces (FeatureManager/`FeatureCatalog` typed getters; `FileSystemFeatureRegistry` for file/folder actions + viewlets; raw `DiscoverImplementations<T>` for `IGenericObjectHandler`/`IDropTarget`) — all backed by the same catalog, but the contract side doesn't say which registry finds what, or what gets constructor-injected. Add one table to Architecture.md mapping contract → discovery surface → instantiation rule. Do **not** merge the registries; the `ICacheable`/`Rehydrate` model is legitimately different.

---

## D. Core shell

### D1. ShellViewModel — extract the two remaining clusters (M)

**1,503 lines as of 2026-09-05** (1,545 at review — the `OverlayCoordinator` extraction was offset by new
work), ~17 concerns, but thinner than it looks: tabs/ribbon/notifications are already thin delegations to `ShellServices`/`RibbonViewModel`/`MessageCenter` (§A5). The two genuinely extractable clusters:
- **`AiInputRouter`** (~345 lines, L1181–1524): handler scoring, ghost-completion, chat drop handling, voice, prefill animation, send/cancel. Self-contained, and the most-touched concern in the file.
- **`OverlayCoordinator`** (~230 lines, L280–510): the unified overlay host + confirmation + input-prompt trio.
Pane/split-layout (L869–945) is a third candidate if the file grows again.

### D2. ShellServices — the second god object (M)

**1,120 lines as of 2026-09-05** — it was 958 at review, so this is the one finding here that has moved
backwards while being tracked. ~13 banner concerns (window registry, tab registry + open/move/tear-off, session capture/restore, file watching, overlays, themed pickers, folder-busy tracking, window positioning). Nothing here is wrong — it's just Core's biggest context-load after ShellViewModel. Split the three leaf concerns with no coupling to the tab registry: file watching (`WatchFile` plumbing), folder-busy tracking, and the themed file/folder pickers. Pairs naturally with the C5 interface peel.

### D3. RibbonEditor (L)

749 lines of procedural `Border`/`StackPanel` construction (`RebuildCards:146–395`, `BuildIconGrid:500`, `BuildColorSwatches:547`). The `ShellViewModel` dependency is already gone; what remains is the ViewModel + `ItemsControl`/`DataTemplate` conversion. Real but expensive; schedule when the ribbon next needs feature work.

### D4. Window-position constants (S)

`ShellServices.PositionWindow:943` hardcodes `TopBarHeight=72`/`TabBarHeight=38`/… duplicating `MainWindow.xaml` (and `MainWindow.xaml.cs:182` repeats the 72 independently). Read from the chrome at runtime or share one static. (Architecture.md pins this on `WindowManager` — stale, §A5.)

---

## E. Features — duplication and rule gaps

### E1. Modal-overlay primitive — the most-copied scaffold in the repo (M) ⭐

The scrim + centered Surface/Accent card pattern appears **~15 times across 6 features** (FileSystemView ×7, ProductView ×5, Compressed ×2, Registry ×2, Json, ProjectDetail), with matching duplicated VM-side confirmation/prompt state (`FileSystemViewModel` has three overlay regions; `ProductViewModel` has *two separate* "Confirmation overlay" regions at :107 and :811). Ship in `Visuals.Common`: (a) a `DialogCard`/overlay style, (b) a small `ConfirmationRequest`-style VM helper. Directly shrinks the two biggest feature VMs (§E6). Note `IShellServices.ShowOverlay`/`ShowConfirmation` already exist for *shell-modal* cases — part of this work is deciding which of the 15 should simply route there.

### E2. Converter duplication past rule-of-three (S–M)

- `SortGlyphConverter` + `SortBrushConverter`: **3 byte-identical copies** (Processes, WindowsApps, WindowsFileSystem, ~140 lines total; comments literally say "mirrors the WindowsApps helper"). Hoist to `Visuals.Common/Converters`. The sortable-GridView-header markup around them is also near-identical — a shared header style/attached behaviour is the follow-on.
- `DepthToMarginConverter`: 2 copies (Json, Processes) — hoist pre-emptively.
- `Scratchpad/Converters/BoolToVisibilityConverter` duplicates the `Visuals.Common` one — delete.

### E3. Formatter gaps (S)

`Compressed/ArchiveNode.cs:57` and `Video/VideoViewModel.cs:646` hand-roll the bytes→units loop (`SizeFormatter.FormatBytes` exists). Audio and Video both implement m:ss/h:mm:ss — add `DurationFormatter.FormatMediaTime(TimeSpan)` and point both at it.

### E4. Colour-literal clusters (S–M)

> **Re-counted 2026-09-05, and this finding is nearly closed — but the remaining cluster is the one the
> review did not name.** Four of the five below are done. What is left, after discounting the two
> sanctioned categories (a token *definition*, and a scrim/alpha wash), is:
>
> | Where | What | Verdict |
> |---|---|---|
> | `Core/MainWindow.xaml` | `#E81123` close-button red, `#06b6d4` ×2, `#E11D48`, `#FFD8D8D8` caret | **the biggest cluster left, and it is Core's own chrome** — the shell hard-codes an accent while every feature is forbidden to |
> | `Features.Text/TextView.xaml:409,414` | `#CC2A2A00` + `#FFFF00` warning banner | real — wants `WarningBrush` |
> | `Features.Projects/ProjectDetailView.xaml:151` | `#1A1A1A` foreground | real — wants `TextBrush` |
> | `Visuals.Text/…/SvgGraphRenderer.cs:18–25` | a whole hard-coded dark palette | defensible (it writes **SVG output**, not WPF) but it means an exported diagram ignores the theme. Decide and record |
> | `Core/Models/WorkspaceStyle.cs` | workspace colour presets | **sanctioned** — user-pickable data, not chrome |
> | `Scratchpad` | the `PostIt.*` values | **sanctioned** — this is the `IThemeContribution` token definition CLAUDE.md names as the pattern |
>
> The measuring trap, recorded because this section fell into it: a raw grep for `#RRGGBB` counts theme
> definitions, and the theme layer is *where colours are supposed to be*, so the naive count says
> Scratchpad and `Nexaflow.Core` are the worst offenders when both are clean. Exclude `Themes/`,
> `Tokens.xaml`, `Colors.*.xaml` and the tests before reading anything into a number.

The original five, for the record: 1. ~~Scratchpad chrome + picker swatches~~ — **done** (what remains is
the sanctioned token definition). 2. ~~Logs level pips~~ — **done**. 3. ~~Images accent-blue
`DropShadowEffect`~~ — **done**. 4. ~~WindowsFileSystem glyph fills + success toast~~ — **done**.
5. **Git brand orange** (`#F05032`) — no longer present as a literal either.

### E5. WindowsFileSystem's five process-global mutable registries (M)

`DefaultActionRegistry.Instance`, `ExternalAppRegistry.Instance`, `FileMapManager.Instance`, `ShellNewRegistry.Instance`, `TemplatedCreateRegistry.Instance` — mutable config, service-located from views/actions, shared across all workspaces with no eviction on reconfigure. This collides with the per-Workspace scoping model and "Trust the DI." Either inject them per workspace via `FeatureManager`, or explicitly sanction them in Architecture.md as intentional process-wide caches (they do model genuinely machine-global state — file-type maps, external apps). Decide once; today it's implicit.

### E6. ViewModel outliers with clean split lines (M–L)

Line counts re-measured 2026-09-05; all four have grown since the review.

- `FileSystemViewModel` (**2,019 lines**, ~16 regions): three overlay concerns (→E1), Define-New wizard, tree management, right-panel management, ribbon pinning, background folder load. The one true outlier — extract overlays first (cheap, mechanical), then tree/right-panel collaborators. **L** in total, incremental.
- `JsonViewModel` (**1,499**): the root-array **table mode** is a feature-within-a-feature, separable from the streaming/windowing reader. **M.**
- `TextViewModel` (**1,344**): already partially decomposed; low priority.
- `ProductViewModel` (**964**): consolidate the two duplicate confirmation regions; lift Snaplinks/Concerns/Restructure overlays. **M.**

### E7. Navigational-breadcrumb helper (M, optional)

Static file breadcrumbs are nicely shared (`FileBreadcrumbs`, 11 consumers), but the *dynamic* clear-and-rebuild-on-navigate loop is hand-rolled ~5× (ProductManager and WindowsRegistry are near-identical private `ApplyBreadcrumbs`; Tabular/Json/AIChat/WindowsSearch each roll an item type). A `SetNavBreadcrumbs(page, segments, onClick)` sibling would collapse them.

### Also noted, no action

`DispatcherTimer` inside several VMs quietly couples them to the UI thread (idiomatic WPF; flag only if VMs must become thread-agnostic). `Projects/TransactionalFileService.cs:240` owns the one raw `FileSystemWatcher` in a feature — directory-scoped with different semantics than `WatchFile`; defensible. `IsBusy`/`IsLoading`/`IsLoadingHead` naming is inconsistent — standardize the name only, don't abstract.

---

## F. Providers

### F1. Cancellation bugs (S) — ✅ fixed 2026-07-08

All four HTTP providers `catch (Exception)` → `throw new LlmProviderException(...)`, which **rewrapped `OperationCanceledException`** — so `AIService.RunAgentAsync`'s cancel path never saw the cancel and a user-cancelled request surfaced as an error toast + failed activity. Additionally **Gemini never passed `ct` at all** (completion stream + model list) — a runaway Gemini stream was uncancellable. **Fixed:** each provider's `SendAsync` now has `catch (OperationCanceledException) { activity.Fail("canceled"); throw; }` before the general catch, and Gemini threads `ct` into `GenerateContentStreamAsync`/`ListAsync` (+ `.WithCancellation(ct)`). When F3's shared helper lands, fold this handling into it so it can't regress per-provider.

### F2. Pool lifecycle (S)

`ProviderManager.ReleaseProviderSet:232` disposes via `as IDisposable` only — `AriaLlmProvider` is **`IAsyncDisposable`-only**, so its pipe client/semaphore leak on every release and accumulate across reconfigure cycles. Handle `IAsyncDisposable` (dispose outside the lock, alongside cooldown). Also: `LoadAssembly:243–260` mutates `_descriptors`/`_loadedAssemblies` without the `_poolLock` that readers hold — guard it.

### F3. One shared stream-execution helper + a resilience policy (S–M)

The four providers repeat an identical ~20-line skeleton (activity start → new SDK client → stream-accumulate → complete/fail → wrap exception) — ~90 duplicated lines where F1-class bugs must be fixed 4×. Extract `StreamToResponse(label, activityMgr, Func<CancellationToken, IAsyncEnumerable<string>>, ct)` into `Providers.Common` owning activity lifecycle, exception mapping (incl. cancellation), and accumulation. **Do not** build a shared HTTP/SSE base — vendor SDKs own the wire; there's nothing to unify below the delta stream. Fold a minimal resilience policy into the same helper (per-request timeout + capped exponential backoff on 429/5xx): today resilience is whatever each SDK defaults to, inconsistently, and a hung stream hangs until the caller's token fires. Related cleanups: construct the SDK client once per pooled instance instead of per call (contradicts the pool's warm/cool rationale — `ClaudeLlmProvider.cs:64`, `GeminiLlmProvider.cs:95`, `OpenAILlmProvider.cs:84`, `OllamaLlmProvider.cs:61+`), and surface `GetAvailableModels` failure reasons out-of-band instead of empty-catch → `[]` (bad API key and "no models" are currently indistinguishable in the options UI).

### F4. Contract evolution — deliberate, not urgent (M)

`ILlmProvider.CompleteAsync` is structured-in/**string-out**: every provider streams internally but accumulates into one flat `RawText`; no `IAsyncEnumerable` surface, no usage/finish-reason metadata — live token rendering, cost telemetry, and truncation handling are all blocked on the contract. And tool use is **100% prompt-convention** (fenced `client_tool` blocks scraped from text; no provider sends native `tools=[]`) — reliability rides on instruction-following, and strong native-tool models get no benefit. Both are defensible lowest-common-denominator choices; document them as such in Architecture.md, then when appetite exists add an optional `CompleteStreamingAsync` overload + usage fields, and a native-tools escape hatch per provider.

### F5. Capability metadata is class-level, should be model-level (M)

`SupportsImages` is a class constant (a text-only OpenAI model claims vision; an Ollama `llava` denies it) consumed by the agent loop's image routing; context window is hardcoded 200k for all Claude models, `null` for Gemini/OpenAI, dynamic only for Ollama — so `GetConversationContextWindowAsync` budgeting is accurate for exactly one provider; the Claude model list is a static 6-entry array that drifts; Claude `MaxTokens` is a hardcoded 8096. One fix shape: a per-model capability record resolved via the injected `ProviderModel` (SDK/models-endpoint where available, table fallback).

### F6. Secrets at rest (M)

Every provider API key is written as plaintext indented JSON under `%AppData%\Smile\nexaflow\` (`ConfigManager` `File.WriteAllText`; key props on Claude/OpenAI/Gemini/Aria configs). Encrypt secret properties with DPAPI (`ProtectedData`, CurrentUser) on save/load — a `[Secret]` attribute the config serializer honours is the clean seam and keeps migration lenient.

### F7. Aria oddities (S–M)

The `[Required]` `AriaConfig.ApiKey` is collected, stored (plaintext), and **never sent** — the provider ctor doesn't take config (misleading security posture; wire it into the handshake or delete it). `ConnectAsync` resolves the user's **email via AD** and stamps it on every frame (PII + heavy `DirectoryServices` dependency — use a non-PII id or make it opt-in). `SendAsync` treats the *first* inbound assistant message as the reply — no correlation id, so unsolicited messages or concurrent sends mis-route, and a slow reply is misreported as "Lost connection" (add a correlation id; distinguish timeout from disconnect).

### F8. Test seam for the riskiest mapping (M)

The neutral `LlmMessage` → SDK-type mapping (roles, attachments, image blocks) is welded to the live SDK call in all four providers, so the most regression-prone provider code has no unit coverage. Expose the request-builder as `internal static` (+ `InternalsVisibleTo`) and test the mapping without network.

---

## G. Performance / resource use

### G1. Image album: eager decode of the whole folder, non-virtualized (M) ⭐

`ImageViewModel` ctor materialises an item per file, `ThumbnailLoadTask` decodes a `BitmapSource` for **every** image and marshals each to the UI thread individually; album/explore are `ItemsControl`+`WrapPanel`, collage an `ItemsControl`+Canvas — nothing virtualized, no folder-size cap. A 2,000-image folder ≈ ~200 MB of decoded thumbnails + thousands of realized containers. Virtualize the grids, let realized cells drive thumbnail fetch, batch the UI marshalling. The single largest memory drain found.

### G2. `RibbonViewModel` leaks per closed window (S) ⭐

It subscribes to the shared `Workspace.RibbonChanged` (`RibbonViewModel.cs:81`) and only unsubscribes on workspace *swap* — `ShellViewModel.Detach()` never detaches it. Since a Workspace outlives its windows (tear-offs share one), every open→close window cycle roots a dead `RibbonViewModel` that still reloads items from disk on every ribbon edit. Add `Detach()` and call it from `ShellViewModel.Detach()`.

### G3. Timers are deactivation-aware but minimize-blind (S)

Deactivation is wired to `Unloaded`, which doesn't fire on window minimize — a minimized window keeps the Processes 1 s snapshot+reconcile and the Audio 33 ms spectrum repaint running invisibly. Treat minimize as deactivation for polling/render timers (keep audio *playback*).

### G4. VFS temp files never evicted mid-session (M)

`VirtualFileSystem._materialized` grows unbounded; extracted archive-entry temp files under `%TEMP%\nexaflow-vfs` are deleted only on container invalidation or process exit. Add an LRU cap (count or total bytes).

### G5–G8. Remaining items (S–M each)

- **Chat timeline**: `ConversationView.xaml:396`'s `MessageList` is a bare `ItemsControl` in a `ScrollViewer` — long conversations realize every message with heavy nested templates. Virtualize (accepting the auto-scroll/variable-height work).
- **Conversations**: `ConversationStore.Load` eagerly deserializes every conversation *with full message bodies* into `AIService._conversations`. Load an id/title/timestamp index; lazy-load bodies on open.
- **Startup**: `App.xaml.cs:201–209` synchronously reads every workspace's AiConfig pre-paint just to learn the union of provider assembly names — cache the union (like the discovery catalog), refresh post-paint.
- **Hex paint churn**: `HexRenderPanel.OnRender` allocates ~800 `FormattedText` + strings per repaint (`MakeText`, `ToString("X2")` per byte). Interaction-only, not continuous — precompute the 256 hex strings / cache glyph runs when convenient.
- **WMI**: `SystemInfoCollector` queries `Win32_OperatingSystem` twice per collect — cache within one `Collect()`.
- **Async void ×3**: `FileSystemViewModel.GoToThisPc:987`, `ResortEntries:1647`, `DotnetViewletViewModel.QueueNugetCheck:222` — fire-and-forget without local handling; convert to `async Task` or wrap.
- **Scratchpad**: VM disposed on `Unloaded` (not `Page.Closed`) → full notes reload on every tab re-entry. Minor; move disposal to `Closed` if reload cost grows.

---

## H. Product functionality & AI-Ready alignment

The live product tree (843 nodes, 674 leaves, 652 done, 0 faulted) says the product is feature-complete on its current scope; the open work is concern debt and a handful of leaves (Font viewer in flight; markdown extras — LaTeX, nomnoml, 5 Mermaid types, 4 Markdig extensions; images EXIF panel; Projects AI tools; Text edit-file; SQL/GraphQL grammars). Nothing architectural blocks any of these.

### H1. "AI Ready" is the biggest product-side debt — give it an architectural lever (M)

317 open `AI Ready` concern links, concentrated in Tabular (36), Scratchpad (29), Code (27), Options (27), Images (26), Markdown (21). Closing them is repetitive tool/context work — exactly what should be made cheap:
1. **C2's `ClientToolBase`** cuts per-tool ceremony ~10 lines.
2. Write a **"make a page AI-ready" recipe** in features.md: `GetContext` (string) → `GetContextObject` (typed) → `GetClientTools` (read-only first, then approval-gated mutators) → `GetAiSystemPromptGuidance` → security context — with one canonical example per step. Today the pattern must be reverse-engineered from 17 differently-shaped implementations.
3. Then batch-close by area, worst-first.

### H2. Tests concern — make coverage self-truthing (✅ both halves now done)

> **Superseded 2026-09-05.** The deferred half of this section — splitting the monolithic
> `Tests.Features` — **has happened**, and by subject rather than by feature: `.Viewers`, `.WindowsOS`,
> `.Architecture`, `.Common`, plus `Tests.Components` and `Tests.Initiatives`. So the `[TestCategory]`
> mitigation proposed below is moot, and the fast inner loop it was working around is real. Left in place
> because the reasoning about *why* the monolith hurt is still the reason not to rebuild one.


Beyond fixing the false table (§A3): a reflection test asserting every `Nexaflow.Features.*` assembly has ≥1 test class in `Tests.Features` (**✅ done 2026-07-08** — `Architecture/CoverageGuardTests`, with an alias map for shared folders), so the gap list can't rot again. The monolithic `Tests.Features` exe (references all 29 features) makes the per-feature edit-test loop pay a full feature-set rebuild — a per-feature `[TestCategory("<Feature>")]` convention is the cheap mitigation (**S**); splitting the project is the clean fix but **L**, defer.

### H3. Agent affordances (S–M)

- **`add-feature` skill** (`.claude/skills/`): encode the 8 touch-points from §B2, the correct `default-ribbon.json` location, and the verification commands. Highest leverage — it makes the riskiest workflow guided, and pairs with B2's tests as belt-and-braces.
- **Fast-loop docs**: one CLAUDE.md paragraph — build the feature csproj (not the solution), run the MSTest exe with `--filter FullyQualifiedName~<Class>` / `TestCategory!=UI`.
- **Per-feature README stubs** (5 lines: params, key files, canonical sibling): cheaper per-session context than re-reading the 276-line features.md for every touch. 31 files, do opportunistically.

### H4. Contract XML-doc thin spot (S)

`IFolderViewlet` members (`FolderNameGlob`, `ContainsFileGlobs`, `SupportedModes`, …) carry no per-member `<summary>` — the glob/percentage semantics live only in Architecture.md prose. Since interface XML docs are the agent's primary source (and elsewhere excellent), bring this one up to the bar; audit `Viewlets/` + `Ribbon/` for the same.

---

## Suggested sequencing

Steps 1–3, 4, 6 and most of 5 and 7 are done; what follows is the **remaining** order, re-derived
2026-09-05. Cheapest, highest-confidence first — same discipline as the last review.

1. **The safety row first — C6.** It is an **S**, and it is the only open finding where the failure is
   silent and the contract is a *safety* one: a null `QuiesceFolderHandler` no-ops the quiesce and nothing
   says so. Pass the fan-out as a constructor argument. Everything else here is cost, not risk.
2. **Cheap truth fixes** (C8, H4, D4): the discovery table, the `IFolderViewlet` summaries, and folding
   the window constants onto the `TopBarHeight` theme resource that already exists. *(S each)*
3. **Finish E4, then B4.** Only three sites remain, and the largest is Core's own `MainWindow.xaml` — so
   this is now an **S**, and it is the last thing standing between the repo and a colour analyzer with a
   short allow-list. Doing them in the other order is what makes B4 expensive. *(S, then M)*
4. **AI-Ready campaign** (H1): write the recipe, then batch-close concern links worst-area-first. This is
   the largest product-visible win left and it is now the only P2 with nothing blocking it. *(M, ongoing)*
5. **Shell splits** (D1, D2) when next touching those files — extract-on-touch rather than big-bang. D2 is
   the one row that has moved *backwards* (958 → 1,120), so it wants a decision rather than more drift. *(M)*
6. **Contract shape** (C3, C5, C7): the tool merger, the two interface peels, the small notes. *(S–M)*
7. **Perf tail** (G1 image virtualization, G5 chat virtualization, G6–G8) opportunistically. *(S–M)*
8. **Provider tail** (F7 Aria, F8 test seam). *(S–M)*
9. **E5, E6, E7, D3** — the expensive structural ones, on touch. *(M–L)*

Deliberately deferred: the colour analyzer (B4 — do E4 first), `IShellServices`/`IAIService` splits beyond the two facet peels (C5), RibbonEditor rewrite (D3), per-feature test project split (H2), and any umbrella file-reader abstraction (the four windowing strategies remain correctly feature-specific — unchanged verdict from the last review).
