# Text viewer

Opens a text file of any size, and can search it, edit it and split it into smaller files.

---

## Opening a file

- The file action is called As Text, and the tab takes the file's name. Files inside an archive open the same way.
- A file of 100 KB or more is read from the top a window at a time. The line count is right from the start, lines you have not scrolled to yet are blank until the viewer reaches them, and the status bar reads Streaming.
- The encoding is detected when the file opens. If characters look wrong, re-read the file as UTF-8, UTF-16 LE, UTF-16 BE, Latin-1 or System Default. [Show me](locate:Text_Encoding)

## Moving around

- Go to a line by pressing Ctrl+G and typing the number. [Show me](locate:Text_GoToLine)
- Line numbers and word wrap are toggles. [Show me](locate:Text_LineNumbers,Text_WordWrap)
- Zoom the text pane with Ctrl and the mouse wheel, or Ctrl+plus and Ctrl+minus; Ctrl+0 resets it. Zoom multiplies the text size set in Options.
- The status bar shows the file's size and its line count.

## Finding and replacing

- Ctrl+F opens the find bar and Ctrl+H opens it with the replace row. A single-line selection in the text pane starts in the find box. [Show me](locate:Text_Find,Text_FindBox)
- The search covers the whole file, not only the part on screen. The match count is in the bar and in the status bar, and every match is marked down the strip at the right edge.
- F3 or Enter goes to the next match, Shift+F3 or Shift+Enter to the previous. Esc from the find box closes the bar.
- With `*?` on, which is how it starts, `*` stands for a run of characters, `?` for one, and a plain word matches as a word rather than inside a longer one. `.*` runs the query as a regular expression instead, and `Aa` makes it case-sensitive. [Show me](locate:Text_MatchCase,Text_UseWildcards,Text_UseRegex)
- Replace and Replace All turn editing on themselves. Replace All works through the whole file, not just the loaded part.

## Editing and saving

- Files open read-only. Edit turns editing on, and the button then reads Editing. [Show me](locate:Text_EditToggle,Text_Save)
- Save with Ctrl+S or the Save button; the file is written in the encoding shown in the encoding box. Until then the tab title carries an asterisk.
- Undo is Ctrl+Z and redo is Ctrl+Y. In a large file, loading a further part of the file drops the undo history.
- A large file inside an archive cannot be edited in place — extract or split it first.

## Watching and splitting

- Monitoring is on when a file opens: a change on disk reloads the file and shows a banner over the text pane. Changes made while you are editing, or while a save is running, are ignored. [Show me](locate:Text_Monitor)
- Split writes the file out as several new files beside it — by line count, by size in MB, or at lines matching a regular expression. The original is left as it is, and the work runs in the background. [Show me](locate:Text_SplitToggle,Text_SplitNow)

## The assistant

- It is told the file's name and path, the encoding, the total line count, whether there are unsaved edits, and the numbered lines currently on screen.
- It can read further into the file than you can see, search it, replace or delete a range of lines, replace across the whole file, and save.
- Anything that changes the file or saves it asks you first.
