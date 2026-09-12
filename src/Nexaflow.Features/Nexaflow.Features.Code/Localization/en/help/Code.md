# Code editor

Shows a source file with syntax colouring and an outline of its structure, and lets you edit and save it.

---

## Opening files
- Double-click a `.cs`, `.ts`, `.py`, `.rb`, `.rs`, `.cpp`, `.java`, `.php` or `.razor` file on the [File System](help:FileSystem) page and it opens here. Any other file — a `.xaml`, a `.css`, a config file — opens here through the As Code action.
- A file too big to edit opens read-only, with a banner pointing at [the Text viewer](help:Text) or at splitting the file. The ceiling is a Code Editor setting: 5 MB to 250 MB, 50 MB by default.
- Open a code snaplink on the Product page and the file opens here, scrolled to the declaration the link named.

## Languages
- Parsed with tree-sitter: C#, JavaScript, TypeScript, Python, Ruby, Rust, C, C++, Java, PHP, JSON, HTML, CSS, ERB, Razor, Jinja, XAML and the XML family, including `.props`, `.targets` and WiX sources.
- PowerShell, SQL, TeX and VB get colouring but no outline.
- Fold and unfold blocks from the fold margin. The markers keep up as you type, without a save.
- A colour literal — `#FF3B30`, an `rgb(…)`, a name like `Tomato`, or a `{StaticResource AccentBrush}` that resolves in the current theme — is drawn with a bar of that colour.
- Embedded code is coloured, folded and mapped with its own grammar: JavaScript and CSS in HTML, Ruby and HTML in an ERB template, the HTML around `<?php … ?>`, Python in Jinja's `{{ … }}` and `{% … %}`, and Ruby heredocs tagged HTML, CSS, JS, JSON or ERB.

## The code map
- Dependencies lists the file's imports. A relative import that resolves to a file on disk — a JavaScript `./util`, a Python `from .util import x`, a Ruby `require_relative`, a XAML `ResourceDictionary` source — is a link that opens that file in its own tab.
- The class diagram has a box per type with its members, base classes and interfaces, and embedded code gets its own cluster. Members lists the top-level functions, for languages that have them.
- Click any member row to jump to it. The jump follows the declaration, so it stays right as you edit above it.
- Collapse the map with [the chevron in its header](locate:Code_MapToggle), or drag it wider; the width is remembered. A tab on the edge brings it back.
- A `.xaml` file maps its `x:Class`, every `x:Name` and `x:Key`, its automation ids and its event handlers. The map keeps working while the file does not yet parse.

## Editing and saving
- Save with Ctrl+S, or from the toolbar, where the encoding and line-ending controls sit beside it. [Show me](locate:Editor_Encoding,Editor_Eol,Editor_Save)
- The encoding is detected when the file opens. Change it and the file is re-read, after asking if you have unsaved edits.
- Save keeps the line endings the file had, or writes LF, CRLF or CR throughout. The footer names the current ones, including Mixed.
- A source file opened from inside a ZIP saves back into the archive, not out to a copy.
- Undo back to the state you saved and the unsaved marker clears.
- If the file changes on disk the editor reloads it and says so in a banner, asking first if you have unsaved work. F5 reloads on demand.
- MD5 or SHA-256 of the whole file or of the selection is copied to the clipboard and shown to you. Commands that need a selection appear only when there is one.
- Base64 encodes or decodes the selection in place, and tells you when the selection is not valid Base64.
- Line operations are on [the line counter in the footer](locate:Editor_LineCommands): convert line endings for any file, and for prose and data remove empty lines, drop duplicates, or sort A→Z or Z→A. They are hidden for code and markup.
- Turn line numbers on or off with [one toggle](locate:Editor_LineNumbers). The footer also shows the file size and line count.

## Finding text
- Type `?` and a term in the AI bar to search this file.
- Several words match lines that hold all of them, anywhere in the line. Wrap a pattern in slashes — `/F[0-9]+/` — to search by regular expression.
- Matches are highlighted, and a strip down the right edge marks where they fall in the file.
- The chip counts the matches and steps forwards or back through them, and clears the search.
- Ask the assistant to narrow the set and the count, highlights and stepping follow it.

## With the assistant
- Turn on [the page-context switch](locate:AiBar_ContextToggle) so the assistant can see the file you are editing.
- Ask it to list every declaration with its line range, to read the whole file, or for the parse tree.
- Ask it to edit one declaration: replace a method, change a signature without the body or the body without the signature, rename, append a member, add an import, or substitute a line inside one method. An edit that would break the file is refused, and one that succeeds lands as a single undo step with your caret and scroll position kept.
- Whole-file find and replace, and Save, are there too. Every edit asks you first.
- It waits for the file to finish loading before answering about it.
