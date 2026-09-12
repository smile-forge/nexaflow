# PDF reader

Shows a PDF's pages, and lists what the document records about itself.

---

## Opening a document
- Double-click a PDF in the [File System](help:FileSystem) page and it opens here.
- A link that names page 42 opens on page 42, and asking for another page later moves this tab rather than opening a second one.
- A PDF inside an archive opens like any other; the temporary copy is deleted when the tab closes.
- A damaged file — a broken cross-reference table, a font that was never embedded — is recovered by scanning for what survives. A document that opens on an owner password alone opens read-only and is labelled protected.
- When a file cannot be made sense of at all, or is past the size ceiling for reading its structure, the panel says so while the page view carries on showing the document.
- Page selection, in-page search, zoom and print sit on the reader's own toolbar. [Open the file in your usual PDF application](locate:Pdf_OpenExternal) for anything this does not do.
- A folder search reads a PDF's words, plus its title, author, subject, keywords, bookmark titles and the values typed into its form. A scan with no text is ruled out rather than left as a maybe, and a document that could not be read is reported as unread rather than as a miss.

## Properties
- [The Properties tab](locate:Pdf_Tab_Properties,Pdf_Properties) reads the document's catalogue, so it fills in while the pages are still painting.
- It lists file name and size, page count, PDF version, whether the document is protected, and how many fields its form carries.
- It also lists title, author, subject, keywords, creator, producer, and the created and modified dates — whichever of them the document states. Fields the document leaves empty are left out.
- Every value is selectable and copyable, and right-click copies the whole set.

## Contents
- [The Contents tab](locate:Pdf_Tab_Contents,Pdf_Contents) lists the document's own bookmarks, indented, each with the page it starts on.
- Click a row to move the reader to it — to the heading's position down the page where the bookmark names one and the reader honours it.
- A grouping bookmark with no destination of its own, such as "Part II" over its chapters, stays in the list.
- The row you picked stays lit while you read on.
- Right-click to copy a title, a title with its page, or the whole outline with its indentation and page numbers.
- If the reader turns out to ignore page jumps, the rows stop offering one.
- Most PDFs carry no bookmarks. Where there is no outline the tab says so.

## The panel
- [Hide the panel](locate:Pdf_TogglePanel) and the document takes the whole tab.
- [Drag the divider](locate:Pdf_Splitter) to set its width; it comes back that wide next time you show it.
- If the embedded viewer will not start on this PC, the properties and contents are still listed, with the reason and a button to open the file in your usual reader. Where the cause is a missing Microsoft Edge WebView2 runtime, a link to install it is shown too.

## With the assistant
- It knows which document you have open before it reads a page: its name, how long it is, whether it has a contents list, and its title and author where the document states them.
- It can read any page range in reading order, page by page; a two-column page is read down one column and then the other. A long stretch that runs into its budget names the page it stopped on.
- It can find a phrase across the whole document and report the pages it appears on with a snippet of each, and can jump by the contents list straight to a section.
- It can list what images a page holds — pixel size, format, whether an image repeats one seen earlier — and then fetch only the one worth looking at.
- It can read a scanned page as a picture at its own resolution, and photograph any page as it is rendered.
- It can move your reader to a page, and says so if the reader refuses to move.
- All of it is read-only and none of it stops to ask permission. Two documents open at once stay two separate documents.

## Extracting images
- **Extract images** from a PDF's actions in the file browser writes the pictures out of one document, or of a whole selection at once.
- Each document gets its own folder, and the file names sort into page order.
- JPEG and JPEG 2000 images come out untouched; everything else is decoded to PNG. Bilevel and JPEG 2000 scans are decoded here, so a scanned document does yield images.
- Repeated images are recognised by content and written once. Everything skipped — repeats, anything that could not be decoded, any document that would not open — is counted back to you.
- The work belongs to the shell: close the file browser and it still finishes and still reports how it went.
