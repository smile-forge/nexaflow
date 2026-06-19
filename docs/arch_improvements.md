# Architecture Improvements

_Review date: 2026-05-31. Scope: contract-layer hygiene, file-reading commonality, and visual/control standardisation, prompted by the recent burst of feature work._

## TL;DR — verdicts at a glance

| # | Observation | Verdict | Effort |
|---|-------------|---------|--------|
| 1 | `ClientBlockParser` / `ParsedAssistantTurn` in `Features.Common` | **Move to Core.** Core-only protocol parsing, not a contract. | S |
| 1b | `ClientPlan` / `ClientPlanStep` in `Features.Common` | **Keep.** Named in the `IToolApprovalCoordinator` contract — genuinely cross-boundary. | — |
| 2 | `OpenPageRequest` in `Features.Common` | **Move to Core.** Live, but Core-internal command plumbing. Not redundant with `OpenTab`. | S |
| 3 | File-reading commonality | **Extract leaf utilities, not an umbrella reader.** Encoding/BOM + file-watch now; resist `IFileReader<T>`. | M |
| 4a | Markdown visual library | **On track.** Migrate Scratchpad next (uses raw `RichTextBox`). | S |
| 4b | Duplicated toolbar styles (`TbBtn`/`TbToggle`/`TbSep` × 8) | **Promote to the app-merged dictionary.** Highest value / lowest risk. | S |
| 4c | Duplicated list/grid styles (FileSystem ↔ Search) | **Extract** — they already carry "matches X exactly" comments. | M |
| 4d | TreeView | **Extract the item chrome only**, keep data templates per-use. | M |
| 5 | `CLAUDE.md` / docs project layout is stale | **Refresh.** Four features + both `Visuals.*` libs undocumented. | S |

The layering itself is healthy: `Features.Common` references only `CommunityToolkit.Mvvm`, no feature references Core or another feature, no circular edges. The items below are leaks and duplication that have accreted, not structural rot.

---

## Update — 2026-06-20 (actioned)

A follow-up rationalisation pass picked up the outstanding visual/leaf items plus duplication that re-grew across the newer features (Processes, SystemInfo, WindowsApps, WindowsRegistry, Git, Dotnet):

