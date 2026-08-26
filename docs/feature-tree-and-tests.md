# Modelling a feature in the product tree (and testing it)

This is the template for representing a feature as product-tree nodes and backing each node with the
*right kind* of test, so the tree stays an honest, mechanically-checkable map of **what exists** and **what's
tested**. It was distilled from the **Text Viewer** gold-standard pass (2026-07) and is meant to be applied to
every feature. **Text Viewer, Code, Tabular, Markdown, Processes, SysInfo, Installed Apps, Win Registry, Log
Viewer, Images, Audio, 3D Model, Scratchpad, Projects, Video, Notebook, Win Search, AI Chat, DICOM, Console, Win File System, Hex, Json, SVG, Email, Virtual Disk, Compressed and Product** have all had the pass and all lint clean — read whichever is closest in
shape to the feature you're modelling:

| Reference | Read it for |
|-----------|-------------|
| **Text Viewer** | the canonical shape: many toolbar controls, a state-gated group (`Edit_mode`), pop-over panels |
| **Code** | a feature whose UI lives in a *shared* control (`Nexaflow.Visuals.Text.Editor`) — the panels/leaves are modelled on the Code tab even though the XAML is elsewhere |
| **Tabular** | the widest UI: five panels, a state node for the header context menu, and a Functionality subtree (detection/parsing) deeper than the UI one |
| **Markdown** | the smallest UI over the largest shared control — and the worked example of *extracting a pure seam* so a leaf becomes unit-testable (§3) |
| **Processes** | a feature spanning **two tabs** (the list and the per-process details page) under one UI node, plus a Functionality subtree for the sampling/reconciliation/tree-building behind the grid |
| **SysInfo** | a feature spanning **three tabs** (Dashboard / Services / Environment Variables) — one panel per page — over a system-probe layer |
| **Installed Apps** | the shape of a *destructive* row menu: the safety gate is the leaf's test, and the journey never opens the menu at all |
| **Win Registry** | a feature where every write routes through an in-tab overlay — the overlays are their own panel, and the leaves are tested at "the right prompt opened, seeded correctly, and the guard fired" |
| **Log Viewer** | a *live* surface: the file watcher, pause and follow are three separate leaves because their test states differ, and the status bar is a panel of one-line readouts |
| **Images** | four mutually-exclusive content surfaces, each its own panel under one UI node, with the shared floating tools as a fifth — and the collage's pan/zoom maths pulled out as a pure seam |
| **Audio** | the shape of a feature over a *device*: the readouts either side of playback are unit-tested, the device-bound half is `shouldnt` with a note naming what covers the rest |
| **3D Model** | the same again for a live viewport — the camera maths came out of the code-behind (`CameraMath`) so every gesture and AI camera tool is assertable without a rendered scene |
| **Video** | what to do when the whole feature sits on a native engine: the window *before* the engine exists is the tested one, because that is where the tab is actually exposed |
| **Scratchpad** | a canvas rather than a document — and where a seam turned out to belong in `Visuals.Common` because two features had grown the same one |
| **Projects** | two tabs plus two file-explorer viewlets under one UI node, over an operations layer that already carried most of the tests — largely a re-pointing job |
| **Notebook** | the smallest complete example — two panels, four behaviours — and where the pass closed a readiness gap the tree had already written down |
| **Win Search** | a feature whose core (the index query) genuinely cannot run headlessly, so everything either side of it is what carries the tests |
| **AI Chat** | the hardest `shouldnt` calls: approvals and interjections only exist inside a running agent turn, so the note has to say what covers them instead |
| **DICOM** | a feature that was *built* without the backbone and retrofitted: read it for what that costs — nine flat children, seven ids too generic to live in one namespace, and a duplicate feature root nobody had noticed |
| **Console** | a feature whose logic almost all lives in shared libraries (`Visuals.Terminal`, `IO.Terminal`), so its leaves snaplink outward and its one irreducible decision — where a typed line goes — had to be lifted out of a live PTY to be testable at all |
| **Win File System** | the largest subtree, and the reference for an action strip: most buttons end in a shell call or the clipboard, so the tests sit on the *gate* and the `shouldnt` notes have to say where each rule is actually asserted |
| **Hex** | a feature that was already well tested and simply unlinked — read it for the re-pointing job, and for splitting one omnibus AI test into one per tool so eight leaves stop all naming the same method |
| **Json** | where the guard is the whole story: the document is held windowed, so Save and Format have to refuse while any part is unread, and that refusal is what the leaf's test asserts |
| **SVG** | the smallest complete example — one canvas, three toolbar controls — and the second surface to need the fit/zoom seam, which is what moved it into `Visuals.Common` |
| **Email** | a feature modelled *only* below the waterline — parse, VFS, AI — with the whole visible half unwritten, so read it for building a UI subtree from the view rather than re-pointing an existing one |
| **Virtual Disk** | the same again but starker: eight nodes of which seven were AI. Also the reference for a viewer that deliberately opens nothing — content is reached by browsing the image, so a file inside has one open path rather than two |
| **Compressed** | the widest Functionality subtree — one backend assembly per container family — and an action bar where every destructive action goes through a choice or password overlay, so the leaf tests are all "cancelling leaves the archive byte-for-byte as it was" |
| **Product** | the multi-view reference: three views (five planned) as panels under one UI node, one shared Functionality subtree, and *one* AI node — because the views are views of one tree. Also where `get_context` splits per view while the tool set stays shared |

