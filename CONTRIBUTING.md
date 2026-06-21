# Contributing to Nexaflow

Thanks for your interest in improving Nexaflow! Contributions of all kinds are
welcome — bug reports, ideas, documentation, and code.

By taking part in this project you agree to abide by our
[Code of Conduct](CODE_OF_CONDUCT.md).

## Before you start

Nexaflow is in active development. **Please open an issue before starting any
significant work** so we can discuss the approach and avoid duplicated effort.
For small fixes (typos, obvious bugs), a direct pull request is fine.

## Ways to contribute

- **Report a bug** — open an issue with steps to reproduce, what you expected, and
  what happened. Include your Windows version where relevant.
- **Suggest a feature** — open an issue describing the problem you're trying to
  solve, not just the solution.
- **Submit a change** — fix a bug, improve docs, or build a feature (see below).

## Development setup

You'll need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
on Windows 10 or 11.

```bash
git clone https://github.com/smile-forge/nexaflow.git
cd nexaflow
dotnet build Nexaflow.slnx
dotnet run --project src/Nexaflow.Core/Nexaflow.Core.csproj
```

## Understanding the codebase

Nexaflow has a deliberately strict, modular architecture — a thin shell that
hosts independent feature and provider modules. Read these before making
structural changes:

- [docs/Architecture.md](docs/Architecture.md) — layering, ownership & lifetime, dependency rules
- [docs/features.md](docs/features.md) — how to build and register a new tab/feature
- [docs/theming.md](docs/theming.md) — the theme system (features never hard-code colours)
- [docs/testing.md](docs/testing.md) — the test projects and how to run them

A few rules worth knowing up front:

- Features depend only on `Nexaflow.Features.Common` (plus the shared `Nexaflow.Visuals.*` UI libs) — never on Core, rarely on each other.
- Features never touch the UI dispatcher directly — marshal through `IShellServices`.
- Features never hard-code colours — every colour resolves from a theme resource.
- Use the project's confirmation overlay for prompts, never a `MessageBox`.

## Coding style

- Follow the patterns already in the surrounding code — match its naming, structure, and comment density.
- MVVM Toolkit conventions: `[ObservableProperty]`, `[RelayCommand]`, constructor injection. No static singletons in feature ViewModels.
- Favour good structure over shortcuts.
- Keep commit messages terse — explain *why* a change was made, not *what* changed.

## Tests

Run the unit tests before opening a pull request, especially for changes to Core:

```powershell
dotnet build src/Nexaflow.Tests/Nexaflow.Tests.Core/Nexaflow.Tests.Core.csproj
src/Nexaflow.Tests/Nexaflow.Tests.Core/bin/Debug/net10.0-windows/Nexaflow.Tests.Core.exe --filter "FullyQualifiedName~Unit"
```

UI tests (`--filter "TestCategory=UI"`) need an interactive desktop session — run
them locally when your change touches shell chrome, the ribbon, the AI bar, or a
viewer. See [docs/testing.md](docs/testing.md) for the full guide.

## Pull requests

1. Branch off `main`.
2. Keep the change focused — one logical change per pull request.
3. Make sure the build is clean and unit tests pass.
4. Reference the related issue in the description.
5. Open the pull request against `main`.

## License

Nexaflow is released into the **public domain** under the
[Unlicense](LICENSE.txt). By contributing, you agree that your contributions are
dedicated to the public domain on the same terms.
