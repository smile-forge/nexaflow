# Testing

How the Nexaflow test suite is structured, how the shared sample-file dataset works, and how to
add coverage for a new viewer or fixture.

## What a feature's tests look like

Tests back the product tree: each node's `tests` concern names the test that proves it, so the tree stays an honest,
mechanically-checkable map of **what exists** and **what's tested**. How the tree itself is shaped — the UI /
Functionality / AI backbone, panels and state nodes, concerns by role, granularity — is
[product-graph.md](product-graph.md); this section is the tests that back it.

Two tiers, matching the two node roles:

1. **One UI journey per feature**, tagged `[CoversNode("<the feature's UI node>")]`. It launches the app once
   (amortising the ~20 s start) and drives the interactive controls in a single pass — the *integration* test
   at the UI level. See `TextJourneyTests` and `UiJourneyTestBase` (soft `CheckPresent`/`CheckInvoke`/`Check`
   so one broken control doesn't hide the rest). Interactive-desktop only (`TestCategory=UI`), and it lives in
   `Nexaflow.Tests.UIJourneys`.
2. **One unit test per leaf control**, tagged `[CoversNode("<leaf-id>")]`, driving the **view-model** command
   or state behind the control (NOT a UI test). One method may cover several leaves — tag each. This is where
   the real per-control assertions live; the journey just proves the wiring holds end-to-end.

**Functionality** behaviours are VM unit tests too — everything under `Functionality` should be unit-testable,
or explicitly declared `shouldnt`.

### Before declaring a leaf untestable, look for the pure seam

The commonest reason a leaf "can't be tested" is that its *rule* is buried inside a WPF control alongside the
caret, the selection and the document rebuild — not that the rule is untestable. Lifting the rule out is
usually a few lines and leaves the control thinner:

- **Markdown's formatting** — the heading / quote / code-fence text rule is `MarkdownBlockFormat` (pure
  `(block, …) → (newBlock, caret)`); the surface only finds the block the caret is in and writes the result.
- **Markdown's scroll-to-heading deep link** — the interesting part is matching a `>`-joined heading path
  against the block list (so duplicate names under different parents stay distinct), not the
  `ScrollToVerticalOffset`, so it is `MarkdownBlocks.FindHeadingBlock`.
- **Code's `.xshd` theming** — the name→role heuristic is in `SyntaxTokenMap` beside the tree-sitter
  capture→role map, not in `XshdTheming`. One role palette, both engines, both testable.
- **SysInfo's health colouring** — `StatusToBrushConverter.Convert` exposes the status→`TextSwatch.*` mapping as
  `ResourceKey`, so "every status resolves to a semantic token, and an unknown one paints as plain text rather
  than implying a verdict" is assertable without an `Application`.
- **Block-level undo** — no seam to extract, but `Undo()` is `public` (as `TextBox.Undo()` is), so the step
  *granularity* — one step per block session, not per keystroke — is assertable.
- **3D camera moves** — orbit / turn / roll / zoom / pan and the authored-view framing are `CameraMath`, pure
  `CameraPose → CameraPose`; `Model3DView`'s code-behind only reads the camera, applies, and writes it back. Every
  gesture and AI camera tool is assertable without a viewport.
- **Pan/zoom canvases with an overview minimap** — cursor-anchored zoom, centring, and the canvas↔minimap
  mapping and its inverse live in `Visuals.Common`'s `PanZoomMiniMap`, shared by `ImageView` and
  `ScratchpadView`. "Zooming keeps the point under the cursor still", "the minimap only appears when something
  is off-screen" and "clicking the minimap centres the viewport there" are asserted once, for both.
- **Resizing a rotated post-it** — the rule is that the corner you are *not* dragging stays put, which needs
  the drag projected onto the note's own axes: `PostItGeometry.Resize`, outside `PostItControl`. Unrotated
  resizing is arithmetic nobody gets wrong; the rotated case is what the tests are for.
- **Fitting content into a viewport** — the DICOM stage cannot lean on WPF's `Stretch`, because the
  measurement overlay is drawn in *screen* space (so strokes stay one width at any zoom) and the view
  therefore owns the matrix; the SVG canvas cannot either, because it re-tessellates rather than scaling a
  bitmap. `Visuals.Common`'s `ViewportFit` holds fit, actual-size and cursor-anchored zoom for both. Its tests
  are about the edges: fit letterboxes rather than crops, 1:1 lets oversized content overflow rather than being
  clamped, and a viewport layout has not measured yet yields the identity instead of an infinite scale.
- **Where a typed line goes when you press Enter** — the terminal's one genuinely consequential decision. Its
  inputs are trivial (is the cursor on a prompt, what was typed, what this shell calls a built-in), so
  `TerminalEnterRouting.Decide` takes them directly instead of reading state only a live pseudo-console sets.
  The third case is the one worth having a test for — mid-program there is no prompt, and the line belongs to
  whatever is reading stdin whatever it looks like.
- **A panel's listing order** — the terminal's Files panel deliberately lists files *before* folders, the
  opposite of Explorer, because the panel exists to drag a path onto the console. That inversion is the kind
  of thing a later "fix" quietly reverts, so it is pinned in `TerminalFileList.Enumerate`.
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

### When a node genuinely can't be unit-tested

**`tests=shouldnt` + a `note` saying why and who covers it.** Recurring cases:
- **WPF `ApplicationCommands` forwarders** (cut/copy/paste, a right-click menu) — need a *focused* control;
  no VM state to assert. Covered by the UI journey's presence check.
- **Passive displays** with no distinct VM behaviour.
- **Live-pipeline / STA-render tools** — WebView2 capture/scroll, `PngBitmapEncoder`/`RenderTargetBitmap`
  image capture, a live media/3D viewport, a live PTY/`cmd.exe`. Test the graceful *no-surface* error path if
  there is one; declare the real behaviour `shouldnt`.

Don't loop trying to test the genuinely-hard ones — mark `shouldnt` + note and move on.

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

### The feature pass

Every feature gets the same pass — model it in the tree, then back each node with the right kind of test:

1. `add-node` the backbone: `UI`, `Functionality`, `<feature>-ai` (+ `-ai-context` / `-ai-act` / `-ai-preview`).
2. Read the view. `add-node` a **panel** per visual region; a **control** leaf per button/display; a **state**
   node for a state-gated group of controls.
