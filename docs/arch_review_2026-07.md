# Architecture Review — 2026-07

_Review date: 2026-07-08. Scope: full re-examination — product tree (843 nodes), all docs, and the implemented code across the 52-project solution. This file supersedes [arch_improvements.md](arch_improvements.md) (2026-05-31, updated 2026-06-20); the still-open items from that review are carried forward here (§C1)._

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

## TL;DR — verdicts at a glance

Priority bands: **P1** = do first (bugs, actively-misleading docs, lock-in-now guardrails); **P2** = high-value structure; **P3** = worthwhile polish.

| # | Finding | Verdict | Lens | Effort | Band |
|---|---------|---------|------|--------|------|
| A1 | Docs teach the **inverted** Workspace vocabulary post-rename | ✅ Done 2026-07-08 — both docs rewritten; stale code comments swept | AI-maint | M | P1 |
| A2 | Dead instructions: `BuildDefaultItems()`, `RegisterQueryHandler` | ✅ Done 2026-07-08 — incl. `IQueryHandler.cs` XML doc | AI-maint | S | P1 |
| A3 | testing.md coverage table is false (4 areas claimed untested now have tests) | ✅ Done 2026-07-08 — replaced with product-tree redirect | AI-maint | S | P1 |
| A4 | 5 projects undocumented (Elevation ×2, IO.Terminal, Syntax, Visuals.Terminal); IO.Common understated | ✅ Done 2026-07-08 — layout re-tiered, Elevation section + hard rule added | AI-maint | M | P1 |
| A5 | Architecture.md SoC list ~60% stale | ✅ Done 2026-07-08 — replaced with a pointer to this doc | AI-maint | S | P1 |
| B1 | Hard rules have **zero mechanical enforcement** (they hold today — lock in) | ✅ Done 2026-07-08 — architecture tests | AI-maint | S | P1 |
| B2 | New-feature checklist has 5 **silent** failure modes | ✅ Done 2026-07-08 — completeness tests (3 of 5) | AI-maint | M | P1 |
| B3 | `Tests.Providers` never runs in CI | ✅ Done 2026-07-08 | AI-maint | S | P1 |
| F1 | **Bug:** cancellation rewrapped as error ×4 providers; Gemini ignores `ct` | ✅ Fixed 2026-07-08 | Product | S | P1 |
| F2 | **Leak:** `IAsyncDisposable`-only provider (Aria) never disposed by pool | ✅ Fixed 2026-07-08 | Perf | S | P1 |
| G2 | **Leak:** `RibbonViewModel` leaks per closed window | ✅ Fixed 2026-07-08 | Perf | S | P1 |
| C1 | 2026-05 blessed moves still not executed (`ClientBlockParser`/`ParsedAssistantTurn`/`OpenPageRequest` → Core) | Do it | Maint | S | P2 |
| C2 | ~30 tool classes duplicate 5-member scaffolding; SystemInfo invented a local base | `ClientToolBase` in Common | AI-Ready | M | P2 |
| E1 | Modal overlay scaffold copy-pasted ~15× across 6 features + VM confirmation state | Shared overlay primitive | Maint | M | P2 |
| G1 | Image album eagerly decodes every thumbnail, non-virtualized | Virtualize + demand-load | Perf | M | P2 |
| F3 | ~90 lines identical stream/catch/activity wrapper ×4 providers; no retry/timeout/429 anywhere | Shared helper + resilience | Product | M | P2 |
| F5 | Vision/context-window/model-list hardcoded per **class**, not per model | Per-model capability table | Product | M | P2 |
| F6 | API keys stored plaintext JSON in %AppData% | DPAPI via `[Secret]` seam | Product | M | P2 |
| D1 | `ShellViewModel` (1,545 lines): AI-input cluster + overlay hosts extractable | `AiInputRouter` + `OverlayCoordinator` | AI-maint | M | P2 |
| D2 | `ShellServices` (958 lines, ~13 concerns) — undocumented second god object | Split 3 leaf helpers | AI-maint | M | P2 |
| H1 | "AI Ready" concern: 317 open links; no recipe for making a page AI-ready | Recipe doc + C2 leverage | AI-Ready | M | P2 |
| H3 | No add-feature skill / fast-loop docs / per-feature README stubs | Add affordances | AI-maint | S–M | P2 |
| E2/E3 | 3× sort converters, 2× depth converter, dup formatters | Hoist to Visuals.Common | Maint | S | P3 |
| E4 | 5 colour-literal clusters (Scratchpad chrome biggest) | Tokenize | Maint | S–M | P3 |
| E5 | 5 process-global mutable registries in WindowsFileSystem | Scope or sanction | Maint | M | P3 |
| E6 | `FileSystemViewModel` 75 KB; Json/Product VMs have clean split lines | Split | AI-maint | M–L | P3 |
| C3–C6 | Tool merger dup, arg-reader gap, tool naming, `IShellServices` peel, quiesce wiring | See §C | Maint | S–M | P3 |
| F4 | Provider contract: no streaming surface, no native tool-calling, no usage metadata | Evolve contract | Product | M | P3 |
| G3–G8 | Minimize-blind timers, VFS temp growth, chat-list virtualization, eager conversations, startup reads, hex paint churn | See §G | Perf | S–M | P3 |
| D3 | RibbonEditor: 749-line procedural code-behind | ViewModel + XAML | Maint | L | P3 |

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

