# What's New in Nexaflow

Welcome to **Nexaflow 1.6** 🎉

This release is about *working inside* the things you open — and seeing what a long job is doing.

### 🧮 Formulas you can edit
*   A formula in a markdown document used to be a picture. Now there's a **caret in it**: click into it and type, and it re-typesets as you go.
*   An argument you haven't written yet shows as a **hole you can aim at**, so a half-finished formula is something you can carry on with rather than something that disappears.
*   Select part of one and a palette key **wraps what you picked**. Drag a term somewhere else and it's braced for you. A bracket is taken as the pair it can only mean anything as.
*   What can't be read wears a **wave** instead of vanishing, so you can see where it went wrong rather than guessing.
*   Paste a formula in from anywhere and it's read the same way as one you typed.

### 🔳 QR codes and barcodes, written as a block
*   A `qr` fenced block becomes a **QR code** — text, a URL, email, phone, SMS, wifi, a vCard or MECARD, a location, a calendar event, a GiroCode, or a crypto address, each built the way its scanners expect.
*   A `barcode` block covers **twenty-three formats**: Code 128, the EAN/UPC family and its add-ons, Code 39, ITF, MSI, pharmacode, codabar, and the ISBN/ISSN/ISMN schemes — each with its own check digit worked out for you.
*   Their own fences too: **`datamatrix`** for the industry formats that exist only as a Data Matrix symbol, **`pdf417`** for licences, boarding passes and shipping labels, and **`aztec`** for rail and air tickets — in both the compact and full-range families.
*   The value is **editable in place**: change it in the document and the symbol redraws.

### 📐 More diagrams that draw themselves
*   **C4 architecture diagrams** — context, container, component, dynamic and deployment — taking the full C4-PlantUML macro set rather than Mermaid's subset, with boundaries, styling and a legend, plus **C4 sequence** diagrams.
*   Mermaid **timeline**, **journey** and **block** diagrams now render instead of dropping you back to the raw source.

### 🔠 Text at the size you want it
*   One **Text Size** in Options, with a live preview, sets the base size for text content everywhere — open tabs re-lay out as soon as you apply it.
*   On top of that each viewer has its **own zoom**: Ctrl+wheel, Ctrl+plus/minus, Ctrl+0, or the presets in the status bar. Markdown, text, code and hex all share it.
*   Markdown gains a **footer** with word and line counts, an unsaved marker, and that zoom control.

### 📁 Long file jobs, out in the open
*   A **file operations panel** above the folder tree shows what a copy, move or delete is actually doing: the file it's on, a progress bar, throughput, an ETA, and a cancel button. Quick jobs never make the tree jump.
*   If the destination runs out of space it offers to **retry** rather than simply failing.
*   Dropping a big folder in no longer stalls the browser while it copies, and **zipping a selection** now does the whole selection at once instead of one folder at a time.

### 🧪 A Solver tab — early days
*   A new **Solver**: type or paste an expression and it offers what it can make of it — evaluation, algebra, calculus, and descriptive statistics over a pasted series of numbers.
*   A symbol navigator, a palette that remembers the keys you actually use, and results you can reuse as the next definition.
*   This one is **early and still rough** — it's in your hands so it can grow with the use it gets.

### 🎨 Quieter, and easier on a laptop
*   A new **Arctic** theme.
*   Animated backdrops can be **stopped while you're on battery** (Options ▸ Shell), and unplugging takes effect straight away.
*   Those backdrops no longer redraw their artwork every frame, and an idle window no longer re-renders thirty-five times a second just to blink a caret.

### ⚡ Smaller things you'll notice
*   A colour written in source — `#FF3B30`, `rgb(…)`, a named colour, a theme key — is **drawn underneath the text that names it**, in XAML, CSS and HTML alike.
*   You can **drop text straight into** a markdown document.
*   Switching tabs no longer repeats work the page had already done.
*   If something does go wrong, Nexaflow now keeps a **crash log** — one file a day, kept for ten days — so a problem can be reported with something in hand.

Thank you for being part of the journey. Press **Next** to continue.
