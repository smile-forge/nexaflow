# What's New in Nexaflow

Welcome to **Nexaflow 1.5** 🎉

This release is about *finding things*, and *seeing inside* the ones you find.

### 🔎 Ask any page "?"
*   Type `?` in the input bar and you search **whatever you're looking at** — a log, a spreadsheet, a JSON file, a notebook, a registry hive, your processes, a conversation, your product tree. Nearly thirty pages answer it themselves, each in the way that suits it.
*   `?/pattern/i` is a **regular expression** anywhere — on pages whose backend has never heard of one too.
*   In a folder, `?` uses the **Windows index** and takes Windows' own property syntax. `?*.log` narrows by name; `?"a phrase"` looks inside the files.
*   When a location isn't indexed — or the index comes back empty — Nexaflow **offers to read the files itself**, streaming hits as it walks. A file it can't settle from the name alone is marked with a `?` until it's been read, then ticked or struck through.
*   Your assistant gets the same search, so you can ask for "the invoices that mention Fenwick" and it can search broadly, then show you only what survived.

### 📕 PDFs, read properly
*   `.pdf` files now **open in Nexaflow** instead of handing you to Acrobat — a clean reading surface beside the document's own properties and contents, all of it selectable.
*   Your assistant can genuinely **read the document**: outline, page-ranged text, search, the images inside it, and any page as a picture — so even a scanned PDF, which is really just pages of pictures, can be asked about.

### 🩻 Medical images, text editing, and 🧩 what's inside a binary
*   **DICOM viewer**: open a `DICOMDIR`, a folder of instances or a single `.dcm` — scroll through a series, invert, apply window presets, and open a resizable tag drawer. It reads from inside a zip or an ISO too.
*   The **text editor** gains Notepad parity and then some: Find & Replace (Ctrl+F / Ctrl+H), go-to-line (Ctrl+G), multi-level undo/redo, zoom by Ctrl+wheel, and a `*` on the tab when there's something unsaved.
*   **Inspect** any `.exe`, `.dll` or `.sys` and see what it actually is — headers, sections, imports and exports, with the import tree drawn as a diagram. Double-clicking still just runs it.

### 🗂️ Places, apps and files
*   A new **Network** tab discovers what's on your segments and asks each device to say what it is - local arp and SSDP requests only for the moment.
*   **OneDrive folders are reachable from This PC** without you knowing where they live on disk, and a contributed location behaves like a real place rather than a shortcut.
*   **Installed apps** gain most of what Windows actually offers: *Modify* for the programs that support it, and for Store apps a *Move*, background-execution control, Repair, Reset — which tells you plainly that it deletes your data — and add-ons you can remove one at a time.
*   **Logs** read JSON logs as JSON.

### ⚡ Smaller things you'll feel
*   Browsing folders got noticeably **quicker** — the file tree no longer asks the disk about every subfolder while you're waiting for it.
*   A closed **Web** tab no longer leaves a browser process running behind it, and the rendering surface is now shared, which is what let PDFs have one.
*   The `nfi` command-line tool can be installed onto your PATH from the installer's optional **Command-line tools** feature.

Plus various bug fixes, improved diagram rendering, theming corrections and adjustments including more intelligences for your assistant.

Thank you for being part of the journey. Press **Next** to continue.