`docs/testing.md` claimed Console, Projects, WindowsRegistry had no tests and `Tests.Providers` was an empty placeholder. All four had real tests (Console 7 files, Projects 4, WindowsRegistry 6, Providers ~10 incl. Aria wire-protocol tests). **Fixed 2026-07-08:** rather than regenerating a table that would rot again, the coverage table was removed and testing.md now redirects to the product tree's `tests` concern (in-app Product tab / `.product/tree.json` / the `PRODUCT.md` concern tally) as the single live record, with "update the node's `tests` concern" as the maintenance step; the Providers section was rewritten to describe actual coverage and the remaining `CompleteAsync`-mapping gap (§F8). Still open: the reflection self-check test asserting every feature assembly has ≥1 test class (§H2). **Effort: S (remaining).**

### A4. Undocumented subsystems

The layout in CLAUDE.md/Architecture.md omits four shared projects — `Nexaflow.Syntax` (tree-sitter engine, referenced by Code/Notebook/ProductManager/Visuals.Text), `Nexaflow.IO.Terminal` (PTY host), `Nexaflow.Visuals.Terminal` (terminal input logic), and the **elevation subsystem**: `Nexaflow.Elevation.Contracts` (pure DTO leaf, referenced by Features.Common and Core) + `Nexaflow.PrivilegeBridge` (a separate `requireAdministrator` exe, build-order-only reference, spoken to over an authenticated named pipe via `IShellServices.RunElevatedAsync`). `IO.Common`'s description ("EncodingDetector + FileChangeWatcher") is 2 files out of 16 — it now also owns Glob, Hashing, Base64/stream-codec contracts, `IArchiveHandler`, the VirtualFileSystem, TextLineIndex, and transforms. An agent that can't find these **will re-implement them in a feature**.

**Fix:** add all five to the layout tables with one-line roles; describe IO.Common by capability group; add an "Elevation" section to Architecture.md (trust model + "to add an elevated op: DTO in Contracts, `IElevatedOperation` in the bridge, route via `RunElevatedAsync`") and a hard rule to CLAUDE.md: *never `Process.Start("runas")` from a feature — use `IShellServices.RunElevatedAsync`*. **Effort: M.**

### A5. The Separation-of-Concerns list is ~60% resolved

Architecture.md's six SoC findings vs reality: #1 ShellViewModel — three of the four recommended extractions already landed (notifications → `MessageCenter`, tabs → `ShellServices`, ribbon → `RibbonViewModel`/`RibbonLayoutService`); what remains is different (§D1). #2 — the layout constants moved from `WindowManager` (now a 136-line static DPI helper) to `ShellServices.PositionWindow:943`. #4 — `ProjectService.Ops` is gone (one stale doc-comment left in `TransactionalFileService.cs:9`). #5 — `BuildDefaultItems` no longer exists. #6 — RibbonEditor's `ShellViewModel` dependency was removed (defaults via `RibbonLayoutService.LoadDefaults`); the procedural-UI half still stands. Refresh the section so future reviews don't re-do finished work. **Effort: S.**

