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

A full pseudo-terminal (PTY) tab with ANSI/VT support, command history, and interactive shell integration. Type `>` in the AI bar to send a command directly to the active console.

### Markdown Editor

Write and preview Markdown with live rendering, including LaTeX formula support via WpfMath.

### Image Viewer

Single and batch image viewing as a native tab.

### Text Editor

Lightweight plain-text editor tab for quick edits without shelling out to Notepad.

### Log Viewer

Tail and inspect log files with filtering, as a tab. Built for large files — recent lines stream in first.

### JSON Viewer

Browse large JSON documents with seek-by-item windowing, so multi-gigabyte files open instantly.

### Tabular Viewer

CSV / TSV / fixed-width data as a tab, with automatic shape detection and column transforms.

### Hex / Binary Viewer

Inspect any file byte-for-byte in a hex viewer.

### Windows Search

Full-text file search backed by the Windows Search index, with AI-powered query refinement.

### Windows Apps Manager

Browse and manage installed Windows applications, surfaced as a tab and queryable through the AI bar.

### System Info Dashboard

A system-information dashboard at a glance — hardware, services, and environment variables (the latter two via an elevated privilege bridge).

### Process Explorer

A live process tree with per-process details (threads, modules, handles, performance) in vertical tabs. Kill or re-prioritise a process — escalating through the elevated privilege bridge when the action needs it — and query it all through the AI bar.

### Windows Registry Editor

Browse and edit the Windows registry as a tab, with AI tools for reading and (approval-gated) writing keys and values.

### Scratchpad

A virtual corkboard for temporary notes. Post-its auto-expire, can be pinned, and support multiple shapes and colours on an infinite canvas.

### Project Management

Lightweight project tracking built for developers. Each project lives as a simple `.project` file. Backlog items have a 9-state workflow and the system generates `.aisummary` files to give the AI instant context on what you're working on.

### Integrated Context-Aware AI

An AI input bar lives at the bottom of every window. Ask questions, get answers, issue commands — the AI knows which tab is active and what project you're in. It runs as an agent: multi-step, tool-using turns with client-side tools and an approval step before anything mutating happens. Provider support includes a local Aria service, Ollama (local models), Claude (Anthropic), Google Gemini, and OpenAI — assigned per *ability* so different tasks can use different models.

### AI Chat

A dedicated conversation tab that browses your saved chat history, with inline agent runs and a rich set of available tools.

### Folder Viewlets (Git & .NET)

Open a folder and Nexaflow surfaces context panels for what it contains — a Git panel for repositories, a .NET panel for projects/solutions. These viewlets feed both context *and* tools straight to the AI, so it understands your repo and project structure without being told.

### Web Viewer

An embedded Chromium tab (WebView2) for opening URLs and local HTML files without leaving the workspace.

### Customisable Ribbon

Add, remove, and reorder toolbar buttons. The layout persists across sessions.

---

## Getting Started

### Prerequisites

- Windows 10 or Windows 11
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Optional, for AI features: an Aria service, an Ollama installation, or an API key for Claude, Gemini, or OpenAI

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

Nexaflow is split into three layers with strict dependency rules:

- **Shell** (`Nexaflow.Core`) — window chrome, tab strip, ribbon bar, breadcrumb navigation, AI input bar. Hosts tabs but never renders them directly.
- **Features** (`Nexaflow.Features.*`) — individual tab implementations. Each feature depends only on the shared contracts in `Nexaflow.Features.Common` and never on Core or other features.
- **Providers** (`Nexaflow.Providers.*`) — LLM provider adapters (Aria, Claude, Gemini, Ollama, OpenAI). Independent of features; wired into the shell at startup.
- **Shared libraries** — non-contract code features reuse: `Nexaflow.Visuals.*` (WPF controls, converters, size/duration formatters, markdown rendering) and `Nexaflow.IO.Common` (encoding/BOM detection, file-change watching).

For a deep dive into the architecture and how to add new features see [docs/Architecture.md](docs/Architecture.md) and [docs/features.md](docs/features.md).

---

## Configuration

| Location | Purpose |
|---|---|
| `%APPDATA%\Smile\Nexaflow\` | Configuration, saved chats and other non-file content |

---

## Roadmap

- [ ] **Local native models** — ONNX / DirectML in-process inference
- [ ] **Syntax-highlighted code editor**
- [ ] **Improved input handling** — multi-modal input (image paste, file drop to AI bar) and richer query routing
- [ ] **Expanded AI capabilities** — per-project memory and inline AI suggestions inside editors
- [ ] **Expanded search with RAG** — semantic search across local files and project content using a local embedding index, retrieval-augmented answers in the AI bar
- [ ] **Multi-monitor awareness and saved workspace layouts**

---

## Contributing

Nexaflow is in active development and welcomes contributions. Please open an issue before starting significant work so we can coordinate.

---

## License

This is free and unencumbered software released into the public domain. See [LICENSE.txt](LICENSE.txt) for details.