# Nexaflow

A modern rethink of the app-centric tooling we've lived with for years.

Nexaflow is an open-source Windows shell replacement that consolidates the tools you reach for every day — file explorer, terminal, text editor, markdown editor, image viewer, project management — into a single tabbed environment with first-class AI assistance baked in. No more context-switching between a dozen single-purpose windows.

![License: Unlicense](https://img.shields.io/badge/license-Unlicense-blue.svg)
![Platform: Windows](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)

---

## Why Nexaflow?

The default Windows experience hasn't fundamentally changed in decades. Windows Explorer, Notepad, and the terminal are separate apps with no shared context, no integrated intelligence, and no cohesion. Nexaflow treats all of these as *tabs* in one coherent workspace — and adds an AI layer that understands what you're looking at.

---

## Features

### Windows Filesystem
Navigate your drive from a built-in file tree. Open any file as the right kind of tab — markdown files open in the editor, images in the viewer, directories in the explorer — without leaving the app.

### Terminal
A full pseudo-terminal (PTY) tab with ANSI/VT support, command history, and interactive shell integration. Type `>` in the AI bar to send a command directly to the console.

### First-Class Markdown Editor
Write and preview Markdown with live rendering, including LaTeX formula support via WpfMath. A proper replacement for Notepad when you're working with `.md` files.

### Image Viewer
Single and batch image viewing as a native tab — no shelling out to Photos.

### Basic Project Management
Lightweight project tracking built for developers. Each project lives as a simple `.project` file. Backlog items have a 9-state workflow and the system generates `.aisummary` files to give the AI instant context on what you're working on.

### Integrated Context-Aware AI
An AI input bar lives at the bottom of every window. Ask questions, get answers, issue commands — the AI knows which tab is active and what project you're in. Provider support includes a local Aria service, Ollama (local models), and Claude (Anthropic), configurable per use-case.

### Web Viewer
An embedded Chromium tab (WebView2) for opening URLs and local HTML files without leaving the workspace.

### Customisable Ribbon
The toolbar is a ribbon you own — add, remove, and reorder buttons and have the layout persist across sessions.

---

## Getting Started

### Prerequisites

- Windows 10 or Windows 11
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Optional: an Aria, Ollama, or Claude API key for AI features

### Build & Run

```bash
git clone https://github.com/smile-forge/nexaflow.git
cd nexaflow

dotnet build Nexaflow.slnx
dotnet run --project src/Nexaflow.Core/Nexaflow.Core.csproj
```

On first launch the Options panel will open so you can choose your AI provider and set your projects root folder.

---

## Project Structure

```
src/
├── Nexaflow.Core/                   # Shell chrome, main window, ribbon, AI input bar
├── Nexaflow.Features/
│   ├── Nexaflow.Features.Common/    # Shared contracts (ITabRegistration, FeatureManager)
│   ├── Nexaflow.Features.Console/   # PTY terminal
│   ├── Nexaflow.Features.Images/    # Image viewer
│   ├── Nexaflow.Features.Markdown/  # Markdown editor + preview
│   ├── Nexaflow.Features.Projects/  # Project and backlog management
│   └── Nexaflow.Features.Web/       # WebView2 browser tab
└── Nexaflow.Providers/
    ├── Nexaflow.Providers.Common/   # LlmProviderRegistry, shared message types
    ├── Nexaflow.Providers.Aria/     # Aria (named-pipe) provider
    ├── Nexaflow.Providers.Claude/   # Claude API provider
    └── Nexaflow.Providers.Ollama/   # Ollama local model provider
```

Each feature is a self-contained assembly that registers a `PageKind` string and a tab factory. Adding a new feature means creating a new project and implementing `ITabRegistration` — the shell discovers it automatically.

---

## Configuration

| Location | Purpose |
|---|---|
| `%APPDATA%\Smile\Nexaflow\ribbon.json` | Persisted ribbon layout |
| `%APPDATA%\Smile\Nexaflow\Conversations\` | Chat history |
| Options panel (first run) | AI provider, projects root folder, theme |

---

## Roadmap

- [ ] Direct Claude and Ollama provider integration in the shell UI
- [ ] Windows Explorer context menu integration
- [ ] Additional file format tabs (CSV, JSON, code with syntax highlighting)
- [ ] Plugin API for community-contributed tabs
- [ ] Multi-monitor awareness and saved workspace layouts

---

## Contributing

Nexaflow is in active development and welcomes contributions. Please open an issue before starting significant work so we can coordinate.

For a deep dive into the architecture, data models, and identified improvement areas see [Architecture.md](Architecture.md).

---

## License

This is free and unencumbered software released into the public domain. See [LICENSE.txt](LICENSE.txt) for details.