---

## B. Mechanical enforcement — lock the rules in while they hold (P1)

> **✅ Actioned 2026-07-08.** B1: `Tests.Features/Architecture/ArchitectureRulesTests` (no feature→Core, no feature→feature, dispatcher-singleton source scan) + `Tests.Providers/ArchitectureRulesTests` (no provider→Core / provider→provider). B2: `FeatureTouchPointTests` (every feature dir referenced by Core.csproj **and** Tests.Features.csproj; every `ViewerMap` sample extension mapped in `default-filemap.json` — `ViewerBySet` was extracted to the shared `Fixtures/ViewerMap` so the UI smoke and the guard consume one map). B3: `Tests.Providers` now builds and runs in ci.yml (per-project TFM map). Still open from B2's list: a test tying every viewer `StaticPageKind` to a filemap entry (needs a "this page kind is a file viewer" marker that doesn't exist yet) and B4's colour analyzer. A `Directory.Build.props` now exists (x64 pinning, PR #123) — a natural home for future analyzer wiring.

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

1,545 lines, ~17 concerns, but thinner than it looks: tabs/ribbon/notifications are already thin delegations to `ShellServices`/`RibbonViewModel`/`MessageCenter` (§A5). The two genuinely extractable clusters:
- **`AiInputRouter`** (~345 lines, L1181–1524): handler scoring, ghost-completion, chat drop handling, voice, prefill animation, send/cancel. Self-contained, and the most-touched concern in the file.
- **`OverlayCoordinator`** (~230 lines, L280–510): the unified overlay host + confirmation + input-prompt trio.
Pane/split-layout (L869–945) is a third candidate if the file grows again.

### D2. ShellServices — the second god object (M)

958 lines, ~13 banner concerns (window registry, tab registry + open/move/tear-off, session capture/restore, file watching, overlays, themed pickers, folder-busy tracking, window positioning). Nothing here is wrong — it's just Core's biggest context-load after ShellViewModel. Split the three leaf concerns with no coupling to the tab registry: file watching (`WatchFile` plumbing), folder-busy tracking, and the themed file/folder pickers. Pairs naturally with the C5 interface peel.

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

1. **Scratchpad chrome + picker swatches** (biggest): panel/border/fg literals across `ScratchpadView.xaml`, and picker ellipses that *duplicate the `PostIt.*` token values* — bind chrome to Surface/Text/Border tokens and swatches to the `PostIt.*` tokens.
2. **Logs level pips** (`LogView.xaml:69–109,393`) re-type hues that `LogsTheme.xaml` already defines as `Log.*` — reference the tokens.
3. **Images** accent-blue `DropShadowEffect Color="#4F8EF7"` ×4 — not a black shadow, so not exempt; use `AccentColor`.
4. **WindowsFileSystem** drive/folder glyph fills + success-toast green — promote to tokens or `Swatch.*`/`SuccessBrush`.
5. **Git brand orange** (`#F05032`) — defensible as a brand asset; ship a `Git.Brand` token if zero-literals is the goal.

### E5. WindowsFileSystem's five process-global mutable registries (M)

`DefaultActionRegistry.Instance`, `ExternalAppRegistry.Instance`, `FileMapManager.Instance`, `ShellNewRegistry.Instance`, `TemplatedCreateRegistry.Instance` — mutable config, service-located from views/actions, shared across all workspaces with no eviction on reconfigure. This collides with the per-Workspace scoping model and "Trust the DI." Either inject them per workspace via `FeatureManager`, or explicitly sanction them in Architecture.md as intentional process-wide caches (they do model genuinely machine-global state — file-type maps, external apps). Decide once; today it's implicit.

### E6. ViewModel outliers with clean split lines (M–L)

