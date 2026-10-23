# PDF reader

Shows a PDF's pages, drawn here from the document itself, and lists what the document records about itself.

---

## Opening a document
- Double-click a PDF in the [File System](help:FileSystem) page and it opens here.
- A link that names page 42 opens on page 42, and asking for another page later moves this tab rather than opening a second one.
- A PDF inside an archive opens like any other; the temporary copy for opening it elsewhere is deleted when the tab closes.
- A damaged file — a broken cross-reference table, a font that was never embedded — is recovered by scanning for what survives. A document that opens on an owner password alone opens read-only and is labelled protected.
- When a file cannot be made sense of at all, the reader says so and offers to [open it in your usual PDF application](locate:Pdf_FallbackOpenExternal). Past the size ceiling for reading its properties and contents, the panel says so while the pages are still shown.
- A folder search reads a PDF's words, plus its title, author, subject, keywords, bookmark titles and the values typed into its form. A scan with no text is ruled out rather than left as a maybe, and a document that could not be read is reported as unread rather than as a miss.

## Reading
- [The pages](locate:Pdf_Pages) are drawn as you scroll to them, so a document of any length opens at once.
- [The page box](locate:Pdf_PageNumber) shows the page you are reading, [out of how many](locate:Pdf_PageCount). Type a page number and press Enter to go there, or step with [previous](locate:Pdf_PreviousPage) and [next](locate:Pdf_NextPage). Ctrl+Home and Ctrl+End go to the first and last page.
- [Zoom in](locate:Pdf_ZoomIn) and [out](locate:Pdf_ZoomOut), or Ctrl+wheel to zoom about the pointer; [the zoom](locate:Pdf_Zoom) shows how large the pages are drawn. [Fit the width](locate:Pdf_FitWidth) or [the whole page](locate:Pdf_FitPage) to the window.
- Drag across text to select it — across pages too — double-click to select a word, and Ctrl+C to copy. Ctrl+A selects the whole document.
- Links on a page work: one to another place in the document goes there, one to a web address opens it.
- [Find](locate:Pdf_Find) a word or phrase: Enter goes to the next match, Shift+Enter the previous, and Esc clears the search. Every match is lit on the pages, and [the count](locate:Pdf_FindStatus) says which one you are on; [previous](locate:Pdf_FindPrevious) and [next](locate:Pdf_FindNext) step through them too.
- [Extract the images](locate:Pdf_ExtractImages) the document carries into a folder you pick, one file per picture, as Extract images does in the file browser — from the document as you see it, page edits not yet saved included.
- [Print](locate:Pdf_Print) the document, each page as large as fits the sheet. [Open the file in your usual PDF application](locate:Pdf_OpenExternal) for anything this does not do.
- Layers the document hides are not drawn, and form fields and stamps show as the document draws them.

## Pages
- [The Pages tab](locate:Pdf_Tab_Pages,Pdf_Thumbnails) shows every page as a small picture, in the order the document now has; the page you are reading is outlined, and clicking a page goes there. Ctrl+click and Shift+click select several.
- Drag pages into a new order — a line shows where they will land, and the list scrolls when you drag to its edge — or move the selected pages up and down a place with Alt+Up and Alt+Down.
- Right-click a page to delete it, or the pages selected, or press Delete. A document keeps at least one page.
- A sheet printed with two or more pages on it — a programme, a folded leaflet — can be split into its pages: select the sheets, right-click and choose Split pages, then pick [how many pages each holds](locate:Pdf_Split_Parts) and [how they sit on it](locate:Pdf_Split_Direction) — side by side, one above another, or across whichever side is longer. The lines it will be cut along are drawn on the pages; a line crossing the artwork — a picture that runs over the fold — is drawn solid in the warning colour and the page named, so you can check it before you [split](locate:Pdf_Sheet_Apply) or [cancel](locate:Pdf_Sheet_Cancel).
- Sheets printed for a stapled booklet hold the last page beside the first, the second beside the second-last, and so on. [Unfold as a booklet](locate:Pdf_Split_Booklet) puts their halves into reading order. Each page split off keeps exactly what the sheet showed there, its links, notes and tags going with it, so nothing on the sheet is redrawn or lost.
- Pages can go back onto sheets too: select two or more, right-click and choose Merge pages, then pick [how many go on each sheet](locate:Pdf_Merge_Parts) and [how they sit on it](locate:Pdf_Merge_Direction) — side by side, one above another, or along their shorter side, so upright pages go side by side. Each page sits in the middle of a space the size of the largest, and a last sheet short of pages keeps its empty spaces.
- [Fold into a booklet](locate:Pdf_Merge_Booklet) takes the pages in reading order and puts them on a stapled booklet's sheets — the last page beside the first, and so on — adding blank pages at the end to fill the last sheet. Halves split from the same sheet and put back where they were become that sheet again, so unfolding a booklet and folding it back gives you the file you started with.
- A sheet of merged pages is [as large as its pages together](locate:Pdf_Merge_Paper), nothing made smaller — or on the pages' own paper, or a paper you name, the pages scaled down to share it: two A4 pages become A5 side by side on an A4 sheet, turned to suit.
- Right-click a page and choose Set page size to put it on [other paper](locate:Pdf_Size_Paper) — a named size, or one of your own in millimetres or inches — [whichever way up fits](locate:Pdf_Size_Orientation), its content [scaled to fit](locate:Pdf_Size_Fit) or kept its size, with a margin around it or its edges cut off.
- [Split, merge or set the size of every page at once](locate:Pdf_SheetEdit) from the toolbar, choosing which [here](locate:Pdf_Sheet_Edit).
- Right-click between two pages to insert a picture or another PDF there, or right-click a page to insert before or after it. A picture becomes a page the size of the page beside it, the picture fitted to it and turned the right way up; a PDF brings all its pages, with its bookmarks, form fields and page labels.
- Edits change the document on screen straight away, but not the file: [undo](locate:Pdf_Undo) and [redo](locate:Pdf_Redo) them with Ctrl+Z and Ctrl+Y, and [save](locate:Pdf_Save) them with Ctrl+S, or [save as another file](locate:Pdf_SaveAs) with Ctrl+Shift+S. The tab's title is marked with * while there are edits to save.
- Saving keeps everything the document carries beyond its pages — its bookmarks, form, page labels, named places and attachments, its tags for screen readers, article threads and layers — following the pages to their new places. Only what was on a page you deleted goes with it.
- A document that is encrypted or signed is asked about before it is saved: the file written is not encrypted, and its signatures no longer hold. A document whose form is an XFA form lays itself out, so its pages cannot be rearranged.
- While there are edits, the file and any PDF inserted are kept from being changed by other programs, so the edits always apply to the file as it was read.

