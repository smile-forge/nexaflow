# Nexaflow

**Your whole Windows workflow — in one window, with AI built in.**

Nexaflow replaces the scattered pile of single-purpose apps you open every day — file explorer, terminal, editors, viewers, project tracker — with one fast, tabbed workspace. And it has an AI assistant that actually *sees* what you're working on, so help is always one question away.

![Version 1.2.0](https://img.shields.io/badge/version-1.2.0-brightgreen)
![License: Unlicense](https://img.shields.io/badge/license-Unlicense-blue.svg)
![Platform: Windows](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)

---

## Why you'll like it

The default Windows experience hasn't really changed in decades. Explorer, Notepad, and the terminal are separate islands — no shared context, no intelligence, constant alt-tabbing. Nexaflow brings them together as **tabs in one coherent workspace**, then adds an AI layer that understands the tab you're looking at and the project you're in.

- **One window, no juggling.** Open a file and it lands in the right tool automatically — markdown opens in the editor, images in the viewer, a folder in the explorer.
- **An assistant that has context.** Ask a question in the AI bar at the bottom of any window. It knows what's on screen and can take action for you, with your approval.
- **Fast, even with big files.** Multi-gigabyte logs, JSON, and data tables open instantly thanks to smart windowed loading.
- **Yours to shape.** A customisable ribbon, themes, and saved workspaces that persist across sessions.
- **Free and open.** Public-domain licensed. No accounts, no telemetry, no lock-in.

---

## What's inside

**📂 Files & search**
A built-in file tree, full-text Windows Search with AI query refinement, and folder "viewlets" that recognise Git repos and .NET projects — feeding that context straight to the assistant.

**📝 Editors & viewers**
A live Markdown editor (LaTeX formulas *and* native Mermaid diagrams), a syntax-highlighting **code editor** with code folding and a class/structure map, a quick text editor, and a Jupyter **notebook** viewer — plus dedicated viewers for images (carousel / album / explore / collage), logs, JSON, CSV/TSV tables, hex/binary, and web pages.

**🎬 Media & 3D**
An **audio player** with spectrum/waveform visualisations, `.lrc` lyrics, and ID3 tag editing; an interactive **3D model viewer** (STL, OBJ, glTF/glb, FBX, and more); and native **video playback**.

**📦 Archives, in place**
Browse `.zip`, `.7z`, `.tar`, `.rar`, and modern `zstd`/`lz4` archives **like folders** in the file tree — open files inside them in their normal viewers, edit and write changes straight back, and zip/unzip from the right-click menu.

**⌨️ Terminal & system tools**
A real terminal (PTY) with shell integration, a live Process Explorer, an installed-apps manager, a system-info dashboard, and a registry editor — with elevated actions handled safely through a privilege bridge.

**🗂️ Stay organised**
Lightweight project tracking with a backlog workflow, a **Product Manager** for status-tracking a product tree, a virtual corkboard Scratchpad for notes, and multiple workspaces you can switch between.

**🤖 AI everywhere**
An AI input bar on every window, a dedicated AI Chat tab with saved history, and agentic, tool-using turns that always ask before doing anything that changes your files. Bring your own provider — Claude, Google Gemini, OpenAI, local models via Ollama, or an Aria service — and assign different models to different tasks.

---

## Get Nexaflow

**[⬇️ Download the latest release](https://github.com/smile-forge/nexaflow/releases/latest)**

You'll need **Windows 10 or 11**. AI features are optional and work with whichever provider you choose — a local model via Ollama, or an API key for Claude, Gemini, or OpenAI.

On first launch, Nexaflow opens its Options panel so you can pick an AI provider and set your projects folder. That's it — start opening tabs.

---

## For developers

Nexaflow is a .NET 10 / WPF app built on a strict, modular architecture — a thin shell that hosts independent feature and provider modules. Building from source is two commands:

```bash
git clone https://github.com/smile-forge/nexaflow.git
cd nexaflow
dotnet build Nexaflow.slnx
dotnet run --project src/Nexaflow.Core/Nexaflow.Core.csproj
```

If you're interested in how it all fits together — the shell/feature/provider layering, how to add a new tab, the theming system, and testing — start here:

- [docs/Architecture.md](docs/Architecture.md) — the big picture, ownership & lifetime, dependency rules
- [docs/features.md](docs/features.md) — how a feature is built and registered
- [docs/theming.md](docs/theming.md) — the layered theme system
- [docs/testing.md](docs/testing.md) — the test projects and how to run them

---

## Roadmap

- [ ] **Local native models** — ONNX / DirectML in-process inference
- [ ] **Richer input** — image paste and file-drop straight to the AI bar
- [ ] **Expanded AI** — per-project memory and inline suggestions inside editors
- [ ] **Semantic search (RAG)** — a local embedding index for retrieval-augmented answers
- [ ] **Multi-monitor awareness and saved workspace layouts**

---

## Contributing & community

Contributions are welcome! Please read:

- [CONTRIBUTING.md](CONTRIBUTING.md) — how to get set up and submit changes
- [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) — the standards we hold the community to
- [SECURITY.md](SECURITY.md) — how to report a security concern

The short version: open an issue before starting significant work so we can coordinate.

---

## License

Nexaflow is free and unencumbered software released into the **public domain**. Use it, modify it, ship it — for any purpose. See [LICENSE.txt](LICENSE.txt) for details.