### Testing a feature that acts on the machine

Processes, SysInfo, Installed Apps and Win Registry all kill, uninstall, reconfigure or overwrite real
system state, so "drive the control and assert the outcome" is not available. The pattern that replaces it:
**assert the gate, not the effect.**

- Every destructive leaf's test declines its confirmation (or its UAC prompt) and asserts that *nothing*
  reached the bridge / the background queue — a passing test therefore proves the guard exists, and it can
  never damage the machine it runs on.
- Where the action goes through `IShellServices.RunElevatedAsync`, assert the **request**: the right
  operation name carrying the right target (`service.stop` on `Spooler`, `env.set` with `Target=Machine`).
  That pins the wiring without a privileged run.
- A **declined** elevation must be silent and a **failed** one must surface — assert both; they're easy to
  collapse into one path by accident.
- The UI journey stays on the read-only controls and says in its summary why the rest are excluded.

Related: the tree/CLI mechanics live in the [product-folder skill](../.claude/skills/product-folder/SKILL.md)
and [CLAUDE.md → product tree]; testing conventions in [testing.md](testing.md); the AI-page contract in
[features.md](features.md); theming in [theming.md](theming.md).

> The `.product/` tree is gitignored working state (the app live-reloads it). Edit it **only through the
> `nfi` CLI**, never by hand — see *The CLI* below.

---

## 1. The shape of a feature subtree

Each feature root lives under `Features` and has (up to) three children — **UI**, **Functionality**,
**AI Integration** — so the three concerns never mix:

```
<feature>                         (feature root — carries the "AI Ready" maturity verdict)
├─ UI                             everything the user sees or touches
│  ├─ <Panel>                     first layer = panels: a distinct visual region / surface
│  │  ├─ <Control>                leaf: one node per button / toggle / input / display
│  │  └─ <State group>            a logical grouping of controls that share a state (e.g. Edit_mode)
│  │     └─ <Control> …
│  ├─ <Panel (state-governed)>    a panel may be gated by a state (Search Bar ← IsSearchActive) — still a panel
│  └─ …
├─ Functionality                  behaviours the feature performs that are NOT a UI control or an AI tool
│  ├─ <Behaviour>                  the "steps of a use-case": search engine, windowing, encoding-detect,
│  └─ …                            file-monitoring, confined-window editing, file-splitting, …
└─ <feature>-ai                   AI Integration
   ├─ <feature>-ai-context        get_context honesty
   ├─ <feature>-ai-act            client tools — ONE leaf per tool (<…>-ai-act-<tool>)
   └─ <feature>-ai-preview        IContextPreview
```

**Panel vs. state node** — the one subtlety:
- A **panel** is a distinct visual surface (the toolbar, the editor, the status bar, a pop-over like the
  search bar or split panel). It is a first-layer child of `UI`. It gets a `theming` concern.
- A **state node** is a *logical grouping* of controls that only appear in a state, but which share the
  parent panel's surface (e.g. `Edit_mode` groups Save / Cut / Paste / the "Editing" button, all living inside
  the toolbar). It has **no concerns** — it's pure structure.
- Split a control into two nodes when the same widget presents as two things to the user in two states — e.g.
  the editing toggle is an **Edit** button (read-only state) and a separate **Editing** button (edit state);
  "it's one ToggleButton" is an implementation detail.

**Granularity:** a node for every button and every display control. Split a group (cut/copy/paste) into
separate leaves when their **test states differ** (copy works read-only; cut/paste are edit-only), because each
is tested separately.

### A feature with more than one view

Several features are more than one tab — SysInfo is three pages, Processes and Projects and AI Chat are two,
Product is three with two more planned. It is tempting to make each view its own subtree with its own
UI/Functionality/AI beneath it, because a view genuinely *is* a bundle. Don't: **category first, views as
panels under `UI`.**