3. `add-node` the **Functionality** behaviours (the non-UI / non-AI logic).
4. Set concerns by role ([product-graph.md → Concerns, by role](product-graph.md#concerns-by-role)): panels
   `theming`; leaves `theming`+`tests`; state nodes none; `AI Ready` only on the feature root. `remove-concern`
   anything that auto-attached wrongly.
5. Give every command-bound control in the view an `AutomationProperties.AutomationId` — the journey can
   only reach a control by its id, and an untagged button is invisible to it (see *Automation ids* below).
6. Write the **one UI journey** (`[CoversNode("<ui>")]`) + **a unit test per leaf/behaviour**
   (`[CoversNode("<leaf>")]`); extract the pure seam where one is hiding; `shouldnt` + note the rest.
7. Snaplink each `done` leaf → its **unit** test; `ui` → the **journey**.
8. `doctor` + `validate` + `lint --under <feature>`; build + run the feature's unit tests (and the UI
   journey on a desktop).

One thing the pass keeps turning up, so look for it: a node claiming `tests=done` (or a `theming=done` over
a hard-coded colour) that nothing actually backs. The lint's `TestsDoneWithoutSnaplink` finds the first kind;
the second only shows up by reading the view. Both are worth fixing while you are in the feature — that is
the point of the pass. A feature without the backbone also tends to carry generic ids and stray duplicate
roots — see [product-graph.md → The shape of a feature subtree](product-graph.md#the-shape-of-a-feature-subtree).

The features below all follow the model and lint clean — read whichever is closest in shape to the feature you're
working on:

| Reference | Read it for |
|-----------|-------------|
| **Text Viewer** | the canonical shape: many toolbar controls, a state-gated group (`Edit_mode`), pop-over panels |
| **Code** | a feature whose UI lives in a *shared* control (`Nexaflow.Visuals.Text.Editor`) — the panels/leaves are modelled on the Code tab even though the XAML is elsewhere |
| **Tabular** | the widest UI: five panels, a state node for the header context menu, and a Functionality subtree (detection/parsing) deeper than the UI one |
| **Markdown** | the smallest UI over the largest shared control — and the worked example of *extracting a pure seam* so a leaf becomes unit-testable |
| **Processes** | a feature spanning **two tabs** (the list and the per-process details page) under one UI node, plus a Functionality subtree for the sampling/reconciliation/tree-building behind the grid |
| **SysInfo** | a feature spanning **three tabs** (Dashboard / Services / Environment Variables) — one panel per page — over a system-probe layer |
| **Installed Apps** | the shape of a *destructive* row menu: the safety gate is the leaf's test, and the journey never opens the menu at all |
| **Win Registry** | a feature where every write routes through an in-tab overlay — the overlays are their own panel, and the leaves are tested at "the right prompt opened, seeded correctly, and the guard fired" |
| **Log Viewer** | a *live* surface: the file watcher, pause and follow are three separate leaves because their test states differ, and the status bar is a panel of one-line readouts |
| **Images** | four mutually-exclusive content surfaces, each its own panel under one UI node, with the shared floating tools as a fifth — and the collage's pan/zoom maths pulled out as a pure seam |
| **Audio** | the shape of a feature over a *device*: the readouts either side of playback are unit-tested, the device-bound half is `shouldnt` with a note naming what covers the rest |
| **3D Model** | the same again for a live viewport — the camera maths is `CameraMath`, outside the code-behind, so every gesture and AI camera tool is assertable without a rendered scene |
| **Video** | what to do when the whole feature sits on a native engine: the window *before* the engine exists is the tested one, because that is where the tab is actually exposed |
| **Scratchpad** | a canvas rather than a document, whose pan/zoom seam is shared with Images in `Visuals.Common` |
| **Projects** | two tabs plus two file-explorer viewlets under one UI node, over an operations layer that carries most of the tests |
| **Notebook** | the smallest complete example — two panels, four behaviours |
| **Win Search** | a feature whose core (the index query) genuinely cannot run headlessly, so everything either side of it is what carries the tests |
| **AI Chat** | the hardest `shouldnt` calls: approvals and interjections only exist inside a running agent turn, so the note has to say what covers them instead |
| **DICOM** | the retrofit reference — read it for the feature-prefixed ids and single root a feature brought onto the backbone has to establish |
| **Console** | a feature whose logic almost all lives in shared libraries (`Visuals.Terminal`, `IO.Terminal`), so its leaves snaplink outward, and its one irreducible decision — where a typed line goes — sits outside the live PTY so it is testable at all |
| **Win File System** | the largest subtree, and the reference for an action strip: most buttons end in a shell call or the clipboard, so the tests sit on the *gate* and the `shouldnt` notes have to say where each rule is actually asserted |
| **Hex** | one AI test per tool, so each of eight act leaves names its own method rather than one omnibus test |
| **Json** | where the guard is the whole story: the document is held windowed, so Save and Format have to refuse while any part is unread, and that refusal is what the leaf's test asserts |
| **SVG** | the smallest complete example — one canvas, three toolbar controls — sharing the fit/zoom seam (`ViewportFit`) with DICOM |
| **Email** | a UI subtree built by reading the view, over a parse / VFS / AI layer |
| **Virtual Disk** | the reference for a viewer that deliberately opens nothing — content is reached by browsing the image, so a file inside has one open path rather than two |
| **Compressed** | the widest Functionality subtree — one backend assembly per container family — and an action bar where every destructive action goes through a choice or password overlay, so the leaf tests are all "cancelling leaves the archive byte-for-byte as it was" |
| **Product** | the multi-view reference: three views (five planned) as panels under one UI node, one shared Functionality subtree, and *one* AI node — because the views are views of one tree. Also where `get_context` splits per view while the tool set stays shared |

---

## Projects

All under `src/Nexaflow.Tests/`:

| Project | Target | Covers | References |
|---------|--------|--------|------------|
| `Nexaflow.Tests.UIJourneys` | `net10.0-windows10.0.19041.0`, MSTest exe | **Every test that launches the app.** `Core\` for the shell, `Features\<Feature>\` for the rest | `Nexaflow.Tests.Fixtures` **and nothing else** — a journey knows the app as a running process, never as an assembly |
| `Nexaflow.Tests.Core` | `net10.0-windows10.0.19041.0`, MSTest exe | Core shell chrome and services **only** — config/workspaces, the feature catalog and DI, shell services, the agent loop, the `?` search route, theming | `Nexaflow.Core`, `Nexaflow.Tests.Fixtures` |
| `Nexaflow.Tests.Visuals` | `net10.0-windows`, MSTest exe | `Nexaflow.Visuals.*` — markdown/LaTeX/music rendering, the inline editor, the shared controls and layout, the WebView surface, and the editor-side highlighting | `Visuals.Common/.Text/.Web`, `Syntax`, `Search`, `IO.Common`, `Features.Common`, `Nexaflow.Tests.Fixtures` — **never Core** |
| `Nexaflow.Tests.Components` | `net10.0-windows`, MSTest exe | The shared component leaves that are neither IO nor UI: `Nexaflow.Syntax` (tree-sitter, structural edit), `Nexaflow.Search` (query syntax/terms), `Elevation.Contracts` | those + `IO.Common` + `Nexaflow.Tests.Fixtures` — **nothing else**, and no WPF |
| `Nexaflow.Tests.Features` | `net10.0-windows10.0.19041.0`, MSTest exe | The shell-adjacent features: AI chat, console, network discovery, OneDrive, Product/Projects, scratchpad, This PC, web — plus the folder viewlets (Git, Dotnet) and the generic search plumbing | those feature projects + `.Common` + `Nexaflow.Tests.Fixtures` — **never Core** |
| `Nexaflow.Tests.Features.Viewers` | same | Every viewer/editor/player — Audio…Video, plus the sample-file corpus. A feature registering no page is not a viewer: the Git and Dotnet viewlets live in `.Features` | the viewer feature projects + `.Common` + `Nexaflow.Tests.Fixtures` |
| `Nexaflow.Tests.Features.WindowsOS` | same | The features that inspect and drive Windows: file system, registry, search index, installed apps, processes, system info | those feature projects + `.Common` + `Nexaflow.Tests.Fixtures` |
| `Nexaflow.Tests.Features.Architecture` | same | The whole-repo guards: reference/dispatcher rules, add-a-feature touch points, solution membership, XAML keys, `[CoversNode]` declarations | the suites above **and** `Tests.Initiatives` (for their **output**, not their API) |
| `Nexaflow.Tests.Features.Common` | `net10.0-windows10.0.19041.0` class library | **Shared support.** Not a test project — `AsyncPump`, `RepoRoot`, `DicomTestFiles`, the `ISearchable` and viewer-`IFileAction` conformance contracts | FlaUI + `Features.Common` + `Nexaflow.Search` + `Nexaflow.Tests.Fixtures` — **no feature** |
| `Nexaflow.Tests.IO` | `net10.0-windows`, MSTest exe | `Nexaflow.IO.*` — the WPF-free IO leaves: `IO.Common`, `IO.Protocol` (DynamicProtocol + the ten-protocol corpus), `IO.Network` | those three + `Nexaflow.Tests.Fixtures` — **nothing else** |
| `Nexaflow.Tests.Initiatives` | `net10.0`, MSTest exe | `Nexaflow.Services.Initiatives` + its CLI — the product tree, the knowledge graph, `SnaplinkValidator`, `ProductTreeOps`, the verb parser | `Services.Initiatives`, `Services.Initiatives.Cli`, `Nexaflow.Tests.Fixtures` — **nothing else** |
| `Nexaflow.Tests.Maths` | `net10.0`, MSTest exe | `Nexaflow.Maths` — the LaTeX parse tree, its printer, the command table, grids. Runs the 238k-formula corpus in seconds because nothing here is drawn | `Nexaflow.Maths`, `Nexaflow.Tests.Fixtures`, and AngouriMath **as an oracle** (it writes LaTeX and knows what structure it wrote) |
| `Nexaflow.Tests.Providers` | MSTest exe | Provider clients — network-free provider surface, config round-trips, `PromptComposer`, `LlmAttachment`, Aria wire protocol | the provider projects |
| `Nexaflow.Tests.Fixtures` | `net10.0` class library | **Generates the sample dataset**, plus `UiFixtures` (the material the journeys open) and `ViewerMap`. Not a test project — no MSTest, no `[TestClass]` | nothing (deliberately dependency-free) |

**TeX's own layout rules are tested in `Nexaflow.Tests.Visuals`, under `Markdown/Latex/Typesetting/`** — where an
integral's limits go, which face a letter comes from, how far a sized delimiter grows — measured on what the builder
sets, and run in CI with the rest of the suite.

No `Nexaflow.Tests.Features*` suite references Core (they mirror the architectural rule that features
don't depend on Core). The sample-data generator therefore lives in its own dependency-free library,
`Nexaflow.Tests.Fixtures`, so the test projects can share it without one test exe dragging in another
(and without pulling Core's x64 RID into the feature tests).

### Why the feature tests are four projects

The feature suites are split by **subject**, so a viewer change rebuilds only `Viewers` — one project referencing
~50 feature assemblies would rebuild everything for any test edit. Two consequences worth knowing:

- **`Search/` follows the split.** It is one `<Feature>SearchableTests.cs` per feature, each in its feature's suite,
  because a single search folder would reference nearly the entire graph — exactly the cost the split exists to
  remove. Only the feature-agnostic ones (query syntax, term parsing, the conformance guard) sit together.
- **`Architecture` is the heaviest project on purpose.** Its guards reflect over every
  `Nexaflow.Features.*.dll` *and* every suite DLL matching `FeatureTestSuites.Patterns`, so it references
  the suites to bring both sets into its output directory rather than guessing at sibling `bin` paths that
  shift with configuration and target framework. It is also the project nobody edits while working on a
  feature. **A new suite must be added to `Patterns`** — that is the one place the discovery is spelled out,
  and a suite missing from it silently drops out of the `[CoversNode]` guard.

Namespaces don't follow the project: a test in `Viewers` is `Nexaflow.Tests.Features.Audio`. That keeps the
folder→feature convention `CoverageGuardTests` enforces readable across all four assemblies.

**Which project a test belongs in is decided by its subject, not its imports.** A test whose subject is an
IO library goes in `Tests.IO`; one that merely reaches through an IO library on its way to a feature —
`Text` opening a file via `EncodingDetector`, `Compressed` browsing through the VFS — stays with the
feature. The rule is worth stating because the second kind is far more common, and placing those in `Tests.IO`
would drag the whole feature graph into a project whose value is that it has none of it.

**The same rule places `Tests.Initiatives`.** Its subject is a WPF-free backend library, like `Tests.IO`'s, so the
suite is plain `net10.0` — no shell, no desktop — which also keeps the `initiatives`
[mutation target](#mutation-testing-strykernet) cheap and safe to run. The ProductManager *feature* — the
view-models, the AI client tools, the graph viewer — genuinely needs WPF and is tested in `Tests.Features`. A test
belongs in `Tests.Initiatives` when its subject is `Nexaflow.Services.Initiatives(.Cli)`; one that reaches it through
the feature stays with the feature.

## Running

```powershell
# Build + run a project's tests (MSTest runner exe):
dotnet build src/Nexaflow.Tests/Nexaflow.Tests.Features/Nexaflow.Tests.Features.csproj
$exe = "src/Nexaflow.Tests/Nexaflow.Tests.Features/bin/x64/Debug/net10.0-windows10.0.19041.0/Nexaflow.Tests.Features.exe"

& $exe --filter "TestCategory!=Desktop&TestCategory!=Interactive"   # what CI runs
& $exe --filter "TestCategory=UI"                        # UI tests — needs an interactive desktop
& $exe --filter "TestCategory=Interactive"               # calls real OS services — dev machines only
& $exe --filter "FullyQualifiedName~SampleFileDetection" # one class
```

### Fast inner loop

Features don't depend on Core, so feature work never needs the solution build: build only the feature csproj you
touched, then the one suite that owns it, and run it with `--filter "FullyQualifiedName~<Class>"` — the suites are split
by subject, so editing a viewer test rebuilds only its own suite. `nfi test <node-id>` chooses and runs the tests for you
(CLAUDE.md → *Prove*). Output is under `bin/x64/<Config>/` — the solution is pinned x64, so a
`bin/<Config>/` without `x64` is not this build's output; delete it.

After any change touching `Nexaflow.Core`, run its unit tests before committing:

```powershell
dotnet build src/Nexaflow.Tests/Nexaflow.Tests.Core/Nexaflow.Tests.Core.csproj
src/Nexaflow.Tests/Nexaflow.Tests.Core/bin/x64/Debug/net10.0-windows10.0.19041.0/Nexaflow.Tests.Core.exe --filter "FullyQualifiedName~Unit"
```

UI journeys are never part of the inner loop — they take over the mouse and keyboard. Run them last, on a machine you
are not using, and after the other suites (which build the fixtures they open):

```powershell
src/Nexaflow.Tests/Nexaflow.Tests.UIJourneys/bin/x64/Debug/net10.0-windows10.0.19041.0/Nexaflow.Tests.UIJourneys.exe
```

### Categories

- **Unit / non-UI** — fast, headless, no desktop. The default for CI.
- **`TestCategory("Desktop")`** *(`Tests.Visuals`, `Tests.Core`)* — **shows a real window and takes
  focus.** Never run in CI: a runner can host a window, but "passes unless something else took focus" is
  not a gate. Focus is a single machine-wide resource, so these must also carry **`[DoNotParallelize]`**:
  run two at once and they take it from each other mid-assertion, which surfaces as a *different* test
  failing on each run rather than as anything resembling a real bug. `DesktopTestCategoryGuardTests`
  enforces this — a class whose source shows a window must declare the category and opt out of
  parallelism. It reads **both** suites' sources, because the resource is machine-wide: a guard scoped to
  the assembly it happens to sit in would pass over the other one and read green while testing nothing.
- **`TestCategory("UI")`** — means two different things by suite, so CI excludes `Nexaflow.Tests.UIJourneys` as a
  whole assembly rather than filtering on the category. In `Tests.Visuals` and `Tests.Core` it means only *renders
  WPF off-screen* — an STA thread, no window, safe to parallelise and safe on a runner, so those run in CI. In
  **`Nexaflow.Tests.UIJourneys`** it means driving the real `Nexaflow.exe` via FlaUI: each test launches a fresh app
  against an **isolated config root** (`NEXAFLOW_CONFIG_DIR` → a throwaway temp dir), so it neither depends on nor
  pollutes the developer's real `%APPDATA%` config. See `UITestBase`.

  The journeys live in one assembly so a run has one launcher and one prompt: separate assemblies would start
  separate test hosts, each asking for the machine and launching its own app, and the instances would steal one
  another's clicks. `UiTestGate` adds a machine-wide semaphore so even a concurrent host cannot put a second app on
  screen.

  **They reference nothing but the built app.** A journey that constructed its own input would be linking
  the assembly it is meant to drive through the UI — preparing and asserting with the same code. So
  anything that cannot be clicked into being (a git repository, a disk image, a seeded workspace config)
  is built into `test-samples/ui/` by the suite that owns that format, as part of that suite's normal run,
  and looked up through `RequiredFixture`. A missing fixture is **inconclusive**, not a failure: it means
  the corpus has not been built on this machine, which says nothing about whether the app works.
  **Run the other suites first.**

  > **They ask before taking the machine.** The first launch in a run puts up a confirmation
  > (`UiTakeoverPrompt`), because these drive the real mouse and keyboard: started while you are working
  > they interrupt you *and* flake themselves, since a click meant for the app lands wherever focus
  > actually went. Answer no and every UI test in that run reports inconclusive rather than re-asking.
  > It is asked once per process at the first launch — not at assembly load — so a headless run never
  > sees it, and it is suppressed entirely on CI (`CI`, `TF_BUILD`, `GITHUB_ACTIONS`, `JENKINS_URL`,
  > `TEAMCITY_VERSION`) or with **`NEXAFLOW_UITESTS_NOPROMPT=1`** for a deliberately unattended local
  > run. An unanswered prompt proceeds after two minutes, so nothing can stall on an absent human.
- **`TestCategory("Interactive")`** — calls a real Windows service instead of a fake, to prove our use
  of an external API is actually correct. Read-only and safe to run on any developer machine, but the
  results depend on that machine's state, so CI never runs them (the workflow filters out both this
  and `Desktop`). The worked example is `AqsTranslatorInteractiveTests`, which exercises the Windows Search
  COM interop: the interop declarations are a hand-transcribed vtable, and only a real call can prove
  the layout is right — a wrong slot is an access violation, not a failed assertion. Such a test
  asserts on the *contract* (a clause came back naming the property asked for), never on what happens
  to be indexed, and calls `Assert.Inconclusive` when the service isn't running.

### Reference corpora and snapshots (opt-in, never in CI)

Three tests compare rendered output against material that is deliberately **not** in the repository, and
are `Assert.Inconclusive` until an environment variable points them at it. They show up as skips in every
normal run, which is intended.

| Variable | Test | What it compares against |
|---|---|---|
| `NEXAFLOW_BARCODE_IMAGES` | `BarcodeReferenceImageTests` | Barcodes drawn by an unrelated generator — the only check that can catch a wrong pattern table. |
| `NEXAFLOW_LATEX_PICTURES` | `LatexPictureSweepTests` | A LaTeX corpus. |
| `NEXAFLOW_DIAGRAM_SNAPSHOTS` | `DiagramSnapshotTests` | *Our own* previous render of every diagram in the sample corpus. |

The last is a **before/after tool, not an absolute one**, and that is why its images are not committed:
text rasterisation depends on the machine's fonts and DPI, so a shared set would fail for reasons unrelated
to the change under test. Use it around a change:

```powershell
# on the base commit
$env:NEXAFLOW_DIAGRAM_SNAPSHOTS = "C:\snapshots"
$env:NEXAFLOW_DIAGRAM_SNAPSHOTS_WRITE = "1"
& $exe --filter "FullyQualifiedName~DiagramSnapshotTests"    # captures the "before"

# after the change
$env:NEXAFLOW_DIAGRAM_SNAPSHOTS_WRITE = ""
& $exe --filter "FullyQualifiedName~DiagramSnapshotTests"    # names every diagram that moved
```

A failure writes the new render beside the old one as `<name>.actual.png` so the two can be compared. Snapshot names are **positional** (`<sample>-<fence index>-<theme>.png`), so inserting a fence into a sample renumbers every fence after it and they all report as differences — re-capture rather than reading anything into that. **A
reported difference is not automatically a regression** — a deliberate improvement moves pixels too. It is a
prompt to look and decide, which is the step that is easy to skip without something insisting on it.

Reach for it whenever a change touches shared rendering rather than one diagram type. The unit tests assert
what a renderer was *asked* to draw; they cannot see a label drawn behind a box, a legend appended below the
visible area, or a group dropped before it reached the canvas — a before/after render can.

### Measuring markdown layout (opt-in, by hand)

`MarkdownLayoutBench` times laying a document out, step by step, over the sample corpus and
`docs/MarkdownSupport.md`: what **opening** a document costs, what **one keystroke** in the middle of it costs, and
each step on its own — Markdig, every stage the document is read by, the builder (nested languages apart from
markdown's own), painting (a fresh tree, the same tree again, and what a keystroke laid where the page was painted
before it), and memory (what opening, a keystroke and each paint allocate, and what a document laid and painted
holds on to) — with corpus totals for every nested language, every stage of each language's own
pipeline, and every kind of block. It writes one JSON file per run and asserts nothing.

```powershell
tools/bench/Run-MarkdownBench.ps1 -Label "what changed"     # Release; runs kept in %LOCALAPPDATA%\Nexaflow\markdown-bench
```

Compare runs taken on one machine in one configuration; the absolute numbers mean little on their own. Release is the
default because a Debug build checks every pipeline stage's output against its input, which a user never pays for.

### Coverage declaration (`[CoversNode]` / `[NoCoverage]`)

Every concrete `[TestClass]` must declare the product-tree node it backs with `[CoversNode("node-id")]`
(from `Nexaflow.Tests.Fixtures`, repeatable, also valid on a method), or opt out with `[NoCoverage("reason")]`
for tests that map to no single node (the `Architecture/` guards, `Fixtures/` sample-corpus tests). Abstract
test bases need no attribute. This is enforced at author time by the `Nexaflow.Analyzers.Coverage` analyzer
(NXCOV001 = missing declaration, NXCOV002 = stale id) and in CI by `CoverageDeclarationGuardTests` (one per
test assembly). `dotnet run --project src/Nexaflow.Services.Initiatives.Cli -- scan-tests . --suggest-attributes`
prints the starter set derived from the tree's existing `tests` snaplinks.

Put a `[CoversNode]` at **class level** only when the whole class covers that node (usually a container with
children); a specific behaviour (a leaf node) goes on the individual `[TestMethod]`(s) — the manifest then carries
precise class+method links. NXCOV003 flags a class-level leaf that over-claims because the class covers other nodes
too. Which node a capability's test declares — the Shared one, not the use case — is in
[product-graph.md → Features and Common / Shared](product-graph.md#features-and-common--shared-are-different-kinds-of-list).

The tree stays authoritative; the attributes are a cross-check with a one-click reconcile. `scan-tests` reflects the
built test DLLs (metadata-only, via `MetadataLoadContext` + the portable PDB for the source path) into the derived
**coverage manifest** (`.product/test-coverage.json`). The Integrity page reconciles that manifest against the tree
and shows each *declared-but-unlinked* test as a **non-gating advisory** with an **Add link** button (writes the
`tests`-concern `code` snaplink for you) — a separate channel from the gating snaplink issues, so advisories never
fail the installer. Id validity is checked against the live `.product/tree.json` (gitignored → absent in CI, where
the guard degrades to presence-only).

### One shape for a builder (`ContentBuilderRulesTests`)

A `ContentBuilder` is one step of the chain in [markdown-ast.md](markdown-ast.md) — reading in, layout out — and owns
nothing. `ContentBuilderRulesTests` (in `Tests.Visuals`) reflects over every class deriving from it and requires one
constructor, taking `(ContentReading, EditState, StyleFormat, bool)`; nothing told to it afterwards; and nothing
named that `ContentBuilder` does not declare. Anything else a builder was going to be given is a fact about the
content or about this showing of it, and belongs somewhere every builder can be given it the same way — the
style, the tree, or a pipeline stage that puts it there. How wide it may be goes to `Lay(room)`, because that is
the one thing that changes without the content or the showing of it changing.

It is a **ratchet**: builders that predate the rule are listed in
`Editing/content-builders-not-yet-one-shape.txt`, and the two tests pull opposite ways — a new builder out of shape
fails until it is fixed or listed, and a listed builder fails once it *is* in shape. So the list can only shrink.

### Automation ids (`NXUI001` / `AutomationIdJourneyCoverageTests`)

A journey can only click what it can find, and on this shell that means an `AutomationProperties.AutomationId`
— the visible name is copy that changes, and most of the chrome is icon-only buttons that have no name at all.
Two gates, at opposite ends of the same rule:

- **`NXUI001`**, from `Nexaflow.Analyzers.Ui`, warns on a `<Button>` (or `ToggleButton`, `RadioButton`, any
  control named `…Button`) with no id. Its subject is XAML, which Roslyn never compiles, so
  `Directory.Build.targets` hands every `Page`/`ApplicationDefinition` item to the compiler as an
  `AdditionalFile` for it — automatic for any WPF C# project, so a new feature inherits the rule by existing.
  A button inside a `ControlTemplate` is exempt: it is another control's chrome, and UIA reports the templated
  control instead. It is a **warning** on purpose — there is a real backlog, and an error would stop the build
  rather than shrink it.
- **`AutomationIdJourneyCoverageTests`** (in `Tests.Features.Architecture`) requires every id declared in a
  view to be named somewhere in `Nexaflow.Tests.UIJourneys`. Matching is loose — the id appearing anywhere in
  the journey sources counts — because a journey may hold it in a constant or pass it to a helper, and the
  failure worth catching is an id no journey mentions at all. It is a **ratchet**: the ids no journey names
  yet are listed in `Architecture/automation-ids-without-a-journey.txt`, and the two tests pull opposite ways — a
  new unreferenced id fails until it is covered or listed, and a listed id fails once it *is* covered or its
  view stops declaring it. The list can only shrink, and it cannot rot into a permanent allowlist.

An id whose value is a markup extension (`{Binding AutomationId}`) is skipped by both: the real id is computed
at run time, so there is no literal for a journey to name.

## Conformance suites — one contract, every implementor

Two contracts are written once in `Nexaflow.Tests.Features.Common` and inherited per implementor, rather
than restated by hand in each feature's folder. Derive a concrete `[TestClass]`, supply the two or three
things the base cannot know, and the inherited `[TestMethod]`s run against it — so a new implementor cannot
ship without being held to the same rules, and a rule added to the base applies everywhere at once.

| Contract | Base | Implementors |
|---|---|---|
| `ISearchable` — a page may decline regex, but never *appear* to support it | `Search/SearchableConformance.cs` (two tiers: with and without seeded content) | every searchable page |
| viewer `IFileAction` — `PerformAction(p)` and `PerformAction([p])` are the same user intent | `FileActions/FileActionConformance.cs` | every action with `OpensViewer => true` |

Each rule is one a hand-written test would assert in some features and not others — and the features that skip it
are the ones that drift. The file-action contract holds the two overloads to one answer: filtering by file type
applies to a one-item selection exactly as to a single file, an empty selection returns `false`, and an action
declaring `SupportsMultipleFiles => false` never opens a tab per file.

What the base deliberately does **not** test is the metadata half — that `StaticExperienceId` is declared
and the experience is mapped in the bundled file map. Reflection over every feature assembly covers that in
`FeatureTouchPointTests`; what reflection cannot do is *invoke* the action, which is the half this base covers.

Adding an action to the contract costs six lines:

```csharp
[TestClass]
[CoversNode("svg-open-actions")]
public class ShowSvgActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) => new ShowSvgAction(shell);
    protected override string ExpectedPageKind => SvgTabRegistration.StaticPageKind;
    protected override string AcceptableFile   => @"C:\art\logo.svg";
}
```

Nothing touches disk: every viewer action is a pass-through to `IShellServices.OpenTab`, and the ones that
filter do it on the extension alone, so the paths are probes rather than fixtures.

## Coverage by feature

Feature assemblies (`Nexaflow.Features.*`) are tested across the `Nexaflow.Tests.Features*` suites,
**one folder per feature** (`Audio/`, `Compressed/`, `Projects/`, … — the Code feature's tests live under
`CodeIntel/`). Which suite holds the folder follows the feature's subject; shared support lives in
`Nexaflow.Tests.Features.Common`.
**Unit** = the headless `TestCategory!=UI` tests; **UI** = `[TestCategory("UI")]` (drives the real
shell via FlaUI). The file-viewer features additionally get a per-file open-smoke UI case from the
shared `SampleFileViewerTests` (each fixture → its viewer; see
[Per-file viewer UI tests](#per-file-viewer-ui-tests)).

**Per-component coverage is tracked in the product tree, not in this file.** A hand-maintained
coverage table here goes stale silently; the live record is the `tests` concern carried by every
component node:

- **In-app** — open the Product tab on the repo root: each node's `tests` concern shows `done`
  (real coverage backs it) or `should` (not yet assessed/covered).
- **From a Claude session** — read `.product/tree.json` via the product-folder skill and inspect
  the node's `{ "tag": "tests", "status": … }` link.
- **Durable per-release snapshot** — the concern tally table in
  [product/PRODUCT.md](product/PRODUCT.md).

When you add (or remove) tests for a component, update that node's `tests` concern in the product
tree — that is the maintenance step.

`Features.Common` (contracts) has no test folder of its own; its client-tool wire-protocol parser
(`ClientBlockParser`) and the agent loop are tested in `Tests.Core` (`Unit/ClientTools/`).

The windowed-reader view-models load into a thread-affine AvalonEdit `TextDocument` across `await`
points, so `LogViewModel`/`TextViewModel` tests run under `Infrastructure/AsyncPump.cs` (a
single-threaded synchronization context). `LogViewModel`'s background head-reassembly needs a live UI
`Dispatcher`, so that one path is left to the UI smoke rather than a unit test.

### Core shell (`Nexaflow.Tests.Core`)

Covers `Nexaflow.Core` and nothing else — background activity, config manager + migration, conversation
store, message center, panes and quick-open, shell services, workspace manager + config scoping, the
feature catalog / subfeature catalog / feature DI, the elevated bridge launcher, the client-tool parser
and agent loop, the `?` search route (`SearchQueryHandler`, `SearchClientTools`) and theme freezing.

`Tests.Core` is the one suite that references Core, and Core hard-references every feature and provider
assembly (they must land in its output for `FeatureCatalog` to scan). So building it builds the whole
solution — which is why everything that does **not** need Core lives in the suites below.

### Visuals (`Nexaflow.Tests.Visuals`)

Covers the `Nexaflow.Visuals.*` libraries, with no reference to Core: markdown parsing and rendering
(`MarkdownBuilder`, `MarkdownSurface`, extensions, pipeline, every language's builder and the layered layout), the
LaTeX formula tree/layout/caret model, the music engraver, writing in a document, `Visuals.Text`'s
editor surface and highlighting, the shared controls and pan/zoom layout, and the WebView2 surface.

Its two WPF categories are split by what they *need*, not what they touch — see
[Categories](#categories) above. `DesktopTestCategoryGuardTests` lives here and
scans both this suite and `Tests.Core`, because focus is machine-wide and the rule is not per-assembly.

### Component leaves (`Nexaflow.Tests.Components`)

Covers the shared leaves that are neither IO nor UI, and — like `Tests.IO` — references nothing above
them:

- **`Syntax/`** — `Nexaflow.Syntax`: the tree-sitter probe, the code highlighter, language injection,
  `SourceText`, `StructuralEdit`, XAML value highlighting.
- **`Search/`** — `Nexaflow.Search`: the `?` bar's query syntax and term parsing (globs vs regex).
- **`Elevation/`** — `Elevation.Contracts` DTO round-trips.

### IO leaves (`Nexaflow.Tests.IO`)

Covers the libraries under `src/Nexaflow.IO.*`, and references nothing above them — no Core, no Features,
no Visuals. That is the point of the split rather than a side effect: these tests need no desktop session,
no shell and no config root (no IO library reaches one), so they are the fastest suite to run and the one
least able to fail for a reason that is not about its subject.

- **`Common/`** — `Base64Codec`, `DirectoryMover`, `FileSplitter`, `Glob`, `Hashing`, `OverlayTextFile`,
  `TextLineIndex`, `TextTransforms`.
- **`Protocol/`** — the DynamicProtocol engine, plus the ten-protocol corpus (`Protocol/Corpus/*.json`)
  and the protocol graphs authored against it (`Protocol/Definitions/*.json`), both copied to the output
  by the csproj. See [dynamic-protocol.md](dynamic-protocol.md) → *Reading the tests*.
- **`Network/`** — the device graph's identity lattice and the send guard.

### Providers (`Nexaflow.Tests.Providers`)

Covers the provider clients (Claude, OpenAI, Gemini, Ollama, Aria) without touching the network:

- **Provider surface** — `Name` identity, `SupportsImages`, model listing (Claude's static list;
  OpenAI/Ollama return empty and never throw when the backend is unreachable), `GetModelInfo`
  (bound vs unbound / default-null).
- **Configs** (`ProviderConfigTests`) — defaults + JSON round-trip for all five provider configs,
  incl. Ollama's `KeepAliveValue` derivation rules.
- **Shared prompt plumbing** — `PromptComposer` (system-prompt split, attachment partitioning,
  file-list append) and `LlmAttachment` (MIME/extension image detection, `ResolvedMimeType`
  precedence, in-memory-vs-disk `ReadBytes`).
- **Aria wire protocol** — `PipeFrame` serialization round-trips and `AriaNamedPipeClient`
  lifecycle guards (send before connect / after dispose throws, idempotent dispose).

**Remaining gap:** the live `CompleteAsync` path — the neutral `LlmMessage` → SDK request mapping
(roles, attachments, vision blocks) — has no coverage; it is welded to the vendor SDK call with no
seam to intercept, so mapping regressions ship untested.

## Sample files (`test-samples/`)

Many tests need real files to read. Rather than hand-curated, machine-local folders, the suite
generates a cached dataset on demand.

`TestSampleData` (in `Nexaflow.Tests.Fixtures`) materialises a **git-ignored** dataset at
`<repoRoot>/test-samples/`. It is a *cache, not source*: excluded via `.gitignore`, generated once
from the in-code catalog, and safe to delete (the next test run regenerates anything missing).
Generation is **idempotent** — a file is only (re)written when absent or its content has drifted
from the catalog.

```
test-samples/
  markdown/   one mermaid-* document per supported Mermaid diagram type, plus extensions.md
              (YAML front matter, emphasis extras, abbreviations, alert blocks)
  tabular/    csv/tsv variations: separators (, ; tab, ", "), quoting, headers, single column,
              mixed column types, and one long file for the windowed streaming readers
  text/       short + long plain text; UTF-8 (BOM/no-BOM), UTF-16 LE/BE, UTF-32 LE; LF and CRLF
  code/       source files across the highlighted languages (embedded-language hosts included)
  notebook/   .ipynb documents (markdown + code cells, varied outputs)
  json/       object, array, deeply nested, and a 1,000-item array for seek-by-item windowing
  logs/       short + long timestamped logs (tail-first streaming), plus a Serilog-compact JSON-lines log
  binary/     random / zeros / mixed / PNG-header blobs for the hex viewer
  images/     small raster images for the image viewer
  archive/    zip/tar/7z/… containers (incl. nested) for the Compressed handlers
  model3d/    STL / OBJ / PLY / glTF meshes for the 3D viewer
  audio/      short WAV clips for the audio player
  video/      a minimal .mp4 for the video viewer
```

### Catalog model

```
SampleFile        — one file: Name + bytes + IsText flag.
                    SampleFile.Text(name, content)  → UTF-8, LF-normalised, compared as text
                                                       (CRLF/editor drift never forces a rewrite)
                    SampleFile.Raw(name, bytes)     → byte-exact: BOMs, line endings, binary blobs
ISampleSet        — a family of files owning one sub-directory (SubDirectory + Files).
TestSampleData    — resolves <repoRoot>/test-samples, materialises every registered set on first
                    access, and exposes:
                      Root                       the dataset directory
                      Path(subDir[, name…])      absolute path under the dataset (generates first)
                      Files(subDir)              every path owned by that set
```

Each set is a small class (`MarkdownSamples`, `TabularSamples`, `TextSamples`, `CodeSamples`,
`NotebookSamples`, `JsonSamples`, `LogSamples`, `BinarySamples`, `ImageSamples`, `ArchiveSamples`,
`Model3DSamples`, `AudioSamples`, `VideoSamples`) registered in `TestSampleData.Sets`. Long/large fixtures are built
programmatically **without `Random`** (fixed-seed LCG or deterministic arithmetic) so regeneration is
reproducible and churn-free.

### Adding a sample family

1. Implement `ISampleSet` — pick a `SubDirectory`, return `SampleFile.Text`/`.Raw` entries.
2. Register it in `TestSampleData.Sets`.
3. Consume it from a test via `TestSampleData.Files("yourdir")` / `TestSampleData.Path("yourdir", "f")`.

## Per-file viewer UI tests

Every sample file is opened through the real shell and asserted to load in the expected in-app viewer.
`SampleFileViewerTests` drives that off `ViewerMap.BySet`, so adding a fixture automatically adds its
coverage — no per-file wiring.

It runs as **three** tests, not one per file and not one for the lot: `MarkdownSamples_…`,
`ImageSamples_…` and `OtherSamples_…`. One app launch per test amortises the whole corpus (a test per
file would pay a launch per file), while the two heaviest families — markdown at a third
of the corpus, images at a fifth — are pulled out so the suite has no single multi-minute block, a
failure names which group broke, and a crash in one group leaves the others' results intact.

A family with its own test must also be listed in `SampleFileViewerTests.OwnTest`, which is what
excludes it from the tail; `Only(...)` refuses to sweep a family missing from that list, so the two
halves can't drift apart and leave a family swept twice.

Each file is opened by navigating the file browser to the sample's folder and taking the default-open
route, then waiting for the viewer's root `AutomationProperties.AutomationId`. Every file's outcome is
accumulated and reported together, so one bad fixture doesn't hide the rest:

### Per-family timings

Each family's elapsed time is written to `TestContext` and appended to the gitignored
`artifacts/journey-timings.csv` (`JourneyTimings`), one row per family per run:

```
timestampUtc,appVersion,scope,unit,items,elapsedMs,msPerItem,failures
```

`appVersion` is the `FileVersion` of the `Nexaflow.exe` that was driven, so rows are comparable across
releases — sort by `unit` then `appVersion` to see whether a viewer got slower. Compare on `msPerItem`,
not `elapsedMs`: a family gains fixtures over time, so its total is not comparable with its own past.

**App launch is deliberately outside every row.** The clock starts inside the test body, after the app
is up and the file browser has painted, and dataset generation is forced before the first family — so a
cold disk or a slow first paint lands on nobody's number. What a row measures is navigate-plus-open-plus-close
for that family. Writing the log is best-effort and can never fail a test.

| Sub-dir | Extensions | Opens in viewer (AutomationId) |
|---------|-----------|--------------------------------|
| `markdown` | `.md` | `MarkdownView` |
| `tabular`  | `.csv` `.tsv` | `TabularView` |
| `text`     | `.txt` | `TextView` |
| `json`     | `.json` | `JsonView` |
| `logs`     | `.log` | `LogView` |
| `binary`   | `.bin` `.dat` | `HexView` |
| `images`   | `.png` (+ other raster formats at runtime) | `ImageView` |
| `model3d`  | `.stl` `.obj` `.ply` `.gltf` `.glb` (+ FBX/3MF/… at runtime) | `Model3DView` |
| `audio`    | `.wav` (+ `.mp3` `.flac` `.m4a` `.aac` `.wma` `.ogg` `.opus` at runtime) | `AudioView` |
| `video`    | `.mp4` (+ `.mkv` `.webm` `.mov` `.avi` `.wmv` … at runtime) | `VideoView` |
| `font`     | `.ttf` `.woff` (+ `.otf` `.ttc` at runtime) | `FontView` |

The dataset directory holds more folders than that table lists, on purpose. The `code`, `notebook` and
`archive` sample sets exist for unit/feature tests but route through their feature's own UI tests
(`CodeViewUiTests`, `NotebookViewUiTests`, `CompressedJourneyTests`), not the `SampleFileViewerTests` map
above — so they have no `ViewerMap` row. And `ui/` is not a sample set at all: it is `UiFixtures.Root`,
where the suite that owns each format leaves the material the journeys open. Counting up:
`TestSampleData.Sets` has one entry per family, `ViewerMap.BySet` has a row only for the families whose
files should default-open into a viewer, and the folder count on disk is the sets plus `ui/`.

The default-open route is deterministic because the UI test runs against a fresh `NEXAFLOW_CONFIG_DIR`:
the file-type map (`FileMapManager`) is seeded from the bundled `default-filemap.json`, which maps each
extension above to the owning viewer's experience id. `FileMapManager` stores its map under the active
config root (`ConfigManager.BaseDir`), so a test run is fully isolated from the developer's real map.

When you add a **new viewer**, give its root `UserControl` a stable
`AutomationProperties.AutomationId="…View"`, map its extension(s) to the viewer's experience id in
`default-filemap.json`, and add the `(subDir, "ViewId")` pair to `ViewerMap.BySet`
(`Nexaflow.Tests.Fixtures/ViewerMap.cs`) — the new family joins the `OtherSamples_…` test from there.

## Mutation testing (Stryker.NET)

Every check above asks "does the code do what the test expects?". Mutation testing asks the inverse, which
nothing else here can: **if this line were wrong, would any test notice?** Stryker rewrites one operator,
literal or branch at a time (`>` → `>=`, `&&` → `||`, a string to `""`, a block to empty), reruns the tests
that cover that line, and records whether they went red. A mutant nothing kills is a line the suite is
watching but not actually guarding.

**It is an occasional review tool, run by hand.** Deliberately not wired into `dotnet build`, the
architecture guards, `ci.yml`, or the `NexaflowSetup.slnx` release gate — those are seconds-scale pass/fail
checks and a sweep here is minutes, for an answer that barely moves between one commit and the next. Run it
when you are thinking about a subsystem's test quality: read the survivors, decide, move on.

```powershell
cd tools/mutation
./Run-Mutation.ps1                                    # what the targets are
./Run-Mutation.ps1 -Target initiatives                # a full sweep of one
./Run-Mutation.ps1 -Target all -Since origin/main     # only what this branch changed
./Run-Mutation.ps1 -Cleanup                           # after an interrupted run — see below
```

Stryker itself is pinned in `.config/dotnet-tools.json` (`rollForward: false`); the script restores it.
Reports land in `artifacts/mutation/<target>/reports/` (gitignored) — read the HTML one.

| Target | Mutates | Tested by | Why it is on the list |
|--------|---------|-----------|-----------------------|
| `io-common` | `Nexaflow.IO.Common` | `Tests.IO` | The cleanest 1:1 in the repo, every subject a pure function over bytes or text. WPF-free end to end. Start here. |
| `initiatives` | `Services.Initiatives` | `Tests.Initiatives` | `SnaplinkValidator` + `ProductTreeOps` + `ProductStore` + the graph builder — the release gate and the edits behind it. WPF-free. |
| `search` | `Nexaflow.Search` | `Tests.Features`, `.Viewers`, `.WindowsOS` | Query syntax and AQS evaluation feeding 27 `ISearchable` surfaces. The one target that runs WPF suites. |

### Where it earns its keep

**Validators and parsers whose failure mode is silence.** Every one of `SnaplinkValidator`'s 32 tests hands
it something broken and checks that it complains, so none of them can detect a change that makes it *stop*
complaining — and it gates the installer build (`nexaflowSetup.wixproj` → `ValidateSnaplinks`). A validator
that quietly fails open looks exactly like a clean tree, and those are the survivors to look for: `&&` → `||` on a
null guard, a dropped `!`, `Concat` → `Except` on a candidate set, `Any` → `All` on a member lookup.

Read survivors against coverage: a mutant that survives in code the tests execute is a finding; one in code no test
executes is a coverage gap first.

### Four things that will bite you

- **Use `--test-runner mtp`.** Every suite sets `EnableMSTestRunner` + `OutputType=Exe`, i.e.
  Microsoft.Testing.Platform. Stryker still defaults to the VSTest runner, which cannot see these tests and
  dies inside `VsTestHelper` with an unrelated `ArgumentNullException` about `path3`. The configs set it; a
  hand-rolled `dotnet stryker` invocation must too. MTP support is marked preview in Stryker 4.16.
- **A shared leaf needs every suite that exercises it.** `Nexaflow.Search` mutated against `Tests.Features`
  alone reports `SearchQueryScorer` as 99 mutants with zero coverage — its tests are in `.WindowsOS`. Check
  `nfi graph node <type-id>` for the real consumer set before adding a target.
- **It leaks processes, and on a WPF suite that costs you the session.** A sweep leaves dozens of MSBuild
  node-reuse workers and test hosts behind. Harmless for a WPF-free target; for `search`, enough orphaned WPF
  hosts exhaust the interactive session's desktop heap, and the symptom does not look like a resource
  problem — unrelated WPF tests start failing with `Win32Exception: Not enough memory resources` out of
  `HwndWrapper..ctor` while the machine has tens of GB free, and it does not clear until you sign out.
  `Run-Mutation.ps1` cleans up after every sweep and `-Cleanup` does it standalone, but prefer a machine you
  are not using for that target.
- **A run can leave a mutated assembly behind.** If a handle is held at the end Stryker warns
  `Failed to restore output assembly … Mutated assembly is still in place` and the mutant stays in the test
  project's `bin`. The script rebuilds afterwards to undo it; a hand-rolled run must too.

### What is deliberately not mutated

Feature ViewModels and anything WPF. Most of their mutable surface is binding glue and property plumbing,
their tests need a pumped UI context, Stryker's project analysis fails on several `net10.0-windows`
feature projects, and the desktop-heap hazard above is worst there. Mutation testing here is a tool for
**leaf logic**, not for the shell.

## Where things are

| Concern | File |
|---------|------|
| Sample dataset generator | `Nexaflow.Tests.Fixtures/TestSampleData.cs` |
| Sample catalogs | `Nexaflow.Tests.Fixtures/{Markdown,Tabular,Text,Code,Notebook,Json,Log,Binary,Image,Archive,Model3D,Audio,Video}Samples.cs` |
| UI test base (app launch, isolated config) | `Nexaflow.Tests.UIJourneys/Infrastructure/UITestBase.cs` |
| File-browser UI helpers (navigate, waits) | `Nexaflow.Tests.UIJourneys/Infrastructure/FileSystemUiTestBase.cs` |
| Machine-wide UI gate (semaphore, consent, DPI, foreground) | `Nexaflow.Tests.Fixtures/UiTestGate.cs` |
| Fixtures the journeys open | `Nexaflow.Tests.Fixtures/UiFixtures.cs` + `RequiredFixture.cs` |
| Per-file viewer UI tests | `Nexaflow.Tests.UIJourneys/Features/Fixtures/SampleFileViewerTests.cs` |
| Tabular detection over samples | `Nexaflow.Tests.Features.Viewers/Tabular/SampleFileDetectionTests.cs` |
| Non-tabular fixture smoke (BOM/binary) | `Nexaflow.Tests.Features.Viewers/Fixtures/GeneratedSampleFilesTests.cs` |