# What's New in Nexaflow

Welcome to **Nexaflow 1.1** — our inaugural stable release! 🎉

Here's everything that landed since v0.9.1.

### 🚀 New Features
*   **Process Explorer**: A live process tree with per-process detail tabs (including a dedicated Handles tab), AI tools, and elevated kill / priority actions.
*   **Image Viewer**: A new four-mode viewer — carousel, album, explore, and collage — launched straight from folder actions.
*   **Real Terminal Console**: The console is now a proper terminal with smart command/AI routing, configurable per-location environments (master/detail config with visible defaults), and a shared core ready for a PowerShell sibling.
*   **Workspaces**: A reworked workspace selector lets you create new workspaces and manage identity, deletion, and export right from the Configure panel.
*   **File-Association Wizard**: A "Define New" wizard for mapping file types, plus criteria-based external-app launching.
*   **Code editor**: A new editor with syntax highlighting, code folding and class diagrams

### ✍️ Markdown & Diagrams
The markdown editor now renders a huge range of Mermaid diagrams natively:
*   Kanban boards, class & requirement diagrams, quadrant charts, sequence, gantt, gitgraph, mindmap, and state diagrams.
*   Flowchart chains, fan-out, nested subgraphs, and diamond ports.
*   Richer text: emphasis extras, abbreviations, alerts, and YAML front matter — all on top of the shared inline editor with polished editing UX.

### 🤖 AI & Context Enhancements
*   **Console ↔ AI**: The terminal is wired into the assistant — an inline banner, command routing, and output capture feed straight back to the AI.
*   **Process Insight**: Process Explorer exposes its data to the AI, so you can ask about what's running on your machine.

### 🎨 Look & Feel
*   New **Gothic** dynamic theme.
*   Rename ribbon buttons via right-click and the edit overlay.
*   A new **About** page in Options.

### 🛠️ Improvements & Bug Fixes
*   The file explorer loads folders off the UI thread, so the list stays responsive in large directories.
*   The web tab survives a missing WebView2 runtime and offers to open your default browser instead.
*   Smoother startup window handling, plus new-file creation, refresh, and selection fixes in the file browser.
*   Under the hood: features now marshal UI work through the shell (no direct dispatcher access), theme resources are frozen once merged, and shared file-watching / encoding / formatting utilities cut duplication across the app.

Thank you for being part of our 1.x launch. Press **Next** to continue.
