# Text viewer

The Text viewer opens any text file — logs, configuration, data, source you just want to read — quickly, however
big it is, and lets you search it, edit it and even split it into smaller files.

---

## Opening files

Double-click a text file in the File System page. For a file that opens somewhere else by default, choose the Text
viewer from the file's actions.

Large files open straight away: the viewer shows the start of the file at once and reads the rest as you scroll,
with a full-length scrollbar from the first moment. While it is still reading, the status bar says **Streaming**.

## Reading comfortably

The toolbar keeps the everyday switches together:

- **Line numbers**, on or off.
- **Word wrap**, on or off, for long lines.
- **Encoding.** If characters look wrong, read the file again as UTF-8, UTF-16 (little- or big-endian), Latin-1 or
  your system's default.
- **Go to line.** Press **Ctrl+G**, type a number, and the view moves there.
- **Zoom** from the footer, or with **Ctrl**+mouse wheel, **Ctrl+plus** and **Ctrl+minus**; **Ctrl+0** resets it.
  Zoom scales on top of the text size set in Options.

The status bar shows the file's size and how many lines it has.

## Watching a file change

Turn on file monitoring from the toolbar and the viewer reloads the file whenever it changes on disk — follow a log
as it grows. A banner at the top tells you the file has changed.

## Finding and replacing

The find bar searches the whole file, not just what is on screen:

- Every match is highlighted, and a strip down the right edge marks where the matches fall in the whole file.
- **Next** and **Previous** step through them; the status bar counts them.
- **Match case** and **Regex** (`.*`) change how the search matches.
- **Replace** changes the current match and **Replace all** every one; the viewer switches to editing to do it.

You can also type `?` and a word in the AI bar to search the file.

## Editing

Files open read-only, so nothing changes by accident. Press **Edit** to start editing — the button then reads
**Editing** — and **Save** writes your changes back. Cut, paste, undo and redo work as you would expect.

## Splitting a big file

**Split** opens a panel for cutting a large file into smaller ones, by size or by number of lines. Choose how, then
**Split now**.

## Asking the assistant

The assistant can see what you are looking at. It can read any range of lines and find text — and, with your
approval, edit lines, replace text and save the file.