The reason is that the concerns don't decompose per view. `AI Ready` is a single feature-level verdict by
definition; `theming` only ever sits on UI nodes. Split by view and "does this feature's theming hold up?"
becomes a mental union of N subtrees instead of one node's derived status — and the feature-wide AI node has
nowhere natural to live. (Product had tried view-first, and its `product-ai` had ended up parented *inside*
the Sunburst view.)

The navigational appeal of the view bundle is real, though, so keep it — in the **id prefix** rather than the
hierarchy, which can only encode one grouping:

```
UI            → product-integrity            (the page, a panel)
                product-integrity-issue-list
Functionality → product-integrity-scan
AI            → product-ai-context-integrity
```

`find integrity` then returns the whole view bundle across all three categories in one call. The shape gives
you the concern roll-up; the namespace gives you the view bundle.

**The one thing that does split per view is `get_context`.** Each view has its own `IPageViewModel`, so its
own `GetContext`, its own readiness gate and therefore **its own test state** — which is the rule from
*Granularity* above. Collapsed into a single `<feature>-ai-context`, a view whose context is untested is
invisible; splitting Product, SysInfo, Processes, Projects and AI Chat immediately surfaced five untested
contexts, including AI Chat's entire context surface and `ProcessDetailViewModel`'s distinctive
`HasData || IsGone` gate (a process that has exited is describable as *gone*, rather than waiting forever for
data that will never arrive).

The **tool set does not split**: the views act on one underlying model, so a tool means the same thing
wherever it is called from. Only tools that act on what is *rendered* — the graph canvas's `focus_node` and
`read_graph` — are view-specific, and they sit beside the model tools under the one `-ai-act` node.

---

## 2. Concerns, by role

The concern vocabulary is fixed in `product.json` (`theming`, `tests`, `docs`, `i18n`, `AI Ready`,
`Expanded`). Which concerns a node *should* carry depends on its role:

| Role | `theming` | `tests` | `AI Ready` | notes |
|------|:---:|:---:|:---:|-------|
| **Feature root** | ✔ | (derived) | ✔ (only here) | `AI Ready` is the human maturity verdict; it lives **only** on the feature root and nowhere below. |
| **Panel** | ✔ | — | — | theming, **no tests** — the one UI journey (below) covers the integrated interaction. |
| **State node** | — | — | — | no concerns; pure grouping. |
| **Leaf control** | ✔ | ✔ | — | theming + tests. |
| **Functionality behaviour** | (if it renders) | ✔ | — | tests; most add `theming` only if they own a visual. |
| **AI act leaf** | — | ✔ | — | tests → the tool's test; may carry `Expanded` if the tool exceeds what the user can do. |

`theming`, `tests` are `is_default` (auto-attach to new nodes as `should`). `AI Ready` is **not** default (so
`add-node` no longer sprays it onto leaves). If a concern auto-attaches where it doesn't belong, strip it with
`remove-concern`.

---

## 3. The testing model

Two tiers, matching the two node roles:

1. **One UI journey per feature**, tagged `[CoversNode("<the feature's UI node>")]`. It launches the app once
   (amortising the ~20 s start) and drives the interactive controls in a single pass — the *integration* test
   at the UI level. See `TextJourneyTests` and `UiJourneyTestBase` (soft `CheckPresent`/`CheckInvoke`/`Check`
   so one broken control doesn't hide the rest). Interactive-desktop only (`TestCategory=UI`).
2. **One unit test per leaf control**, tagged `[CoversNode("<leaf-id>")]`, driving the **view-model** command
   or state behind the control (NOT a UI test). One method may cover several leaves — tag each. This is where
   the real per-control assertions live; the journey just proves the wiring holds end-to-end.

**Functionality** behaviours are VM unit tests too — everything under `Functionality` should be unit-testable,
or explicitly declared `shouldnt`.

### Before declaring a leaf untestable, look for the pure seam

The commonest reason a leaf "can't be tested" is that its *rule* is buried inside a WPF control alongside the
caret, the selection and the document rebuild — not that the rule is untestable. Lifting the rule out is
usually a few lines and leaves the control thinner:

- **Markdown's formatting mini-toolbar** — heading / bold / quote / code-fence were private methods on
  `InlineMarkdownEditor` mutating `_blocks[_active]`. The text rule moved to `MarkdownBlockFormat` (pure
  `(block, …) → (newBlock, caret)`); the editor keeps the caret and the rebuild. Four leaves went from
  `should` to a real unit test.
- **Markdown's scroll-to-heading deep link** — the interesting part is matching a `>`-joined heading path
  against the block list (so duplicate names under different parents stay distinct), not the
  `ScrollToVerticalOffset`. It moved to `MarkdownBlocks.FindHeadingBlock`.
- **Code's `.xshd` theming** — the name→role heuristic moved out of `XshdTheming` into `SyntaxTokenMap`,
  which already owned the tree-sitter capture→role map. One role palette, both engines, both testable.
- **SysInfo's health colouring** — the status→`TextSwatch.*` mapping came out of `StatusToBrushConverter.
  Convert` as `ResourceKey`, so "every status resolves to a semantic token, and an unknown one paints as
  plain text rather than implying a verdict" is assertable without an `Application`.
- **Block-level undo** — no seam to extract, but `Undo()` became `public` (as `TextBox.Undo()` is), so the
  step *granularity* — one step per block session, not per keystroke — is assertable.
- **3D camera moves** — orbit / turn / roll / zoom / pan and the authored-view framing were private methods
  on `Model3DView` reading and writing `Viewport.Camera`. The arithmetic moved to `CameraMath` as pure
  `CameraPose → CameraPose`; the code-behind kept "read the camera, apply, write it back". The turn gesture
  went from `should` to done, and every AI camera tool gained a real assertion instead of only a
  fails-without-a-viewport one.
- **Pan/zoom canvases with an overview minimap** — cursor-anchored zoom, centring, and the canvas↔minimap
  mapping and its inverse were interleaved with `ScaleTransform`/`Canvas.SetLeft` in both `ImageView` and
  `ScratchpadView`. They live in `Visuals.Common`'s `PanZoomMiniMap` — the second feature was where it
  became clear the seam was shared, not feature-specific (the collage had been copied from the scratchpad).
  "Zooming keeps the point under the cursor still", "the minimap only appears when something is off-screen"
  and "clicking the minimap centres the viewport there" are now asserted once, for both.
- **Resizing a rotated post-it** — the rule is that the corner you are *not* dragging stays put, which needs
  the drag projected onto the note's own axes. It came out of `PostItControl` as `PostItGeometry.Resize`;
  unrotated resizing is arithmetic nobody gets wrong, and the rotated case is what the tests are for.
- **Fitting content into a viewport** — the DICOM stage cannot lean on WPF's `Stretch`, because the
  measurement overlay is drawn in *screen* space (so strokes stay one width at any zoom) and the view
  therefore owns the matrix; the SVG canvas cannot either, because it re-tessellates rather than scaling a
  bitmap. `Visuals.Common`'s `ViewportFit` holds fit, actual-size and cursor-anchored zoom for both — it
  started inside the DICOM feature and moved out one batch later, when SVG turned out to be doing the same
  arithmetic by hand. Note what the tests turned out to be about: that fit letterboxes rather than crops,
  that 1:1 lets oversized content overflow rather than being clamped, and that a viewport layout has not
  measured yet yields the identity instead of an infinite scale.
- **Where a typed line goes when you press Enter** — the terminal's one genuinely consequential decision,
  and it was unreachable: `HandleEnter` read a private `_atPrompt` flag that only a live pseudo-console sets.
  The inputs are trivial (is the cursor on a prompt, what was typed, what this shell calls a built-in), so
  `TerminalEnterRouting.Decide` takes them directly. The third case is the one worth having a test for —
  mid-program there is no prompt, and the line belongs to whatever is reading stdin whatever it looks like.
- **A panel's listing order** — the terminal's Files panel deliberately lists files *before* folders, the
  opposite of Explorer, because the panel exists to drag a path onto the console. That inversion is the kind
  of thing a later "fix" quietly reverts, so it moved to `TerminalFileList.Enumerate`.
- **An editor's rename semantics** — the console environments editor pins folders to an environment *by
  name*, so a rename is the moment every pin can silently stop resolving. `ConsoleEnvironmentEditing` holds
  the three rules that fail without a symptom: pins follow a rename unless another environment still owns the
  old name, a new environment must not collide, and the last one cannot be removed.

Reach for this before reaching for `shouldnt`. Prefer changing the abstraction's shape over adding a
test-only hook — and when the *second* feature needs the same seam, that is the signal it belongs in
`Visuals.Common` rather than twice over in two code-behinds.

A **device** is not automatically an excuse either. Audio's engine only exists after the first play, so
"every transport command is safe before it exists" — the half that actually breaks — is unit-testable even
though playing a sound is not. Split the leaf's rule from the device call and test the half you can.

Some leaves are only reachable through the real control (a rendered `RichTextBox`, a live AvalonEdit
selection). Those get a control-level test in an off-screen window (`MarkdownEditorHarness`) tagged
`TestCategory=UI` — still one test per leaf, just not headless.

**When a node genuinely can't be unit-tested → `tests=shouldnt` + a `note` saying why and who covers it.**
Recurring cases:
- **WPF `ApplicationCommands` forwarders** (cut/copy/paste, a right-click menu) — need a *focused* control;
  no VM state to assert. Covered by the UI journey's presence check.
- **Passive displays** with no distinct VM behaviour.
- **Live-pipeline / STA-render tools** — WebView2 capture/scroll, `PngBitmapEncoder`/`RenderTargetBitmap`
  image capture, a live media/3D viewport, a live PTY/`cmd.exe`. Test the graceful *no-surface* error path if
  there is one; declare the real behaviour `shouldnt`.

Don't loop trying to test the genuinely-hard ones — mark `shouldnt` + note and move on.

---

## 4. Snaplink discipline

- A leaf's `tests` concern, once `done`, **snaplinks to the test that covers it** — point it at the **unit
  test**, not the journey. The `ui` node's `tests` snaplink → the **journey**. Panels/state nodes have no
  `tests` concern, so no snaplink.
- Keep the tree snaplink and the test's `[CoversNode]` **in agreement** (same `Class.Method`). They're two
  channels for the same fact; drift between them is a smell (§6 proposes making it gating).
- Snaplinks are forward-looking: a `done` snaplink may point at a not-yet-merged test file. That's fine — the
  broken-link check only gates the **release/setup build**, never a plain `dotnet build`, and it resolves on
  merge.

---

## 5. The CLI (edit the tree only through it)

> **The in-app assistant has this same surface** — both families. Every verb below that reads or changes the
> tree is a `product_*` client tool, and every `graph` verb is a `graph_*` one, on all three Product views,
> running the same `Services.Initiatives` call and rendering through the same reporter (`ProductReport` /
> `GraphReport`) so the CLI and the assistant print identical text. Reads auto-run; the structural tree ops
> (`add-node` / `move` / `rename` / `remove` / `remap` / `doctor --fix`) and `graph build` are
> approval-gated. `batch` is deliberately CLI-only — the model already has the individual operations.
>
> So the loop this document describes — `find` the node, `query` what it owes, edit, then `validate` and
> `lint` to check the edit — is one a model can run unaided; before the tools existed it could set
> `tests=done` and had no way to learn it had just made an unbacked claim. The graph half is the same story
> for code: `graph_context` answers "what is this and what owns it" in one call, and `graph_grep` scopes a
> regex to a neighbourhood, which plain text search cannot do at all.


`nfi.exe` self-locates the `.product` tree (follows a git worktree to its main checkout), so
run it from anywhere with no root arg. Build once and call the exe directly, or use the prebuilt
`tools/graph-cli/` copy.

- **Discover:** `find`, `describe`, `describe <id> --code` (resolves each snaplink to its real source),
  `tree <id> [--full]` (the whole subtree as one outline — the view to start *and* finish a pass like this one
  with), `query` (filter by subtree/concern/status/leafness — e.g. `query --under git --concern tests --status
  should --leaf` = leaves still owing a test), `diff` (what changed since the last release snapshot),
  `graph …` (code/AST discovery).
- **Edit (one node):** `add-node`, `move <id> <new-parent>`, `rename <old-id> <new-id>`,
  `remove <id> [--recursive]`, `set-status`, `set-concern`, `remove-concern`, `add-snaplink`,
  `remove-snaplink`, `set-node`.

> **Address a snaplink by what it is, not where it sits.** `remove-snaplink <id> [--concern <tag>]` takes
> `--type/--doc/--class/--method/--target`, and every field you give has to agree: `--doc` alone drops every
> link into that file, adding `--method` narrows it to the one (paths compare slash- and case-insensitively,
> so a backslashed path pasted from Explorer still matches). `--index <n>` still removes by position, but a
> position is only valid until the next edit reorders the list — which is exactly what an earlier line in the
> same batch does — so the two are mutually exclusive and giving both is an error. Give neither and it clears
> the list, so say what you mean.

> **Ids are one flat global namespace.** `add-node` slugs the title, so a node titled "Run" under any feature
> claims the bare id `run`. Give every node a feature-prefixed id (`dotnet-verb-run`, not `run`) and use
> `rename` to fix an existing one — it retargets the parent, the children and every `node` snaplink, but
> **not** a `[CoversNode("old-id")]` in test source, which you must update by hand (NXCOV002 flags it).
- **Bulk / integrity:** `batch <file>` (transactional; `--dry-run` first), `doctor [--fix]`, `validate`,
  **`lint [--under <id>]`** — checks a feature against §1–§4 (backbone present, `AI Ready` only on the feature
  root, panels/state nodes journey-covered, every leaf unit-tested, a `done` `tests` concern naming its test).
  Advisory: roles are inferred from position, so a finding is a prompt to look, not a verdict, and nothing
  here fails a build. **Run `lint --under <feature>` at the start and end of a pass like this one** — the
  eight references above plus Git and DotNet all lint clean, so a finding means you've diverged from them.
  On an unconverted feature the first run reports only `MissingBackbone` (it short-circuits); add the
  `UI` / `Functionality` nodes and re-run to see the real list.

> `validate` resolves a snaplink's file against **your working tree first**, then the product root — so in a
> worktree it checks the code on the branch you're editing (matching `describe --code`) instead of reporting
> every not-yet-merged file as broken. The installer gate is unaffected: there they're the same directory.

> **A snaplink `doc` is always the repo's own path — never a path through a linked worktree.**
> `.claude/worktrees/<name>/src/Foo.cs` resolves while that branch is checked out and dies the moment the
> worktree is removed, so `validate` reports it as `WorktreePath` (gating) even though the file exists today,
> and **`doctor --fix` re-roots every one of them** back onto `src/Foo.cs`. The same normalisation happens
> upstream in `scan-tests`: a test DLL built inside a worktree carries that checkout's absolute paths in its
> PDB, and the manifest records the repo path — otherwise the Integrity page's *Add link* suggestions would
> seed the tree with links that break at merge.

**Workflow for a restructure:** generate a `.batch` file (one instruction per line — the standalone verbs
minus `<root>`; `#` comments; `"quote"` spaces), `batch … --dry-run`, apply, then `doctor` + `validate`.
Prefer generating the batch with a script over hand-writing dozens of lines.

**Moving files** is the same workflow: `remap <old-path> <new-path>` is a batch instruction too, so a move
that shifts several files lands as one validated transaction and `--dry-run` reports how many snaplinks each
line would rewrite before anything is written. Inside a batch a remap that matches nothing is an *error* (the
path came from a move you already made, so a miss means the script is wrong); standalone it is just reported,
because there "nothing references that path" is a fair answer to a question.

> **Arguments are strict.** Every verb declares exactly what it accepts, so an unknown option, a missing
> option value, or a surplus positional is a hard error naming that verb's usage — never silently ignored.
> `batch` parses each line the same way and is all-or-nothing, so one typo aborts before anything is written.
> Note in particular that **a note belongs to a node, not a concern**: `set-node <id> --note "…"`, not
> `set-concern <id> <tag> <status> --note "…"` (which is now rejected rather than quietly dropping the note).

---

## 6. Locking it down — what enforces the model

### Already enforced
- **`SnaplinkValidator`** (Integrity page + setup-build gate): every snaplink resolves (file/heading/class/
  method exists); a `requires_snaplink` concern that's `done`/`faulted` with no snaplink is gating.
- **NXCOV analyzer** (NXCOV001 missing / NXCOV002 stale id / NXCOV003 class-level over-claim) +
  **`CoverageDeclarationGuardTests`**: `[CoversNode]` declarations are present, valid, and don't over-claim.
- **`AiSurfaceRulesTests`** (`KnownNullScope`): a tool-bearing page must return a distinct
  `GetSecurityContext()`.
- **`FeatureTouchPointTests`** / architecture rules: add-a-feature wiring, reference/dispatcher rules.
- **`StructureLinter`** (`nfi lint`, advisory): the §1–§4 conventions, plus the two granularity rules
  (`LeafCoveredByTooManyTests`, `TooManySnaplinks`) — see *Granularity* below.

### Granularity — is the node about one thing?

Every other rule asks whether the tree *says* what it should. These two ask whether the tree is granular
enough to be worth saying anything about, and they are the only rules that read a node's **size** rather
than its shape. They come in a pair because the same node usually trips both, from opposite directions.

| Rule | Fires when | Evidence |
|------|-----------|----------|
| `LeafCoveredByTooManyTests` | a **leaf** is declared by more than `MaxTestsPerLeaf` (12) tests | the `scan-tests` manifest |
| `TooManySnaplinks` | **any node** carries more than `MaxSnaplinksPerNode` (12) snaplinks, its own plus its concerns' | the tree itself |

When a leaf accumulates far more tests than one-unit-test-per-behaviour implies, the tests have enumerated
behaviours the tree never named: the node's status then means "some of these work" and nothing can tell you
which, so `tests=done` is a claim about a dozen things at once. A node carrying a pile of snaplinks is the
same statement made in code references instead. Both thresholds are 12, deliberately — the honest ceiling
depends on how much code a feature involves and how user-facing it is, and no constant knows that, so one
catch-all number the reader can hold beats two tuned ones.

Two scoping rules worth knowing:

- **`LeafCoveredByTooManyTests` skips containers.** A panel accumulating its children's tests is the tree
  working. Snaplinks do not aggregate that way — a parent never inherits its children's — so
  `TooManySnaplinks` applies to every node.
- **Both run over the whole tree, not just `Features`.** The other rules are feature-shaped (a backbone, a
  panel's theming) and mean nothing outside it. "This node is about too much" means the same anywhere, and
  scoping it to features hid the worst case in the repo: `sequence-diagram`, under *Common / Shared*, at 29
  snaplinks.

`LeafCoveredByTooManyTests` needs the manifest, which is derived and gitignored — absent, it simply doesn't
run and every other rule still does.

```powershell
& $nfi scan-tests .          # refresh the manifest first — the tests half is silent without it
& $nfi lint --under common   # any node, not just a feature root
```

At the time of writing the repo had 15 findings across the two rules, and three nodes tripped both —
`sequence-diagram`, `quadrant-graph` and `registry-modify-value` (21 snaplinks, 19 tests). That overlap is
the signal: a node doing several jobs shows it in its links and in its tests at once. Fixing one means
`add-node` for the behaviours the test names already describe, then re-pointing the snaplinks — not
deleting tests.

### The gap
The **role-based rules in §2–§4 are conventions, not checks.** Nothing stops a future feature from giving a
panel a `tests` concern, sticking `AI Ready` on a leaf, marking a leaf `done` with no test, or letting the
snaplink and the `[CoversNode]` drift apart. Proposals to close it, cheapest first:

**(a) Turn on `requires_snaplink` for `tests`.** One flag in `product.json`. Instantly makes "a `done`/
`faulted` `tests` concern must name its test" a gating rule (it's off today). Zero new code — but **not free**:
`query --concern tests --status done --unbacked` currently returns **243 nodes**, so this needs a burn-down
(baseline the current set, gate only new violations) rather than a flip. `lint` already reports it per-feature
as `TestsDoneWithoutSnaplink`, which is the cheap way to hold new work to the rule meanwhile.

**(b) Add an explicit node `kind` — the enabling change for everything else.** Add `kind` to `ProductNode`
(`feature | panel | control | state | behaviour | ai-context | ai-act | container`), set via
`add-node --kind …`. Roles are currently *inferred* from position ("child of a UI node = panel", "no children
= leaf"), which is brittle. An explicit `kind` makes the rules below robust and self-documenting. (Store it as
a field, or — lower-friction — as a reserved concern/tag.)

**(c) A `StructureValidator` in the release-gate path** (alongside `SnaplinkValidator`; degrade to
presence-only when the tree is absent in CI). Gating rules, keyed on `kind`:
- `AI Ready` only on a `feature` node.
- a `panel` has `theming`, has no `tests`; a `state` node has no concerns; a `control`/`behaviour`/`ai-act`
  leaf has a `tests` concern.
- every feature has the backbone (a UI + Functionality + AI Integration; AI Integration has
  context/act/preview) — a *template conformance* check per feature root.
- a leaf under UI/Functionality is a real leaf (no empty container pretending to be one).

**(d) Snaplink ↔ `[CoversNode]` cross-check, made gating.** Today "declared-but-unlinked" is a non-gating
advisory. Add the reverse and gate it: a leaf whose `tests` snaplink names `Class.Method` where that method
carries **no** matching `[CoversNode]` for the leaf is drift. This keeps the two channels locked together (and
`scan-tests` already reflects the manifest needed to check it).

**(e) A Roslyn analyzer for the test side (author-time):**
- a test method that launches the app (derives from `UiJourneyTestBase`) should `[CoversNode]` a `ui`-kind
  node — warn otherwise (enforces "one journey, at the UI node").
- a `[CoversNode]` on a `panel`-kind node from a *unit* (non-UI) test is suspicious — panels are journey-
  covered — warn.

### Cross-checks against the code (the higher-value drift catchers)
(a)–(e) keep the tree *self*-consistent. These two instead assert the tree/tests still match the **real code**
— which is where drift actually happens (a control or tool added and the tree/tests never updated).

**(f) UI AutomationId guard — presence + coverage.**

> **Build it on the graph, not on a second scanner.** `.xaml` is parsed now (the `xml` grammar, from
> `external/tree-sitter-xml`), and every `AutomationProperties.AutomationId` in the repo is already a node —
> `code:<view>.xaml#A:<id>` — carrying its file and line. The index this guard needs therefore exists: query it
> with `nfi graph search ".xaml#A:"` rather than re-walking `Views/*.xaml` with `XDocument`. That also drops the
> "XAML-as-XML can't see controls built in code-behind" caveat below to a narrower one, since the graph *does*
> see the code-behind and its `handles`/`references` edges back to the element.

Two source scans over the feature `Views/*.xaml`
(RepoRoot + `XDocument`; no tree needed, so runs in full CI):
- every command-bound `Button`/`ToggleButton` outside a Style/Template carries an
  `AutomationProperties.AutomationId` — a UI test can only reach a control by its id;
- every `AutomationId` declared in a view is referenced by ≥1 test source — an automation hook shouldn't be
  added and then left unexercised.
It's a heuristic (XAML-as-XML can't see controls built in code-behind), so keep an allowlist. **A prototype
found ~90 command-buttons with no id and ~40 unreferenced ids across the codebase**, so introduce it as a
**burn-down**: baseline the current set in a committed file, gate only *new* violations, and delete a line as
each is fixed (the `KnownNullScope` pattern). Best landed once more features naturally comply, so the baseline
starts small.

**(g) AI-tool ↔ act-node cross-check.** Per feature, the set of names from `GetClientTools()` must equal the
titles of the `<feature>-ai-act-*` leaves — catching a tool added / renamed / removed in code with no matching
tree leaf, and stale leaves. The robust form has to *call* `GetClientTools()` (an instance), so:
- **(i) a shared assert helper each feature's AI test calls** with its already-constructed VM and the act-node
  id — tree-backed, degrades when the tree is absent (CI). Cheapest, because the AI tests already build the VM
  and several already pin the tool set with an "update the tree to match" comment; this just *enforces* it.
- **(ii) a central guard** that constructs each tool-bearing page VM generically (interface deps mocked, a temp
  file for path params) and cross-checks — no per-test wiring, but fragile for VMs with awkward ctors.
- (A pure source-scan for tool-name string literals avoids construction but is heuristic — tool names also live
  in separate `IClientTool` classes, e.g. the git/font tools.)
Recommend **(i)**.

**Recommended order:** (a) now (free), then (b) `kind`, then (c) the structure rules and (d) the cross-check
on top of it; (f) and (g) as the code-drift catchers (introduce (f) as a burn-down); (e) last, if the
author-time nudges prove worth an analyzer.

---

## 7. New-feature checklist

1. `add-node` the backbone: `UI`, `Functionality`, `<feature>-ai` (+ `-ai-context` / `-ai-act` / `-ai-preview`).
2. Read the view. `add-node` a **panel** per visual region; a **control** leaf per button/display; a **state**
   node for a state-gated group of controls.
3. `add-node` the **Functionality** behaviours (the non-UI / non-AI logic).
4. Set concerns by role (§2): panels `theming`; leaves `theming`+`tests`; state nodes none; `AI Ready` only on
   the feature root. `remove-concern` anything that auto-attached wrongly.
5. Give every command-bound control in the view an `AutomationProperties.AutomationId` — the journey can
   only reach a control by its id, and an untagged button is invisible to it (§6f).
6. Write the **one UI journey** (`[CoversNode("<ui>")]`) + **a unit test per leaf/behaviour**
   (`[CoversNode("<leaf>")]`); extract the pure seam where one is hiding (§3); `shouldnt` + note the rest.
7. Snaplink each `done` leaf → its **unit** test; `ui` → the **journey**.
8. `doctor` + `validate` + `lint --under <feature>`; build + run the feature's unit tests (and the UI
   journey on a desktop).

The twenty-eight subtrees listed at the top are the worked references for every step above.

One thing the pass keeps turning up, so look for it: a node claiming `tests=done` (or a `theming=done` over
a hard-coded colour) that nothing actually backs. The lint's `TestsDoneWithoutSnaplink` finds the first kind;
the second only shows up by reading the view. Both are worth fixing while you are in the feature — that is
the point of the pass.

Two more, from retrofitting a feature that was built without the backbone. **Ids are one flat global
namespace**, so a feature modelled in isolation tends to claim generic ones — `cine`, `measurement`,
`reports`, `ai-integration` — which read fine inside their own subtree and are unusable from outside it.
`rename` fixes them under validation, but it cannot reach a `[CoversNode("<old-id>")]` in test source, so
retag in the same commit (NXCOV002 flags what you miss). And **check for a stray duplicate root** before you
start: the DICOM pass found an empty `dicom-viewer` sibling of `dicom`, carrying the feature-root-only
`AI Ready` concern and nothing else — a placeholder from before the feature was named, invisible in every
view that starts from the real root.
