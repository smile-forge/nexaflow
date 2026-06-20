# Testing

How the Nexaflow test suite is structured, how the shared sample-file dataset works, and how to
add coverage for a new viewer or fixture.

## Projects

All under `src/Nexaflow.Tests/`:

| Project | Target | Covers | References |
|---------|--------|--------|------------|
| `Nexaflow.Tests.Core` | `net10.0-windows10.0.19041.0`, MSTest exe | Core shell chrome, services, `Nexaflow.Visuals.*` | `Nexaflow.Core`, `Nexaflow.Visuals.Text`, `Nexaflow.Tests.Fixtures` |
| `Nexaflow.Tests.Features` | `net10.0-windows10.0.19041.0`, MSTest exe | Every `Nexaflow.Features.*` | the feature projects + `Nexaflow.Tests.Fixtures` — **never Core** |
| `Nexaflow.Tests.Providers` | MSTest exe | Provider clients — **no test classes yet** (placeholder project) | the provider projects |
| `Nexaflow.Tests.Fixtures` | `net10.0` class library | **Generates the sample dataset.** Not a test project — no MSTest, no `[TestClass]` | nothing (deliberately dependency-free) |

`Nexaflow.Tests.Features` deliberately does **not** reference Core (it mirrors the architectural rule
that features don't depend on Core). The sample-data generator therefore lives in its own
dependency-free library, `Nexaflow.Tests.Fixtures`, so both test projects can share it without one
test exe dragging in another (and without pulling Core's x64 RID into the Features tests).

## Running

```powershell
# Build + run a project's tests (MSTest runner exe):
dotnet build src/Nexaflow.Tests/Nexaflow.Tests.Features/Nexaflow.Tests.Features.csproj
$exe = "src/Nexaflow.Tests/Nexaflow.Tests.Features/bin/Debug/net10.0-windows10.0.19041.0/Nexaflow.Tests.Features.exe"

& $exe --filter "TestCategory!=UI"                       # everything except UI (CI-safe, headless)
& $exe --filter "TestCategory=UI"                        # UI tests — needs an interactive desktop
& $exe --filter "FullyQualifiedName~SampleFileDetection" # one class
```

### Categories

- **Unit / non-UI** — fast, headless, no desktop. The default for CI.
- **`TestCategory("UI")`** — drives the real `Nexacore.exe` via FlaUI (UI Automation). Requires an
  interactive desktop session; skip in headless/CI with `--filter "TestCategory!=UI"`. Each UI test
  launches a fresh app against an **isolated config root** (`NEXAFLOW_CONFIG_DIR` → a throwaway temp
  dir), so it neither depends on nor pollutes the developer's real `%APPDATA%` config. See
  `UITestBase`.

## Coverage by feature

Feature assemblies (`Nexaflow.Features.*`) are tested in `Nexaflow.Tests.Features`. **Unit** = the
headless `TestCategory!=UI` tests; **UI** = `[TestCategory("UI")]` (drives the real shell via FlaUI).
The file-viewer features additionally get a per-file open-smoke UI case from the shared, data-driven
`SampleFileViewerTests` (each fixture → its viewer), shown below as *via SampleFileViewer*.

| Feature | Unit tests | UI tests |
|---------|-----------|----------|
| AIChat | ✅ `ConversationContextReadiness` | — |
| Console | — | — |
| Dotnet | ✅ `DotnetTargetScanner`, `DotnetViewletViewModel` | — |
| Git | ✅ `GitService` | — |
| Hex | ✅ `HexBuffer`, `HexViewModel` | ✅ *via SampleFileViewer* (`binary` → `HexView`) |
| Images | — | — |
| Json | ✅ `JsonFileLoader` (small + large streaming, virtual-chunk windowing, BOM, estimation) | ✅ *via SampleFileViewer* (`json` → `JsonView`) |
| Logs | ✅ `LogViewModel` (small load, tail-first read, encoding detection) | ✅ *via SampleFileViewer* (`logs` → `LogView`) |
| Markdown | ✅ `MarkdownViewModelEditing` (editor model; rendering is covered in `Tests.Core`) | ✅ *via SampleFileViewer* (`markdown` → `MarkdownView`) |
| Processes | ✅ `CpuSampling`, `Handles`, `ProcessTools`, `ProcessTreeBuilder`, `Reconciliation` | ✅ `ProcessesViewTests` |
| Projects | — | — |
| Scratchpad | ✅ `DroppedMedia`, `PostItStore`, `PostItViewModel`, `ScratchpadConfig`, `ScratchpadViewModel`, `UrlPreviewTask` | — |
| SystemInfo | ✅ `EnvironmentVariablesViewModel`, `EnvVarModel`, `EnvVarsCollector`, `ServicesCollector`, `ServicesViewModel`, `SystemInfoViewModel` | — |
| Tabular | ✅ 13 classes (CSV tokeniser, shape + column-type detection, column transforms, windowed `RowWindowReader`/`LineSamplingReader`, encoding detection, sample detection, …) | ✅ *via SampleFileViewer* (`tabular` → `TabularView`) |
| Text | ✅ `TextViewModel` (small + large windowed load, window advance) | ✅ *via SampleFileViewer* (`text` → `TextView`) |
| Web | ✅ `WebPageChrome` | — |
| WindowsApps | ✅ `WindowsAppsViewModel` | — |
| WindowsFileSystem | ✅ 15 classes (file-type map, create actions + templates, glob matcher, external apps, tree node, view model, …) | ✅ `FileSystemViewTests`, `FileSystemCreateTests`, `TemplatedCreateOptionsTests` |
| WindowsRegistry | — | — |
| WindowsSearch | ✅ `SearchQueryParser`, `SearchQueryScorer`, `SearchResultEntry`, `SearchViewModel` | ✅ `SearchViewTests` |

`Features.Common` (contracts) has no test folder of its own; its client-tool wire-protocol parser
(`ClientBlockParser`) and the agent loop are tested in `Tests.Core` (`Unit/ClientTools/`).

The windowed-reader view-models load into a thread-affine AvalonEdit `TextDocument` across `await`
points, so `LogViewModel`/`TextViewModel` tests run under `Infrastructure/AsyncPump.cs` (a
single-threaded synchronization context). `LogViewModel`'s background head-reassembly needs a live UI
`Dispatcher`, so that one path is left to the UI smoke rather than a unit test.

**No coverage yet:** Console, Images, Projects and WindowsRegistry have no tests.

### Core & shared libraries (`Nexaflow.Tests.Core`)

Covers `Nexaflow.Core` and the `Nexaflow.Visuals.*` libraries.

- **Unit** — background activity, config manager, conversation store, message center, panes, shell
  services, workspace manager + config scoping, elevation contracts + bridge launcher, the client-tool
  parser + agent loop, and the WPF-free Markdown/diagram parsers, pipeline factory and Sugiyama layout.
- **UI** — app launch, notifications, setup wizard and the tab strip (Core shell); plus the
  `Visuals.Text` Markdown renderer (`BlockRenderer`, `MarkdownView`, extensions, diagram renderer,
  sample render).

### Providers (`Nexaflow.Tests.Providers`)

The project exists (MSTest exe) but currently ships **no test classes** — the provider clients
(Claude, OpenAI, Gemini, Ollama, Aria) are untested.

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
  json/       object, array, deeply nested, and a 1,000-item array for seek-by-item windowing
  logs/       short + long timestamped logs (tail-first streaming)
  binary/     random / zeros / mixed / PNG-header blobs for the hex viewer
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

Each set is a small class (`MarkdownSamples`, `TabularSamples`, `TextSamples`, `JsonSamples`,
`LogSamples`, `BinarySamples`) registered in `TestSampleData.Sets`. Long/large fixtures are built
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

The default-open route is deterministic because the UI test runs against a fresh `NEXAFLOW_CONFIG_DIR`:
the file-type map (`FileMapManager`) is seeded from the bundled `default-filemap.json`, which maps each
extension above to the owning viewer's experience id. `FileMapManager` stores its map under the active
config root (`ConfigManager.BaseDir`), so a test run is fully isolated from the developer's real map.

When you add a **new viewer**, give its root `UserControl` a stable
`AutomationProperties.AutomationId="…View"`, map its extension(s) to the viewer's experience id in
`default-filemap.json`, and add the `(subDir, "ViewId")` pair to `SampleFileViewerTests.ViewerBySet`.

## Where things are

| Concern | File |
|---------|------|
| Sample dataset generator | `Nexaflow.Tests.Fixtures/TestSampleData.cs` |
| Sample catalogs | `Nexaflow.Tests.Fixtures/{Markdown,Tabular,Text,Json,Log,Binary}Samples.cs` |
| UI test base (app launch, isolated config) | `*/UI/Infrastructure/UITestBase.cs` |
| File-browser UI helpers (navigate, waits) | `Nexaflow.Tests.Features/WindowsFileSystem/UI/FileSystemUiTestBase.cs` |
| Per-file viewer UI tests | `Nexaflow.Tests.Features/Fixtures/SampleFileViewerTests.cs` |
| Tabular detection over samples | `Nexaflow.Tests.Features/Tabular/SampleFileDetectionTests.cs` |
| Non-tabular fixture smoke (BOM/binary) | `Nexaflow.Tests.Features/Fixtures/GeneratedSampleFilesTests.cs` |
