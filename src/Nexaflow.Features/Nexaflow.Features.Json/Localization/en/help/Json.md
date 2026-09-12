# JSON viewer

Shows a JSON file as a tree you can read, edit and search.

---

## Opening a JSON file

- Double-click any `.json` file in the [File System](help:FileSystem) page. The tab takes the file's name.
- Send another extension here with the File System page's *Define New* file-association wizard, choosing *JSON file viewer and editor*.
- Comments, trailing commas and a byte-order mark do not stop a file opening. A file that is not valid JSON shows the parser's explanation where the tree would be.

## Reading the tree

- Expand and collapse a node with the toggle at the start of its row. Each object says how many properties it holds, each array how many items.
- Go back to a level: click its crumb in the path bar under the toolbar, which reads like `$ / "orders" / [12] / "customer"`.

## Views

The switches at the left of the toolbar change how the *selected* node is drawn, and Tree is the default. [Show me](locate:Json_TreeMode,Json_TextMode,Json_TableMode)

- Raw JSON puts that node's text into a small editor beneath its row. Edit an object or array there and click away: valid JSON folds back into the document, and text the parser cannot read is left alone, with the node keeping what it had.
- Table turns an array of objects into rows and columns — one column per key, in the order the keys first appear, blanks where an item has no value, and a nested value shown as its own count, such as `{ 4 }` or `[ 12 ]`. [The table switch](locate:Json_TableMode) is available whenever the selected array holds objects; choose it on the root array to table the whole document.

## Editing and saving

- Move a row: drag it onto one of its siblings, above or below depending on which half you drop on. Array indices renumber themselves, and a drop is only accepted beside a sibling.
- Re-indent the whole document: [Format](locate:Json_Format). It marks the document modified but does not write to disk — the file changes when you save, and not before.
- Save appears only when there is something to save, alongside a **Modified** flag in the status bar. While any part of a streamed document is still unloaded, Save and Format both decline and say so.

## Large files

Anything over a megabyte streams, and the status bar then carries the file's size, its node count and a **Streaming** badge.

- The first items appear while the rest of the file is still being measured; the count is estimated from the file's ends.
- Placeholder rows stand in for what has not arrived, and batches of fifty load as you scroll, in either direction.
- Three hundred top-level items are held at once. Those furthest from you are released and come back when you scroll to them.

## Searching

- Go to a node by JSONPath: type it beginning with `$` — such as `$.store.book[0].title` — into [the AI bar](locate:AiInputBox). The viewer expands to the first match, selects it and scrolls it into view. A path that matches nothing says so, and one that lands outside the loaded part of a streamed file says that.
- Search the whole file, not just the part on screen: type `?` and a term. Items containing a match are marked, the toolbar counts the hits, and next and previous step between them.
- Several words must all appear in the same item. `order*` matches a prefix, `*order*` anything containing it, and anything between slashes — `/ERR-[0-9]+/` — is a regular expression.
- The search stops after 200 matching items and marks the count as a floor rather than a total. Past 512 MB it declines to search and asks you to narrow the ground with a `$` path first.

## The assistant

- It knows the file's name and size, the shape of the root, the node count, the current view, the JSONPath of the selection, whether there are unsaved changes, and whether the file is only loaded in a window.
- It can evaluate a JSONPath and hand back the matching values, read the loaded document including your unsaved edits, re-indent it, and run the same `?` query you would.
- Nothing it does here changes a value or writes to disk. A re-indent leaves the document modified, waiting on your Save.
