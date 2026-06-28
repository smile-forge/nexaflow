# What's New in Nexaflow

Welcome to **Nexaflow 1.2** 🎉

This release is all about opening *more* of your files — sound, 3D, notebooks, archives — right inside the same window, plus a smarter code editor and a new way to track a product.

### 🎬 New media & 3D viewers
*   **Audio player**: A full player tab — playback with a live spectrum + waveform, scrolling `.lrc` lyrics, and an in-place ID3 tag editor. Plays MP3, FLAC, M4A/AAC, WMA, Ogg/Opus, and WAV.
*   **3D model viewer**: Open STL, OBJ, PLY, 3DS, glTF/glb and — via Assimp — FBX, 3MF, AMF, Collada and many more. Rotate / zoom / pan, toggle wireframe, and inspect materials and mesh stats.
*   **Native video playback**: An embedded libVLC player handles the wide range of formats VLC does — with a codec/metadata panel, a scrubbable keyframe scene-strip, and fullscreen.

### 📦 Browse archives like folders
*   **Step into archives in the file tree**: `.zip`, `.7z`, `.tar`, `.rar`, and modern `zstd` / `lz4` containers expand like folders — nesting included.
*   **Open, edit, write back**: Open a file inside an archive in its normal viewer, edit it, and your change is written straight back into the container.
*   **Zip It / Unzip here**: New right-click file and folder actions, plus a dedicated archive inspector tab.

### 📓 Notebooks & a smarter code editor
*   **Jupyter notebooks**: `.ipynb` files render as cells — rendered markdown alongside syntax-highlighted code — with a per-cell code outline.
*   **Richer code intelligence**: The code editor now parses **embedded languages** (JavaScript/CSS in HTML, Ruby in ERB, and more), broadens language coverage, and draws a cleaner code/class map. New code formats open *As Code* by default.

### 🧭 Product Manager
*   A new feature for tracking a **product as a status tree** — a sunburst you can navigate, with cross-cutting concerns, snaplinks, and a status roll-up. Opens from any folder that has (or can start) a `.product/` directory.

### ✍️ More Mermaid diagrams
The markdown renderer adds six more native diagram types — **XY charts**, **radar**, **Ishikawa** (fishbone), **Sankey**, **ER diagrams**, and **Venn** — each drawn in WPF with full front-matter `config:` support. A diagram showcase doc ships alongside.

### 🤖 AI & web
*   **The web tab is now AI-aware and resizable** — ask the assistant about the page you're viewing.
*   Tabular data gains reusable **templates** for common shapes.

### 🛠️ Improvements & under the hood
*   **Config survives updates**: App and feature settings now **migrate forward** across version bumps instead of resetting — your keys, layout, and preferences carry over.
*   File copy/move now reports the **specific** Windows fault instead of a generic error.

Thank you for being part of the journey. Press **Next** to continue.
