# Scratchpad

A board of post-it notes you can drop files, links, pictures and text onto.

---

## Making notes

- Drop an image file and it is copied into the note and embedded, and the note sizes itself to the picture.
- Drop any other file, or a folder, and it becomes a link. Clicking it opens the item in Nexaflow, or in Windows if no page here handles it.
- Drag a picture out of a browser with no file behind it and it is saved as a PNG and embedded.
- Drop an http or https address and it shows as a link straight away, then fills in with the page's title, description and preview image, downloaded locally so the note still works offline.
- Drop text or markdown and it becomes the body of the note.
- Drop several files at once and they land in a cascade. Ctrl+V pastes the same way, and right-clicking empty board offers "New post-it here" and "Paste as post-it" at the cursor.
- Add a blank note in the middle of the view, already in edit mode. [Show me](locate:Scratchpad_AddNote)

## Working with a note

- A note's body is markdown. The block your caret is in shows its source while the rest stays rendered, and you can select and copy straight across the whole note.
- Right-click a note's header for a strip of options above it: seven papers (Yellow, Blue, Green, Pink, Orange, Purple, White), four shapes (Square, Rounded, Diagonals, Speech bubble), and Front and Back to restack it.
- Drag the header to move a note. Dragging or resizing one also brings it to the front.
- Resize from any edge or corner, and rotate about the centre with the handle at the left of the header. A tilted note resizes along its own tilt, nothing goes below 80 pixels, and new notes arrive at a random tilt of up to eight degrees.

## Moving around the board

- The board has no edges. Pan with the middle button, or with a left drag after a short dead zone so a plain click still selects. A drag that starts on a note's header or grip moves the note instead. [Show me](locate:Scratchpad_Canvas)
- Zoom with the scroll wheel about the pointer, or with Ctrl and the numeric keypad's plus or minus. Ctrl+0 returns to 100%.
- Fit rescales and recentres so every note is on screen. [Show me](locate:Scratchpad_ZoomToFit)
- Click the zoom percentage in the status bar for 50, 75, 100, 125 and 150% presets, applied about the centre of the view. [Show me](locate:Scratchpad_ZoomLabel)
- An overview map appears in the top-left corner when notes have drifted off screen. Drag on it to go there.

## Expiry and the recycle bin

- An unpinned note counts down in its header and goes to the recycle bin when the time runs out.
- Click the countdown to pin the note. A pinned note does not expire; unpinning starts a fresh countdown.
- Set the lifetime a new note starts with in Options, from 30 minutes to 24 hours. The default is two hours.
- The close button on a note's header moves it to the recycle bin. [Show me](locate:Scratchpad_RecycleBin)
- Each binned note is listed by its first non-empty line. Restore puts it back on the board, unpinned and with its images; Delete removes it for good.
- Empty the bin in one go from the dropdown beside the button. It asks first, because nothing there is recoverable afterwards. [Show me](locate:Scratchpad_BinOptions)
- Set how long the bin keeps notes in Options, from one day to Infinite. The default is 30 days, and older notes are purged when Nexaflow starts.

## Search and the assistant

- Type `?` and a word in the AI bar to search the board. Notes that miss are hidden, matched words are marked in the bodies that remain, and the board pans to each hit in reading order without changing the zoom. `note*` matches a prefix and `*note*` a substring, and the recycle bin is not searched.
- The assistant is told how many notes there are and how they break down by colour. It can list your notes with their colour, shape, position and a one-line preview, read their markdown, and add a note — filtered by colour or shape if you ask it that way. It cannot edit, delete, recolour, move, pin or restore anything. Notes it writes are white and diagonally rounded.

## Where notes are kept

- Each note is a file on disk, saved a moment after you stop typing, with a folder of its own for dropped images and downloaded previews. That folder follows the note into the bin and back out, and goes only when the note is deleted for good.
- The Scratchpad is stored with the app rather than with a workspace, so the same notes are there whichever workspace you switch to.