- `FileSystemViewModel` (75 KB, ~16 regions): three overlay concerns (→E1), Define-New wizard, tree management, right-panel management, ribbon pinning, background folder load. The one true outlier — extract overlays first (cheap, mechanical), then tree/right-panel collaborators. **L** in total, incremental.
- `JsonViewModel` (59 KB): the root-array **table mode** (:58, :1061) is a feature-within-a-feature, separable from the streaming/windowing reader. **M.**
- `ProductViewModel` (41 KB): consolidate the two duplicate confirmation regions; lift Snaplinks/Concerns/Restructure overlays. **M.**
- `TextViewModel` (42 KB): already partially decomposed; low priority.

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

### H2. Tests concern (333 open links) — make coverage self-truthing (S–M)

Beyond fixing the false table (§A3): add a reflection test that asserts every `Nexaflow.Features.*` assembly has ≥1 test class in `Tests.Features`, so the gap list can't rot again. The monolithic `Tests.Features` exe (references all 29 features) makes the per-feature edit-test loop pay a full feature-set rebuild — a per-feature `[TestCategory("<Feature>")]` convention is the cheap mitigation (**S**); splitting the project is the clean fix but **L**, defer.

### H3. Agent affordances (S–M)

- **`add-feature` skill** (`.claude/skills/`): encode the 8 touch-points from §B2, the correct `default-ribbon.json` location, and the verification commands. Highest leverage — it makes the riskiest workflow guided, and pairs with B2's tests as belt-and-braces.
- **Fast-loop docs**: one CLAUDE.md paragraph — build the feature csproj (not the solution), run the MSTest exe with `--filter FullyQualifiedName~<Class>` / `TestCategory!=UI`.
- **Per-feature README stubs** (5 lines: params, key files, canonical sibling): cheaper per-session context than re-reading the 276-line features.md for every touch. 31 files, do opportunistically.

### H4. Contract XML-doc thin spot (S)

`IFolderViewlet` members (`FolderNameGlob`, `ContainsFileGlobs`, `SupportedModes`, …) carry no per-member `<summary>` — the glob/percentage semantics live only in Architecture.md prose. Since interface XML docs are the agent's primary source (and elsewhere excellent), bring this one up to the bar; audit `Viewlets/` + `Ribbon/` for the same.

---

## Suggested sequencing

Cheapest, highest-confidence first — same discipline as the last review:

1. **Bug/leak trio** ✅ done — F1 cancellation ×4 + Gemini `ct`, F2 Aria dispose (+ discovery-lock fix), G2 ribbon leak. *(S)*
2. **Docs truth pass** (A1–A5) ✅ done: Workspace vocabulary rewrite, dead instructions, coverage table, missing projects/elevation, SoC refresh — plus de-listing to the product tree + skill query recipes. *(M)*
3. **Enforcement** (B1–B3) ✅ done: architecture tests + touch-point completeness tests + Providers in CI. *(S–M)*
4. **Contract moves + tool seam** (C1, C2, C4): the blessed relocations, `ClientToolBase`, tool polish. *(S–M)*
5. **Overlay primitive + converter hoists** (E1, E2, E3): the biggest duplication kills; E1 also unblocks the E6 VM splits. *(M)*
6. **Provider consolidation** (F3, F5, F6): shared stream helper + resilience, per-model capabilities, DPAPI secrets. *(M)*
7. **Performance pair** (G1 image virtualization, G3 minimize-aware timers); the rest of §G opportunistically. *(M)*
8. **Shell splits** (D1, D2) when next touching those files — extract-on-touch rather than big-bang. *(M)*
9. **AI-Ready campaign** (H1–H3): recipe + skill, then batch-close concern links worst-area-first. *(M, ongoing)*

Deliberately deferred: the colour analyzer (B4 — do E4 first), `IShellServices`/`IAIService` splits beyond the two facet peels (C5), RibbonEditor rewrite (D3), per-feature test project split (H2), and any umbrella file-reader abstraction (the four windowing strategies remain correctly feature-specific — unchanged verdict from the last review).
