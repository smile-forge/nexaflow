# Font viewer

Sets your own text in several fonts at once, and shows what each font contains.

---

## Preview text and size

- Type the text every row is set in: replace the sentence in the preview box. Rows redraw as you type. [Show me](locate:FontPreviewInput,FontSizeSlider)
- Change the size for all rows with the size slider: 8 to 120 pt in whole points, starting at 16.
- Each row also sets a short line of letters, digits and symbols under your text.

## Adding and removing fonts

- Add an installed family: **Add font**, then pick from the list. Type to narrow it. [Show me](locate:FontAddButton)
- Add a font file: **Add font**, then **Load from file…**, and choose a `.ttf`, `.otf`, `.ttc` or `.woff`.
- Nothing is installed and nothing is uninstalled. The page never writes to the Windows font store; a font file is read where it sits, and the remove control on a row only drops that row from the comparison. [Show me](locate:FontRemoveButton)
- A `.ttc` holding several families adds one row per family.
- A `.woff` is decoded to an ordinary font first and labelled *WOFF → decoded*. The decoded copy is deleted when the tab closes.
- A font inside an archive or a disk image is copied to a real file first, then opened.
- A file that will not load stays in the list with the reason in place of its preview.

## Faces

- Pick a face for a row — Regular, Bold, Italic, Condensed, or whatever else the family ships — by clicking its chip. The row, the details and the glyph map all switch to it.
- Each row keeps its own face, so you can compare one family's bold with another's italic.
- Only faces that are in the file are offered; there is no fake bold or fake slant.

## Font details

The details panel shows the selected face. [Show me](locate:FontDetailsPanel)

- Identity: family, face, source, format, how many faces the file holds, and the designer's sample text if the font carries one.
- Style and metrics: weight with its OpenType number, style, stretch, whether it is a symbol font, glyph and mapped-character counts, and cap height, x-height, baseline, underline and strikethrough in ems.
- Legal and licensing: copyright, trademark, licence text, embedding rights, and manufacturer, designer and vendor links.
- Technical: version, file location and description.
- Copy either identifier with **Copy name** or **Copy path**. Copy path appears only for fonts that came from a file.

## Glyph map

- See every character the face maps: expand the glyph map under the details. [Show me](locate:FontGlyphMap)
- Page through with Prev and Next; 500 characters a page, with the range and total shown.
- Copy a character: right-click its glyph. Control codes and surrogate halves are not listed.

## Elsewhere in the app

- Open a font from your files: double-click a `.ttf`, `.otf`, `.ttc` or `.woff` in the [File System](help:FileSystem) page, or choose **As Font** from the file's actions. Select several and they open in one tab.
- Find a font in your list by name: type `?` and a word in the ask box. Matches are highlighted and the selection steps through them from a counter beside the size slider. No rows are hidden.
- The assistant knows every font in the list, which is selected, and your preview text and size. It can read any listed font's details and render any of them to an image, and it highlights the fonts it refers to.
- Pin the tab to a conversation and the chip shows each font's identity rows above a sample.
