# Testing

How the Nexaflow test suite is structured, how the shared sample-file dataset works, and how to
add coverage for a new viewer or fixture.

## Projects

All under `src/Nexaflow.Tests/`:

| Project | Target | Covers | References |
|---------|--------|--------|------------|
| `Nexaflow.Tests.UIJourneys` | `net10.0-windows10.0.19041.0`, MSTest exe | **Every test that launches the app.** `Core\` for the shell, `Features\<Feature>\` for the rest | `Nexaflow.Tests.Fixtures` **and nothing else** — a journey knows the app as a running process, never as an assembly |
| `Nexaflow.Tests.Core` | `net10.0-windows10.0.19041.0`, MSTest exe | Core shell chrome, services, `Nexaflow.Visuals.*` | `Nexaflow.Core`, `Nexaflow.Visuals.Text`, `Nexaflow.Tests.Fixtures` |
| `Nexaflow.Tests.Features` | `net10.0-windows10.0.19041.0`, MSTest exe | The shell-adjacent features: AI chat, console, network discovery, OneDrive, Product/Projects, scratchpad, This PC, web — plus the generic search plumbing | those feature projects + `.Common` + `Nexaflow.Tests.Fixtures` — **never Core** |
| `Nexaflow.Tests.Features.Viewers` | same | Every viewer/editor/player — Audio…Video, plus the sample-file corpus | the viewer feature projects + `.Common` + `Nexaflow.Tests.Fixtures` |
| `Nexaflow.Tests.Features.WindowsOS` | same | The features that inspect and drive Windows: file system, registry, search index, installed apps, processes, system info | those feature projects + `.Common` + `Nexaflow.Tests.Fixtures` |
| `Nexaflow.Tests.Features.Architecture` | same | The whole-repo guards: reference/dispatcher rules, add-a-feature touch points, solution membership, XAML keys, `[CoversNode]` declarations | the suites above **and** `Tests.Initiatives` (for their **output**, not their API) |
| `Nexaflow.Tests.Features.Common` | `net10.0-windows10.0.19041.0` class library | **Shared support.** Not a test project — `AsyncPump`, `RepoRoot`, `DicomTestFiles`, the `ISearchable` conformance contract. The FlaUI bases left with the journeys; `ViewerMap` moved to `Tests.Fixtures` | FlaUI + `Features.Common` + `Nexaflow.Search` + `Nexaflow.Tests.Fixtures` — **no feature** |
| `Nexaflow.Tests.IO` | `net10.0-windows`, MSTest exe | `Nexaflow.IO.*` — the WPF-free IO leaves: `IO.Common`, `IO.Protocol` (DynamicProtocol + the ten-protocol corpus), `IO.Network` | those three + `Nexaflow.Tests.Fixtures` — **nothing else** |
| `Nexaflow.Tests.Initiatives` | `net10.0`, MSTest exe | `Nexaflow.Services.Initiatives` + its CLI — the product tree, the knowledge graph, `SnaplinkValidator`, `ProductTreeOps`, the verb parser | `Services.Initiatives`, `Services.Initiatives.Cli`, `Nexaflow.Tests.Fixtures` — **nothing else** |
| `Nexaflow.Tests.Providers` | MSTest exe | Provider clients — network-free provider surface, config round-trips, `PromptComposer`, `LlmAttachment`, Aria wire protocol | the provider projects |
| `Nexaflow.Tests.Fixtures` | `net10.0` class library | **Generates the sample dataset**, plus `UiFixtures` (the material the journeys open) and `ViewerMap`. Not a test project — no MSTest, no `[TestClass]` | nothing (deliberately dependency-free) |

No `Nexaflow.Tests.Features*` suite references Core (they mirror the architectural rule that features
don't depend on Core). The sample-data generator therefore lives in its own dependency-free library,
`Nexaflow.Tests.Fixtures`, so the test projects can share it without one test exe dragging in another
(and without pulling Core's x64 RID into the feature tests).

### Why the feature tests are four projects

One project referencing ~50 feature assemblies meant editing a single viewer test rebuilt everything.
The suites are split by **subject**, so a viewer change rebuilds only `Viewers`. Two consequences worth
knowing:

- **`Search/` was split with them.** It is one `<Feature>SearchableTests.cs` per feature, and keeping it
  whole would have left its project referencing nearly the entire graph — which is exactly the cost the
  split exists to remove. Each file follows its feature; only the feature-agnostic ones (query syntax,
  term parsing, the conformance guard) stayed behind.
- **`Architecture` is the heaviest project on purpose.** Its guards reflect over every
  `Nexaflow.Features.*.dll` *and* every suite DLL matching `FeatureTestSuites.Patterns`, so it references
  the suites to bring both sets into its output directory rather than guessing at sibling `bin` paths that
  shift with configuration and target framework. It is also the project nobody edits while working on a
  feature. **A new suite must be added to `Patterns`** — that is the one place the discovery is spelled out,
  and a suite missing from it silently drops out of the `[CoversNode]` guard.

Namespaces did **not** change: a test in `Viewers` is still `Nexaflow.Tests.Features.Audio`. That keeps the
folder→feature convention `CoverageGuardTests` enforces readable across all four assemblies, and meant no
`using` churn when files moved.

**Which project a test belongs in is decided by its subject, not its imports.** A test whose subject is an
IO library goes in `Tests.IO`; one that merely reaches through an IO library on its way to a feature —
`Text` opening a file via `EncodingDetector`, `Compressed` browsing through the VFS — stays with the
feature. The rule is worth stating because the second kind is far more common, and moving those would drag
the whole feature graph back into a project whose value is that it has none of it.

**The same rule put `Tests.Initiatives` where it is.** Its 265 tests used to sit in
`Tests.Features\ProductManager\`, where they were hosted in a `UseWPF` project and needed a desktop session
— despite referencing nothing but `Services.Initiatives`, its CLI and `Tests.Fixtures`. Their subject is a
WPF-free backend library, exactly like `Tests.IO`'s, so the suite is one too: plain `net10.0`, no shell, no
desktop. What stayed behind in `Tests.Features` is the ProductManager *feature* — the view-models, the AI
client tools, the graph viewer — which genuinely needs WPF. A test belongs in `Tests.Initiatives` when its
subject is `Nexaflow.Services.Initiatives(.Cli)`; one that reaches it through the feature stays with the
feature.

That split also made the `initiatives` mutation target cheap and safe to run — see
[Mutation testing](#mutation-testing-strykernet).

## Running

```powershell
# Build + run a project's tests (MSTest runner exe):
dotnet build src/Nexaflow.Tests/Nexaflow.Tests.Features/Nexaflow.Tests.Features.csproj
$exe = "src/Nexaflow.Tests/Nexaflow.Tests.Features/bin/x64/Debug/net10.0-windows10.0.19041.0/Nexaflow.Tests.Features.exe"

