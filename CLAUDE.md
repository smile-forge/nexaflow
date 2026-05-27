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
  Nexaflow.Core/                    shell chrome, main window, ribbon, AI input bar
  Nexaflow.Features/
    Nexaflow.Features.Common/       ALL contracts (interfaces, TabEntry, FeatureManager)
    Nexaflow.Features.AIChat/       AI conversation tab
    Nexaflow.Features.Console/      PTY terminal
    Nexaflow.Features.Images/       image viewer
    Nexaflow.Features.Logs/         log viewer
    Nexaflow.Features.Git/			git interface
    Nexaflow.Features.Markdown/     markdown editor + preview
    Nexaflow.Features.Projects/     project + backlog management
    Nexaflow.Features.Scratchpad/   virtual corkboard
    Nexaflow.Features.Text/         text editor
    Nexaflow.Features.Web/          WebView2 browser tab
    Nexaflow.Features.WindowsSearch/ Windows Search integration
  Nexaflow.Providers/
    Nexaflow.Providers.Common/      LlmProviderRegistry, shared message types
    Nexaflow.Providers.Aria/        named-pipe Aria client
    Nexaflow.Providers.Claude/      Claude API
    Nexaflow.Providers.Ollama/      Ollama local models
```

## Hard Rules

- Features depend only on `Features.Common` — never on Core, rarely on each other
- Providers depend only on 'Providers.Common' - never on Core, never on each other.
- Core never instantiates feature view or ViewModel types directly. All view (tabs) and viewlet creation goes through `FeatureManager`.
- Features communicate back to the shell only via `IShellServices` (injected into `ITabRegistration` constructors by `FeatureManager`).

## Key Files

| File | Why You'd Touch It |
|------|--------------------|
| `src/Nexaflow.Core/ViewModels/ShellViewModel.cs` | Tab lifecycle, ribbon, AI routing — god object, be careful |
| `src/Nexaflow.Core/Services/ShellServices.cs` | `IShellServices` implementation |
| `src/Nexaflow.Features/Nexaflow.Features.Common/*.cs` | Contracts — changes here affect everything |
| `src/Nexaflow.Features/Nexaflow.Features.Common/FeatureManager.cs` | Discovery + constructor injection for features |

## Config & Data Paths

```
%APPDATA%\Smile\Nexaflow\ribbon.json          ribbon layout
%APPDATA%\Smile\Nexaflow\Conversations\       AI chat history
%APPDATA%\Smile\Nexaflow\{ConfigName}\        per-feature config (IFeatureConfig.ConfigName)
```

## Tests

After any change touching `Nexaflow.Core`, run the unit tests before committing:

```powershell
dotnet build src/Nexaflow.Tests/Nexaflow.Tests.Core/Nexaflow.Tests.Core.csproj
src/Nexaflow.Tests/Nexaflow.Tests.Core/bin/Debug/net10.0-windows/Nexaflow.Tests.Core.exe --filter "FullyQualifiedName~Unit"
```

UI tests (`--filter "TestCategory=UI"`) require an interactive desktop session — skip in headless/CI. Run them manually when changes touch shell chrome, tab strip, ribbon, or the AI bar.

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