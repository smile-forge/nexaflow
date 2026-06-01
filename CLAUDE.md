# Nexaflow — Claude Context

## What This Is

Nexaflow is a WPF shell replacement for Windows — file explorer, terminal, text editor, markdown editor, image viewer, project management, and AI assistant in one tabbed window. `.NET 10 / WPF / MVVM Community Toolkit`. The solution file is `Nexaflow.slnx`.

## Build

```powershell
dotnet build Nexaflow.slnx
dotnet run --project src/Nexaflow.Core/Nexaflow.Core.csproj
```

## Project Layout

```
src/
  Nexaflow.Core/                    shell chrome, main window, ribbon, AI input bar, FeatureManager
  Nexaflow.Features/
    Nexaflow.Features.Common/       ALL contracts (interfaces + small DTOs). NO FeatureManager — that's in Core.
    Nexaflow.Features.AIChat/       AI conversation tab (now a browser over conversations)
    Nexaflow.Features.Console/      PTY terminal
    Nexaflow.Features.Git/			git interface
    Nexaflow.Features.Hex/          binary / hex viewer
    Nexaflow.Features.Images/       image viewer
    Nexaflow.Features.Json/         JSON viewer (seek-by-item windowing)
    Nexaflow.Features.Logs/         log viewer (tail-first streaming)
    Nexaflow.Features.Markdown/     markdown editor + preview
    Nexaflow.Features.Projects/     project + backlog management
    Nexaflow.Features.Scratchpad/   virtual corkboard
    Nexaflow.Features.Tabular/      CSV/TSV/fixed-width viewer (shape detection + transforms)
    Nexaflow.Features.Text/         text editor (head-first windowing)
    Nexaflow.Features.Web/          WebView2 browser tab
    Nexaflow.Features.WindowsFileSystem/ file explorer tab (the DirectoryTree + file list)
    Nexaflow.Features.WindowsSearch/ Windows Search integration
  Nexaflow.Visuals.Common/          shared WPF controls + value converters (PieChart, BoolToVisibility, …)
  Nexaflow.Visuals.Text/            shared markdown rendering (MarkdownView / SelectableMarkdownView / MarkdownFlowDocument)
  Nexaflow.Providers/
    Nexaflow.Providers.Common/      LlmProviderRegistry, shared message types
    Nexaflow.Providers.Aria/        named-pipe Aria client
    Nexaflow.Providers.Claude/      Claude API
    Nexaflow.Providers.Ollama/      Ollama local models
```

Shared, non-contract code lives in `Nexaflow.Visuals.*` (UI) — mirror that pattern for any future shared-but-not-a-contract code rather than dumping it in `Features.Common`.

## Hard Rules