## Properties
- [The Properties tab](locate:Pdf_Tab_Properties,Pdf_Properties) reads the document's catalogue, so it fills in while the pages are still being drawn.
- It lists file name and size, page count, PDF version, whether the document is protected, and how many fields its form carries.
- It also lists title, author, subject, keywords, creator, producer, and the created and modified dates — whichever of them the document states. Fields the document leaves empty are left out.
- Every value is selectable and copyable, and right-click copies the whole set.

## Contents
- [The Contents tab](locate:Pdf_Tab_Contents,Pdf_Contents) lists the document's own bookmarks, indented, each with the page it starts on.
- Click a row to move the reader to it — to the heading's position down the page where the bookmark names one.
- A grouping bookmark with no destination of its own, such as "Part II" over its chapters, stays in the list.
- The row you picked stays lit while you read on.
- Right-click to copy a title, a title with its page, or the whole outline with its indentation and page numbers.
- Most PDFs carry no bookmarks. Where there is no outline the tab says so.

## Converting to Markdown
- [Convert to Markdown](locate:Pdf_ConvertDocument) reads the whole document on this PC with page reading, a few pages at a time, saying how far it has got, and opens the Markdown in the Markdown editor in the right-hand pane: each page's columns as one text, its headings, formulas as LaTeX, tables, and its figures cut out of it. A figure the workspace's image model can draw again as a diagram comes back as one. [Show me](locate:Pdf_ConvertDocument)
- Each page is read as the reader draws it, so a scan is read in its own colours however it is stored, and a page with text of its own is read with its layout.
- [A line above the document](locate:Pdf_ConvertScans) says when pages of it are scans — pictures, with no text in them to search or copy — and offers the same conversion. ✕ puts it away for the document; while a conversion is under way it steps aside for the conversion's own line.
- While it reads, a tinted line above the document says **Converting to Markdown**, the page it is on, and about how long the rest should take, worked out from the pages read so far. Reading takes a few seconds a page, so a long document takes a while.
- **Stop** asks first, and the reading goes on while it does: **Show what's converted** stops and opens what has been read so far as a draft, **Discard** stops and lets it go, and **Keep converting** carries on. [Show me](locate:PageConversion_Stop)
- A finished reading is kept, so converting the document again opens at once; one stopped part-way is not. The Markdown is a draft: its first save asks where to keep it, as a Markdown Package unless you pick a Markdown file.
- The first time, page reading is a download of about 4 GB, offered above the document.

## The panel
- [Hide the panel](locate:Pdf_TogglePanel) and the pages take the whole tab.
- [Drag the divider](locate:Pdf_Splitter) to set its width; it comes back that wide next time you show it.

## With the assistant
- It knows which document you have open before it reads a page: its name, how long it is, the page you are reading, whether it has a contents list, and its title and author where the document states them.
- It can read any page range in reading order, page by page; a two-column page is read down one column and then the other. A long stretch that runs into its budget names the page it stopped on.
- It can find a phrase across the whole document and report the pages it appears on with a snippet of each, and can jump by the contents list straight to a section.
- It can list what images a page holds — pixel size, format, whether an image repeats one seen earlier — and then fetch only the one worth looking at.
- It can read a scanned page as a picture at its own resolution, and look at any page drawn as the reader draws it.
- It can read the scanned pages into text with page reading; from then on it reads and searches them like any other page.
- It can move your reader to a page.
- All of it is read-only and none of it stops to ask permission. Two documents open at once stay two separate documents.

## Extracting images
- **Extract images** from a PDF's actions in the file browser writes the pictures out of one document, or of a whole selection at once.
- Each document gets its own folder, and the file names sort into page order.
- JPEG and JPEG 2000 images come out untouched; everything else is decoded to PNG. Bilevel and JPEG 2000 scans are decoded here, so a scanned document does yield images.
- Repeated images are recognised by content and written once. Everything skipped — repeats, anything that could not be decoded, any document that would not open — is counted back to you.
- The work belongs to the shell: close the file browser and it still finishes and still reports how it went.