- ✅ **Item 3 — file-reading leaves.** New `Nexaflow.IO.Common` (net10.0, no WPF) now owns `EncodingDetector` (Tabular's detector promoted as canonical; Tabular repointed) and a debounced `FileChangeWatcher` (Logs + Text migrated off their per-event `Task.Delay(300)` copies). The umbrella reader and placeholder-scrollbar machinery remain deferred, as recommended. Json's BOM-skip and Logs/Text encoding heuristics were left bespoke — they're interwoven with each reader's strategy, not verbatim duplicates of `EncodingDetector`.
- ✅ **4c — list/grid styles.** Shared `GridColumnHeaderStyle` / `GridColumnHeaderPaddingTemplate` / `FileListRowStyle` now live in the app-merged `Styles.xaml`; FileSystemView and SearchView reference them by key (their `SortHeaderTemplate` data templates stay local).
- ⏸️ **4d — TreeView chrome.** Left as-is by decision: the three trees (FileSystemView, FileMapEditor, the Core browser windows) have diverged in expansion/selection/event wiring (e.g. FileMapEditor is always-expanded; the Core windows use `EventSetter` double-click/expand handlers), so a single shared `TreeViewItem` style would be a lowest-common-denominator that changes behaviour. Per this doc's own "node models differ" caution.
- ✅ **New: size/duration formatting → `Visuals.Common`.** ~8 copies of the 1024-scaling byte formatter (Processes, SystemInfo, WindowsApps, WindowsFileSystem, Hex, Json, Logs, Text, AIChat, WindowsSearch) collapsed into `SizeFormatter` + `DurationFormatter`; `BytesToTextConverter` moved there too.
- ✅ **New: client-tool arg reader → `Features.Common/ClientTools`.** 6 features each re-implemented `JsonObject`→`Str/Bool/Int` (some fragile); unified into `ToolArgs`. Feature-specific extras (Git `Cap`, FsTool path/security, RegArgs `OnUi`, Processes nullable `Int`) kept local.
- ✅ **New: collection dedupe.** Json's `BulkObservableCollection` deleted in favour of the existing `Visuals.Common/RangeObservableCollection`.

Items 1 (`ClientBlockParser`/`ParsedAssistantTurn` → Core), 2 (`OpenPageRequest` → Core), and the FileSystemView inline-overlay flag remain open — out of scope for this pass.

---

## 1. Client-tool types in `Features.Common`

Your instinct is right, but it splits three ways, not one. The deciding test is **"does a `Features.Common` contract name this type in its signature?"** — not "who happens to call it today."

- `src/Nexaflow.Features/Nexaflow.Features.Common/ClientTools/ClientBlockParser.cs` — static parser for the `client_tool` / `client_plan` / `client_prefill` fenced-block wire protocol. Called **only** from `Core/Services/AIService.cs` (`RunAgentAsync`, ~line 337). No contract names it. → **Move to Core**, e.g. `Core/Services/Agent/`.
- `.../ClientTools/ParsedAssistantTurn.cs` — the parser's return type. Produced by `ClientBlockParser`, consumed only by `AIService`. No contract names it. → **Move to Core** with the parser.
- `.../ClientTools/ClientPlan.cs` (`ClientPlan` + `ClientPlanStep`) — **stays.** `IToolApprovalCoordinator.RequestPlanApprovalAsync(ClientPlan, …)` is a `Features.Common` interface, implemented by `ShellViewModel` and handed to `IAIService.RunAgentAsync`. The type is part of a cross-boundary contract even though no *feature* implements that side today.

Everything else under `ClientTools/` is correctly placed: `IClientTool`, `DelegateClientTool`, `ClientToolParameter`, `ToolCall`, `ToolResult`, `ToolSafety`, `IToolApprovalCoordinator`. Features implement `IClientTool` via `IPageViewModel.GetClientTools()`; the shell implements the coordinator. These are the genuine contract surface.

**Net:** move two files (parser + its DTO) to Core; the protocol parsing is an implementation detail of Core's `IAIService` implementation, and `Nexaflow.Tests.Core` already references Core so the unit tests move cleanly with them. This is a small, mechanical change that makes `ClientTools/` mean exactly "the client-tool contract" again.

## 2. `OpenPageRequest`

`src/Nexaflow.Features/Nexaflow.Features.Common/OpenPageRequest.cs` is a one-line record `(PageKind, PageParams)`. It is **not dead** — `PaneView.xaml.cs` wraps a breadcrumb event into one and fires `ShellViewModel.OpenPageCommand`, whose handler unwraps it and calls `_shellServices.OpenTab(...)` (`ShellViewModel.cs:521`).

But it is **not interchangeable with `IShellServices.OpenTab` either.** `OpenTab` is a method call (the path features use); `OpenPageRequest` exists purely because WPF `ICommand` parameters want a single bound object. It's the *command-parameter shape* of the same intent. So:

- Don't try to "replace it with `OpenTab`" — different mechanism (command binding vs. direct call).
- Do **move it to Core.** Only Core's `PaneView` and `ShellViewModel` touch it; no feature does. It's shell-internal MVVM glue sitting in the feature-contract layer. Same category of leak as item 1, smaller.

## 3. Reading text / structured / binary files with windowed streaming

This is the interesting one, and the tension you named is the correct one to feel. My recommendation: **extract the small mechanical leaves that are provably duplicated and semantically stable; do _not_ build a unifying reader service.**

### Why no `IFileReader<T>` / `FileReadingService`

The four serious readers use *fundamentally different access patterns*, and an umbrella interface would have to abstract over all of them at the lowest common denominator:

| Feature | Strategy | Anchor |
|---------|----------|--------|
| Logs (`LogViewModel`) | **tail-first**, background head-load in 64 KB chunks, append-delta on watch | byte seek from EOF |
| Text (`TextViewModel`) | **head-first**, pre-scan line index, sliding 100 KB window + placeholder-newline padding for scrollbar | `_lineByteStarts[]` |
| Tabular (`RowWindowReader`) | **full-rescan per window**, no byte anchors (StreamReader buffering makes `Position` unreliable — see CLAUDE.md note) | line count |
| JSON (`JsonFileLoader`) | **seek-by-item**, per-depth-1-item byte-offset index, load/unload 50-item batches keeping ≤300 in memory | `SortedList<int,long>` |
| Markdown / Images | full load / delegate to `BitmapImage` | — |

These aren't variations on one algorithm — they're four different answers to "what does 'next' mean for this data shape." Forcing them behind one interface buys nothing and, with the **xml viewer + in-place editing** features you have planned, you'd be widening that interface every time. You already discovered this once: Tabular's `IRowSource` is a clean abstraction *because it's tabular-specific*. Keep that pattern — abstractions per data shape, not per file.

So: leave `LogViewModel`'s tail loader, `RowWindowReader`, `JsonFileLoader`, and `TextViewModel`'s windowing exactly where they are. Duplicated *strategy* is fine here, exactly as you said.

### What _is_ worth extracting (the mechanical leaves)

These repeat verbatim, have stable semantics, and have nothing to do with data shape:

1. **Encoding + BOM detection.** Logs, Text, Tabular, and JSON each re-implement "sniff BOM, pick encoding, skip BOM bytes." Tabular already has a private `EncodingDetector` — promote that one. Highest-confidence extraction.
2. **Debounced file-change watching.** Logs and Text both wrap `FileSystemWatcher` with the same LastWrite+Size filter and ~300 ms debounce. A `FileChangeWatcher` wrapper removes two near-identical copies and one class of subtle bug (debounce races).
3. **(Maybe) a `LineOffsetIndex` helper** — the byte-offset-per-line-start array that Text builds and Tabular/JSON conceptually share. Lower confidence; the consumers index slightly differently. Hold until a third consumer wants it.

Explicitly **do not** extract the virtual-scrollbar placeholder machinery yet. Text pads an AvalonEdit document with placeholder newlines; JSON inserts sentinel nodes into an `ObservableCollection`. Same *idea*, structurally different *implementations*. That's a rule-of-three "not yet" — revisit when the xml viewer becomes the third instance.

### Where the leaves should live

`Features.Common` is a pure contract project (MVVM only). Encoding sniffing and a file-watch wrapper are concrete infrastructure with no contract nature, so dropping them there muddies "Common = contracts." You already established the precedent for shared *non-contract* libraries with `Nexaflow.Visuals.Common` / `Nexaflow.Visuals.Text`. Mirror it: a small **`Nexaflow.IO.Common`** for these utilities, referenced by the feature projects that read files. One concrete recommendation, consistent with the structure you already trust.

## 4. Visual standardisation

### 4a. Markdown library — on track

`Nexaflow.Visuals.Text` (`MarkdownView` / `SelectableMarkdownView` / `MarkdownFlowDocument`) is correctly extracted and already used by Core's `AiResponseOverlay` and AIChat's `ConversationView`. **Scratchpad is the gap** — `PostItControl.xaml` still uses a raw `RichTextBox` and the project doesn't reference `Visuals.Text`. Migrating it is the next step you already identified; nothing more to design.

### 4b. Toolbar styles — the highest-value win

`TbBtn`, `TbToggle`, `TbSep` are **copy-pasted into ~8 feature views** (Markdown, Text, Json, Hex, Logs, Tabular, Images, Scratchpad) — 40–60 lines of identical `ControlTemplate` each. Crucially, they already reference app-level theme brushes by key (`{StaticResource Surface2Brush}`, `{StaticResource BorderBrush}` — confirmed in `TextView.xaml:14`). That means **they resolve through the same app-merged resource dictionary the brushes do** — no assembly reference, no layering cost.

So the fix is loose-coupled and cheap: define `TbBtn`/`TbToggle`/`TbSep` once in the app-merged dictionary (`Core/Themes/Styles.xaml`, alongside the existing shared `ToggleSwitch`/`IconButton`), then delete the eight local copies and reference by key. This is the single best effort-to-payoff item in the review: ~400 duplicated lines gone, every toolbar re-themed from one place, and the feature views shrink to markup.

> Note: defining the style in Core is *not* a layering violation — features bind to it by string key via the resource system exactly as they already bind the brushes. No compile dependency is introduced.

### 4c. List / grid styles — extract, they're confessed duplicates

`FileSystemView.xaml` and `SearchView.xaml` carry the **same** `FileRowStyle`/`ResultRowStyle` and `GvColHeaderStyle`, with literal comments saying _"matches FileSystemView exactly"_. That's the codebase telling you to extract. Two routes:

- Cheap: move the shared styles into the app-merged dictionary (as 4b).
- Cleaner: a `FileListView` UserControl in `Visuals.Common` if the row/column behaviour (sort, selection) is also duplicated — likely, given the headers match. Prefer this if a third list view is on the horizon; otherwise the style move is enough.

### 4d. TreeView — share the chrome, not the control

Three TreeViews exist: the elaborate one in `FileSystemView.xaml` (chevron + hover/selection `TreeViewItem` template, ~150 lines), the simple pickers in Core's `FileBrowserWindow`/`FolderBrowserWindow`, and the minimal one in `FileMapEditorControl`. The **visual chrome** (the `TreeViewItem` template) is genuinely shareable; the **data** (file-system nodes with lazy-load vs. experience-map nodes) is not. So extract the `TreeViewItem` style/template into the shared dictionary and keep each `HierarchicalDataTemplate` local. Don't attempt a single "file tree control" — the node models and lazy-load differ, and you'd be back to a lowest-common-denominator abstraction.

### Theming & automation angle

You called out theming and UI-test automation as goals — both are served by the same move. Each style/control extracted to a single keyed definition is one place to re-theme, and a named shared control is a stable hook for automation. When you extract the toolbar and list controls, give them `AutomationProperties.AutomationId` (or `x:Uid`) so the existing UI test category (`TestCategory=UI`) can address them without brittle visual-tree walks. Cheap to add at extraction time, expensive to retrofit.

### Already-shared overlays — a smaller flag

`FileSystemView` ships its own `InputPromptOverlay` / `ConfirmationOverlay`, but `IShellServices` already exposes `ShowPrompt` / `ShowConfirmation`. Worth checking whether the inline overlays predate the contract and can route through it instead — one fewer bespoke modal to theme and test. (Flag, not a verdict — I didn't deep-read the overlay behaviour.)

## 5. Stale docs (housekeeping)

The project layout in `CLAUDE.md` (and the same content in `docs/Architecture.md`) is missing features that now exist: **`WindowsFileSystem`, `Json`, `Hex`, `Tabular`**, and neither shared UI library (**`Nexaflow.Visuals.Common`, `Nexaflow.Visuals.Text`**) is documented. Since you've told Claude to read `docs/Architecture.md` / `docs/features.md` before exploring, a stale map costs every session. Refresh it when convenient.

---

## Suggested sequencing

Cheapest, highest-confidence first:

1. **Toolbar styles → app-merged dictionary** (4b). Biggest duplication removed, near-zero risk.
2. **Move `OpenPageRequest`, `ClientBlockParser`, `ParsedAssistantTurn` to Core** (1, 2). Pure relocation; tests follow.
3. **Scratchpad → `Visuals.Text`** (4a). You'd planned it anyway.
4. **`Nexaflow.IO.Common` with `EncodingDetector` + `FileChangeWatcher`** (3). Migrate one consumer at a time.
5. **List/grid + TreeView chrome extraction** (4c, 4d), with AutomationIds.
6. **Refresh `CLAUDE.md` / docs layout** (5) — fold in as you touch each area.

Items 3's umbrella reader, 3's placeholder machinery, and 4d's "one tree control" are deliberately **deferred until a third consumer appears** — the xml viewer is the natural forcing function. Extracting them now would lock in the wrong seam.