- Features depend only on `Features.Common` (and the `Nexaflow.Visuals.*` UI libs) — never on Core, rarely on each other
- Providers depend only on 'Providers.Common' - never on Core, never on each other.
- Core never instantiates feature view or ViewModel types directly. All view (tabs) and viewlet creation goes through `FeatureManager`.
- Features communicate back to the shell only via `IShellServices` (injected into `IPageRegistration` constructors by `FeatureManager`).
- A feature advertises a page via `IPageRegistration` (`PageKind` + `CreatePage`); `FeatureManager` discovers it by reflection (each registration exposes a `static string StaticPageKind`) — there is no manual `Register(typeof(...))` call.
- **Features never hard-code colours.** Every colour — even one a feature "owns" (status pip, chart/pie series, selection/search wash, post-it paper) — resolves from a theme resource so a theme can retune it: reuse a palette/semantic token (`TextBrush`/`AccentBrush`/`SuccessBrush`/`WarningBrush`/`DangerBrush`/`OnAccentBrush`), the categorical `Swatch.*` bank (for N distinct colours), or a feature-owned token shipped via `IThemeContribution` (like the scratchpad's `PostIt.*`). Code-drawn surfaces read the resource at paint time with a literal only as a last-resort fallback. Full rule + patterns in [docs/theming.md](docs/theming.md) → *Rule: a feature never hard-codes a colour*.

## Key Files

| File | Why You'd Touch It |
|------|--------------------|
| `src/Nexaflow.Core/ViewModels/ShellViewModel.cs` | Tab lifecycle, ribbon, AI routing — god object, be careful |
| `src/Nexaflow.Core/Services/ShellServices.cs` | `IShellServices` implementation |
| `src/Nexaflow.Core/FeatureManager.cs` | Reflection discovery + per-`Workspace` constructor injection for features (lives in **Core**, not Common); `EvictWorkspace` clears the cache on reconfigure |
| `src/Nexaflow.Core/Services/WorkspaceManager.cs` | The `Profiles` list (dropdown) + live `Workspace`s; create/switch/reconfigure/dispose lifecycle |
| `src/Nexaflow.Core/Services/FileSystemFeatureRegistry.cs` | Discovery for the file-system contracts (`IFileAction`/`IFolderAction`/`IFileCreateAction`/`IFolderViewlet`) — NOT FeatureManager |
| `src/Nexaflow.Features/Nexaflow.Features.Common/*.cs` | Contracts — changes here affect everything |
| `src/Nexaflow.Features/Nexaflow.Features.Common/IPageRegistration.cs`, `Page.cs` | The tab/page factory contract (`CreatePage`) and the `Page` model (`Title`/`Icon`/`Breadcrumbs`/`ContentFactory`). NB: the older names `ITabRegistration`/`TabEntry`/`PageFactory` are gone |
| `src/Nexaflow.Core/Themes/Styles.xaml` | App-merged shared control styles. Feature XAML references theme keys by `{StaticResource …}` — no assembly ref needed. A theme is layered (palette → region tokens → per-theme overrides + scenes → styles); see [docs/theming.md](docs/theming.md) |

## Config & Data Paths

Base: `%APPDATA%\Smile\nexaflow\`

```
{ConfigName}\                 GLOBAL app/feature config (IFeatureConfig.ConfigName) — shared by all profiles
Contexts\<name>\              PER-PROFILE data (folder name kept as "Contexts" for compat):
  ai-abilities\               AI ability grid (which provider/model per ability)
  <provider configs>\         provider API keys / subscriptions for THIS profile
  Conversations\              AI chat history for THIS profile
  <ribbon layout>             ribbon items for THIS profile
```

Ribbon, AI ability grid, provider configs and conversations are **per-profile** (not global). Feature `IFeatureConfig` is **global** (one instance per assembly).

## Profile / Workspace scoping

A **`Profile`** is the saved, shared config shown in the dropdown; a **`Workspace`** is a runtime grouping of one-or-more window frames running ONE profile. Getting scope wrong is the easiest way to add a bug — full detail in [docs/Architecture.md → Ownership & Lifetime](docs/Architecture.md#ownership--lifetime).

- **Central (one per process):** `ConfigManager`, `ProviderManager` (loads provider **assemblies/types**; owns the **ref-counted instance pool** — identical configs share one provider), `WorkspaceManager` (the `Profiles` list + live `Workspace`s), `FeatureManager` (feature **types**; builds instances per Workspace), `BackgroundActivityManager`. Global configs incl. the **AI persona** and every feature `IFeatureConfig`.
- **Per-`Profile` (shared, saved):** ability→model assignments (`AiConfig`), provider configs (API keys), ribbon layout (live-synced across its workspaces via `RibbonChanged`), conversations. All under `Contexts\<name>\`.
- **Per-`Workspace` (runtime):** `ShellServices` (windows/tabs), `AIService` (agent loop), the **acquired** provider instances. App/IPC launch = a new Workspace; tear-off / "open in new window" = same Workspace; dropdown switch reconfigures the current Workspace in place (tabs close, providers/AIService rebuilt); closing the last window disposes it.
- The `IShellServices` / `IAIService` injected into a feature are the **active workspace's** — opening a tab or asking the AI always acts within one workspace.
- Options & Manage-AI overlays are **modal** (block profile switching); you can't delete the active profile; there's always ≥1 profile.

Mnemonic: **persona & feature settings = global; ability grid, provider configs, conversations, ribbon = per-profile (shared); AIService, providers, windows/tabs = per-workspace (runtime).**

## Tests

After any change touching `Nexaflow.Core`, run the unit tests before committing:

```powershell
dotnet build src/Nexaflow.Tests/Nexaflow.Tests.Core/Nexaflow.Tests.Core.csproj
src/Nexaflow.Tests/Nexaflow.Tests.Core/bin/Debug/net10.0-windows/Nexaflow.Tests.Core.exe --filter "FullyQualifiedName~Unit"
```

UI tests (`--filter "TestCategory=UI"`) require an interactive desktop session — skip in headless/CI. Run them manually when changes touch shell chrome, tab strip, ribbon, or the AI bar.

## Potential WPF Gotchas

The global MenuItem style in src/Nexaflow.Core/Themes/Styles.xaml overrides the default WPF template. If you need submenus, header arrows, or Role-dependent behavior, extend that template — adding child MenuItems in code isn't enough.

ItemsControl.ItemsSource binding + Items.Add is illegal — pick one

ObservableCollection.Clear() + N × Add() fires N+1 CollectionChanged events — the intermediate state of "empty" can render as a blank frame if anything in the view rebuilds on each event. Batch updates via Dispatcher.BeginInvoke

A bare string assigned to ToolTip inherits the parent's TextAlignment when WPF wraps it in the default popup TextBlock. Assign an explicit TextBlock if you care about alignment

## Other design considerations

**Large-file reading** — there are four established strategies; pick the one whose access pattern matches your data shape before inventing a fifth. Each reader's *strategy* is deliberately feature-specific (the data structure differs); only mechanical leaves (encoding/BOM sniffing, the debounced `FileSystemWatcher` wrapper) are duplication worth sharing — see [docs/arch_improvements.md](docs/arch_improvements.md).

| Strategy | Canonical reader | When |
|----------|------------------|------|
| Tail-first + background head-load | `Logs/ViewModels/LogViewModel.cs` | append-only files where the recent end matters most |
| Head-first window + placeholder padding for scrollbar | `Text/ViewModels/TextViewModel.cs` | top-of-file-first text; line index built up front |
| Full-rescan per window (no byte anchors) | `Tabular/RowWindowReader.cs` | row data; `StreamReader` buffering makes `BaseStream.Position` unreliable for cross-call seeks |
| Seek-by-item via byte-offset index | `Json/JsonFileLoader.cs` | random access to structured items |

**Shared UI & theming** — shared controls live in `Nexaflow.Visuals.Common` (controls + converters) and `Nexaflow.Visuals.Text` (markdown). Theme brushes **and** shared control styles live in the app-merged `src/Nexaflow.Core/Themes/Styles.xaml`; feature XAML pulls them by `{StaticResource <key>}` with no assembly reference (the resource lookup walks up to `Application.Resources`). Put a new *shared* style there and reference it by key rather than copy-pasting per view. A theme is assembled in layers (palette → region tokens → per-theme overrides + scenes → styles) by `ThemeManager`; a region can carry an animated backdrop via `ThemedRegion` + a `Scene.{Region}` template, and a feature can extend theming without Core referencing it via `IThemeContribution` — full model in [docs/theming.md](docs/theming.md). Markdown rendering is centralised in `Nexaflow.Visuals.Text` (`SelectableMarkdownView`) — used by Core's AI overlay and AIChat; reuse it rather than hand-rolling `RichTextBox`.

## Style Notes

- Terse commits. Say why it changed, not what.
- Prefer clean architecture over simplicity for an action.
- Design for good structure and then trust the structure to make life easy - if its hard or convoluted or the structure is wrong.
- Trust the DI.
- Primary MVVM Toolkit patterns: `[ObservableProperty]`, `[RelayCommand]`, constructor injection. No static singletons in feature ViewModels.

## Working With the User

- Direct, short questions and terse commit-style explanations preferred.
- One concrete recommendation over a list of options.
- When something needs a worktree (feature branch), use one; merge via PR.
- Session transcripts are not searchable — check [docs/Architecture.md](docs/Architecture.md) and if implementing a feature [docs/features.md](docs/features.md) for context before exploring the codebase.