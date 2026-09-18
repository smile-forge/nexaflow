# AI Capabilities Registry

This registry is maintained by the AI to track known syntax, block types, and rendering primitives available within the Nexafflow Markdown renderer.

## 🔳 Custom Block Types

| Block Tag | Purpose | Syntax/Example | Source |
| :--- | :--- | :--- | :--- |
| 'qr' | QR Code Generation | 'type: url\nurl: https://...\nec: M\ncellSize: 4' | `markdownsupport.md` |
| 'barcode' | Linear Barcodes | 'format: CODE128\nwidth: 2' | `markdownsupport.md` |
| 'datamatrix' | 2D Matrix | 'type: gs1\n...' | `markdownsupport.md` |
| 'pdf417' | Stacked 1D | '...' | `markdownsupport.md` |
| 'aztec' | 2D Aztec | 'format: compact' | `markdownsupport.md` |
| 'math' | LaTeX Mathematics | '$$ E=mc^2 $$
| 'diagram' | Diagrams (Mermaid, etc.) | 'mermaid\ngraph TD...'
| 'music' | Musical Notation (ABC, Lilypond) | '#%abc ... #%' or '#%lilypond ... #%'
| 'alert' | GitHub-style Callouts | '> [!NOTE]'
| 'figures' | Figure with Caption | '^^^\nCaption'

## 🖋️ Inline Formatting

| Syntax | Effect | Example |
| :--- | :--- | :--- |
| '==text==' | Highlight | '==important==' |
| '~text~' | Subparam/Subpart | '~subscript~' |
| '^text^' | Superscript | '^superscript^' |
| '~~text~~' | Strikethrough | '~~deleted~~' |
| '++text++' | Underline | '++underlined++' |
| ''text'' | Citation | ''source'' |

## 🛠️ Usage Instructions
- **Discovery:** If a block type is not found in this registry, the AI should search docs/ for documentation or attempt to 'probe' the renderer with a basic example.