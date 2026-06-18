# Testing

How the Nexaflow test suite is structured, how the shared sample-file dataset works, and how to
add coverage for a new viewer or fixture.

## Projects

All under `src/Nexaflow.Tests/`:

| Project | Target | Covers | References |
|---------|--------|--------|------------|
| `Nexaflow.Tests.Core` | `net10.0-windows10.0.19041.0`, MSTest exe | Core shell chrome, services, `Nexaflow.Visuals.*` | `Nexaflow.Core`, `Nexaflow.Visuals.Text`, `Nexaflow.Tests.Fixtures` |
| `Nexaflow.Tests.Features` | `net10.0-windows10.0.19041.0`, MSTest exe | Every `Nexaflow.Features.*` | the feature projects + `Nexaflow.Tests.Fixtures` — **never Core** |
| `Nexaflow.Tests.Providers` | MSTest exe | Provider clients (Claude/OpenAI/Gemini/Ollama/…) | the provider projects |
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