& $exe --filter "TestCategory!=UI&TestCategory!=Interactive"   # CI-safe, headless
& $exe --filter "TestCategory=UI"                        # UI tests — needs an interactive desktop
& $exe --filter "TestCategory=Interactive"               # calls real OS services — dev machines only
& $exe --filter "FullyQualifiedName~SampleFileDetection" # one class
```

### Categories

- **Unit / non-UI** — fast, headless, no desktop. The default for CI.
- **`TestCategory("Desktop")`** *(`Tests.Core`)* — **shows a real window and takes focus.** Focus is a
  single machine-wide resource, so these must also carry **`[DoNotParallelize]`**: run two at once and
  they take it from each other mid-assertion, which surfaces as a *different* test failing on each run
  rather than as anything resembling a real bug. `DesktopTestCategoryGuardTests` enforces this — a class
  whose source shows a window must declare the category and opt out of parallelism.
- **`TestCategory("UI")`** — needs an interactive desktop session; skip in headless/CI with
  `--filter "TestCategory!=UI"`. In `Tests.Core` this now means only *renders WPF off-screen* — an STA
  thread, no window, safe to parallelise. Elsewhere, two kinds, and the split is the point:
  - **`Nexaflow.Tests.UIJourneys`** drives the real `Nexaflow.exe` via FlaUI. Each test launches a fresh
    app against an **isolated config root** (`NEXAFLOW_CONFIG_DIR` → a throwaway temp dir), so it neither
    depends on nor pollutes the developer's real `%APPDATA%` config. See `UITestBase`.
  - The remainder, in `Tests.Core/Visuals`, render WPF controls off-screen. They want a desktop session
    but launch nothing and touch no pointer, so they stay beside their subject.

  They live in one assembly because they used to be spread over four, and a whole-suite run then started
  four test hosts: each asked for the machine separately, and each launched its own app, so the instances
  stole one another's clicks. One assembly is one launcher and one prompt; `UiTestGate` adds a
  machine-wide semaphore so even a concurrent host cannot put a second app on screen.

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
  and `UI`). The worked example is `AqsTranslatorInteractiveTests`, which exercises the Windows Search
  COM interop: the interop declarations are a hand-transcribed vtable, and only a real call can prove
  the layout is right — a wrong slot is an access violation, not a failed assertion. Such a test
  asserts on the *contract* (a clause came back naming the property asked for), never on what happens
  to be indexed, and calls `Assert.Inconclusive` when the service isn't running.

### Coverage declaration (`[CoversNode]` / `[NoCoverage]`)

Every concrete `[TestClass]` must declare the product-tree node it backs with `[CoversNode("node-id")]`
(from `Nexaflow.Tests.Fixtures`, repeatable, also valid on a method), or opt out with `[NoCoverage("reason")]`
for tests that map to no single node (the `Architecture/` guards, `Fixtures/` sample-corpus tests). Abstract
test bases need no attribute. This is enforced at author time by the `Nexaflow.Analyzers.Coverage` analyzer
(NXCOV001 = missing declaration, NXCOV002 = stale id) and in CI by `CoverageDeclarationGuardTests` (one per
test assembly). `dotnet run --project src/Nexaflow.Services.Initiatives.Cli -- scan-tests . --suggest-attributes`
prints the starter set derived from the tree's existing `tests` snaplinks. See CLAUDE.md → *Test coverage is
declared on the test* for the full loop (scan-tests → manifest → Integrity-page reconcile → Add link).

## Coverage by feature

Feature assemblies (`Nexaflow.Features.*`) are tested across the `Nexaflow.Tests.Features*` suites,
**one folder per feature** (`Audio/`, `Compressed/`, `Projects/`, … — the Code feature's tests live under
`CodeIntel/`). Which suite holds the folder follows the feature's subject; shared support lives in
`Nexaflow.Tests.Features.Common`.
**Unit** = the headless `TestCategory!=UI` tests; **UI** = `[TestCategory("UI")]` (drives the real
shell via FlaUI). The file-viewer features additionally get a per-file open-smoke UI case from the
shared, data-driven `SampleFileViewerTests` (each fixture → its viewer; see
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
tree — that is the maintenance step that replaces editing a table here.

`Features.Common` (contracts) has no test folder of its own; its client-tool wire-protocol parser
(`ClientBlockParser`) and the agent loop are tested in `Tests.Core` (`Unit/ClientTools/`).

The windowed-reader view-models load into a thread-affine AvalonEdit `TextDocument` across `await`
points, so `LogViewModel`/`TextViewModel` tests run under `Infrastructure/AsyncPump.cs` (a
single-threaded synchronization context). `LogViewModel`'s background head-reassembly needs a live UI
`Dispatcher`, so that one path is left to the UI smoke rather than a unit test.

### Core & shared libraries (`Nexaflow.Tests.Core`)

Covers `Nexaflow.Core` and the `Nexaflow.Visuals.*` libraries.

- **Unit** — background activity, config manager, conversation store, message center, panes, shell
  services, workspace manager + config scoping, elevation contracts + bridge launcher, the client-tool
  parser + agent loop, and the WPF-free Markdown/diagram parsers, pipeline factory and Sugiyama layout.
- **UI** — app launch, notifications, setup wizard and the tab strip (Core shell); plus the
  `Visuals.Text` Markdown renderer (`BlockRenderer`, `MarkdownView`, extensions, diagram renderer,
  sample render).

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

Every sample file has a UI test that opens it through the real shell and asserts it loads in the
expected in-app viewer. `SampleFileViewerTests` is **data-driven** (`[DynamicData]` over
`TestSampleData.Files(...)`), so adding a fixture automatically adds its UI case — one test per file,
named e.g. `tabular/quoted_fields.csv → TabularView`.

Each case navigates the file browser to the sample's folder, double-clicks the file (the default-open
route), and waits for the viewer's root `AutomationProperties.AutomationId`:

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

(The `code`, `notebook` and `archive` sample sets also exist for unit/feature tests but route through their feature's own UI tests, not the data-driven `SampleFileViewerTests` map above.)

The default-open route is deterministic because the UI test runs against a fresh `NEXAFLOW_CONFIG_DIR`:
the file-type map (`FileMapManager`) is seeded from the bundled `default-filemap.json`, which maps each
extension above to the owning viewer's experience id. `FileMapManager` stores its map under the active
config root (`ConfigManager.BaseDir`), so a test run is fully isolated from the developer's real map.

When you add a **new viewer**, give its root `UserControl` a stable
`AutomationProperties.AutomationId="…View"`, map its extension(s) to the viewer's experience id in
`default-filemap.json`, and add the `(subDir, "ViewId")` pair to `SampleFileViewerTests.ViewerBySet`.

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
that quietly fails open looks exactly like a clean tree. The first run of that target found four such
mutants alive: `&&` → `||` on a null guard, a dropped `!`, `Concat` → `Except` on the candidate set, and
`Any` → `All` on a member lookup.

Contrast the search target's first run, which is the reassuring case: of the mutants planted in code the
tests actually execute, **all 163 were killed**. Where a test ran, it noticed.

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
their tests need a pumped UI context, Stryker's project analysis already fails on several `net10.0-windows`
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
