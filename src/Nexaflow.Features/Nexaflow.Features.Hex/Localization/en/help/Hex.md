# Hex Editor

Shows the bytes in a file and lets you change them.

---

## Moving around
- Go to an offset: type it into the Go to box in hex, with or without `0x`, and press Enter. Ctrl+G puts you in the box. [Show me](locate:Hex_Goto,Hex_GotoGo)
- Type something that isn't hex and it is read as decimal. An offset past the end of the file lands on the last byte.
- Move by byte or row with the arrow keys, by screen with Page Up and Page Down, and to either end of a row with Home and End. Hold Shift to select as you go.
- Click to place the cursor, drag to select, Shift+click to extend a selection. Click an address to jump to the first byte of that row.
- Zoom with Ctrl+mouse wheel, Ctrl+plus or Ctrl+minus, or by clicking the percentage in the status bar. Ctrl+0 resets it.
- The status bar shows the file size and the cursor offset in hex and decimal, or, with a selection, its size, start and end.

## Reading the text
- Auto decoding reads the byte-order mark — UTF-8, or UTF-16 either way round — and otherwise uses ASCII. Pick ASCII, UTF-8 or UTF-16 in the toolbar to choose yourself.
- Bytes that have no printable character show as a dot.
- Show or hide the text evaluation pane with the ¶ button, or drag the splitter to resize it. [Show me](locate:Hex_EvalPane)

## Editing
- Files open read-only. To edit, pick INS or OVR. [Show me](locate:Hex_ModeReadOnly,Hex_EditMode,Hex_ModeOverwrite)
- Type two hex digits to set a byte.
- OVR replaces bytes and keeps the file the same size. INS pushes the rest of the file along. Delete and Backspace remove bytes.
- Undo with Ctrl+Z, redo with Ctrl+Y. [Show me](locate:Hex_Undo,Hex_Redo)
- Undo every edit and the file counts as unchanged again.

## Saving
- Nothing is written to the file until you save.
- Save with Ctrl+S. It is available only once there is something to save, and the arrow beside it offers Save As… to write a copy under a new name. [Show me](locate:Hex_Save)
- Saving over the original writes a complete new copy first and swaps it in only once it is whole. If that fails you are told, the part-written copy is removed, and the original is left as it was.

## Opening files
- In the [File System](help:FileSystem) page, As Hex is in every file's actions, and it is what opens a file no other viewer claims.
- In the [PE inspector](help:Executable), View in hex opens the file here with those bytes selected and in view.
- A file inside a zip archive opens like any other, and Save rebuilds the zip around your change.
- A very large file opens without being read into memory whole.
- Opening the same file again re-points [this tab](locate:TabItem_Hex) rather than adding another, and unsaved edits stay put.
- If a read fails you are told why — the file was moved, renamed or deleted, Windows refused access, another program has it open and won't share it, or it stopped returning data part-way — and offered Retry or Close tab.

## With the assistant
- Ask it to read a range anywhere in the file, on screen or not, a few kilobytes at a time, or to read the bytes you have selected.
- Ask it to find a signature such as `DE AD BE EF`, or a piece of text.
- Ask it to move the cursor, select a range, or change the decoding.
- With your approval it can overwrite bytes in place, never changing the file's length, and save. Its edits join your undo history, and nothing is written until a save you make or approve.
