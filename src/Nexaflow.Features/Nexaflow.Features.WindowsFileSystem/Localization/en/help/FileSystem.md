# File System

The File System page is Nexaflow's file explorer: your drives and folders, the files in them, and everything you can
do with them — without leaving the tab you are working in.

---

## Finding your way around

- **This PC** is where a new File System tab starts: every drive with its label, free space, file system and type
  (SSD, HDD, USB, network, CD). A disconnected network drive shows as unavailable rather than holding up the list.
- **The folder tree** on the left and **the file list** on the right show the same place. Click a folder in either
  to go into it.
- **The breadcrumb** above the page shows where you are — *This PC › Drive › Folder* — and every segment is a way
  back up.
- **Go straight to a path** by typing it into the AI bar after a `>`: `> C:\Projects`, `> %USERPROFILE%\Downloads`,
  or a folder relative to where you are.
- **Named folders** — Desktop, Documents, Downloads, Pictures, Music and Videos — are offered as ready-made tabs
  alongside This PC wherever you open a new page.

## The file list

- **Open** a folder or file by double-clicking it, or select it and press **Shift+Enter**. A file opens in the page
  that suits it — text in the Text viewer, Markdown in the Markdown page — in a tab of its own.
- **Sort** by clicking a column heading; click it again to reverse the order.
- **Select several** with **Ctrl** or **Shift** as you click, or press in the empty space below the rows and drag a
  rubber band across them. Hold **Ctrl** to add to what is already selected; dragging past the edge scrolls.
- **Filter at a glance.** The footer counts what is selected and shows *N folders* and *N files*. Click either count
  to show only that kind; click it again to show everything.

## Acting on files

Select something and the **action strip** above the list offers what makes sense for it: **Copy**, **Cut**,
**Paste**, **Rename**, **Delete**, **Run**, **Install**, **Open with**, **Properties** and **Copy path**, plus the
actions Windows adds for that type of file.

- **Right-click** a file, a folder or empty space for the same actions in a menu.
- **Copy path** puts the path of everything selected on the clipboard, one per line.
- **Create** a new folder, any file type Windows knows how to create, or a file from one of your own templates.
- **Your own tools.** In **Options** you can add buttons that open the selection in an app of your choosing, shown
  only for the files they apply to — *Open in VS Code* for `.cs` files, say.

## Moving files around

- **Drag** files out of the list into another app, or drop files onto the list or onto a folder in the tree to copy
  them there. Hold **Shift** to move them instead.
- **Right-drag** to be asked at the drop: *Copy here*, *Move here* or *Cancel*.
- **Progress** for copying, moving and deleting — and for zipping and unzipping — appears above the folder tree with
  a progress bar, the time remaining and a **Cancel** button. If the destination runs out of space, it offers
  **Retry** rather than giving up.

## Two folders side by side

Open a folder in the right pane to see two places at once, which makes dragging between them easy. The window
splits into two panes, each with its own tabs; closing the last tab in a pane closes the split. Help opens in the
pane beside the page you are using the same way.

## More in some folders

Some folders get extra views on top of the list: a folder holding a Git repository shows the repository's state, and
one holding a .NET solution shows its projects. Compact views sit above the file list; a full view can take over
the pane, with a toggle to flip back to the files.

## Pinning to the ribbon

Drag an action — or the whole tab — onto the ribbon to keep it one click away. It remembers the files or folder it
was pinned with.

## Searching and asking

- Type `?` and a word in the AI bar to search the folder you are looking at and everything beneath it.
- **Ask the assistant.** It can see the folder you are in: it can list the files, find files by name, read a file,
  count its lines and check its size — and, with your approval, create, copy, move, rename or delete files.
