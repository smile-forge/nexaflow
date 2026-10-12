# Rich text

Shows a rich-text document, an `.rtf` file, laid out as its writer made it.

---

## Opening files
- Double-click an `.rtf` in the [File System](help:FileSystem) page, or pick **As rich text** from the file's actions.
- One document per tab, named after the file, so several can sit side by side in [their own tabs](locate:TabItem_Rtf).
- A document inside an archive or a disk image opens like one on disk — see [Archives](help:Compressed).
- The file is read off the interface thread, so a long document does not freeze the window while it loads.
- A file that does not open the way every RTF document does is not drawn; the page says so instead.
- When the file changes on disk — saved again from a word processor — the page reads it again. While the tab is out of sight it waits, and reads the latest once you come back to it.

## What is shown
- Paragraphs with their alignment, indents, spacing and tab stops; justified text is spread to both margins.
- Fonts, sizes, bold, italic, underline, strike-through, small capitals, superscript and subscript, and the text and highlight colours the writer chose.
- Text the writer left in the default colour, and the page behind it, take the theme's colours.
- Tables, including cells merged across or down and tables inside cells.
- Links, which open where they lead when pressed.
- Pictures held in the document — PNG, JPEG, bitmaps and Windows metafiles — including a picture placed as a drawing shape.
- Symbols inserted from a font such as Wingdings.
- Text in any of the code pages a document can name, so a document written in Greek, Russian, Japanese or Chinese reads as it was written.
- The document is read as one continuous page: a page break is shown as a few blank lines, and headers, footers and footnotes are not shown.

## Reading
- The page only shows the document; it is changed in a word processor.
- Select and copy the words as they read — never the RTF they are written in.
- Ctrl+scroll, or Ctrl and plus or minus, makes the text larger or smaller; Ctrl+0 sets it back.
- The footer counts the document's words and paragraphs, and gives the size of the file.

## Searching
- Search from [the AI bar](locate:AiInputBox) with `?`. The matches light up on the page and their places show in the strip beside it; step through them from the bar that appears above the document.
- The search reads the words the document draws, so searching for a control word such as `par` finds only that word where it is written.
- Searching a folder looks inside rich-text documents too, by the same words.

## With the assistant
- The assistant knows the document's name, how many words and paragraphs it has, and how big it is.
- It reads the words itself, a range of paragraphs at a time, and searches the document to find where something is. Ask it to summarise the document or find what it says about something.
- Reading changes nothing and never waits for your approval.
- Until the file has finished loading the assistant holds back, and it is released even when the load fails.
