# File System

Browses the drives and folders on this PC and acts on the files in them.

---

## Getting to a folder

- A new tab starts at This PC and lists the drives; Desktop, Documents, Downloads, Pictures, Music and Videos are offered as tabs of their own.
- Go to a path by typing it into the bar at the bottom after `>` — `> C:\data`, `> %AppData%`, or a folder name relative to where you are. A full path is recognised there without the `>`.
- Right-click a folder in the tree for Open in right pane, which puts a second folder beside this one.
- F5 re-reads the folder. Clicking the folder or file tally under the list shows only that kind; clicking it again shows both.

## Opening a file

- Double-click a file, or press Shift+Enter with one row selected. It opens in whichever built-in viewer claims its type, and in the Windows default program when none does.
- A `.zip`, `.7z`, `.tar.gz`, `.iso` or `.vhd` opens in place — you browse inside it as though it were a folder. A file that merely happens to be a container, like `.docx` or `.eml`, keeps its own viewer.
- Define New fixes what a type opens with from then on: a viewer inside the app or an outside program, applied to this one file, to that extension in this folder, or to the extension anywhere. [Show me](locate:DefineNew_TargetInternal,DefineNew_TargetNewApp)

## Copying, moving and deleting

- Copy, Cut, Paste, Rename, Delete, Run, Install, Open With, Properties and Copy path act on the selection, from the strip above the list or the right-click menu. Ctrl+C, Ctrl+X and Ctrl+V do the first three.
- Dragging files onto a folder copies them; hold Shift to move. Right-dragging asks at the drop — Copy here, Move here or Cancel.
- A copy or move into a folder that already holds that name arrives as "name (2)"; nothing is replaced.
- Rename takes one item at a time and will not replace a file that already has the name you give.
- Delete asks first and sends the files to the Recycle Bin. Shift+Delete skips the question and deletes them for good.
- Run and Install hand the file to Windows, so a program that needs administrator is Windows asking for it, not this page.
- Copies, moves and deletes are queued and run in the background, with a row each and a Stop all. [Show me](locate:FileOps_Toggle,FileOps_CancelAll)
- A run that stops part-way offers Try again, which continues into the same destination rather than making a second copy. [Show me](locate:FileOps_Retry)

## Creating things

- New opens Create new, which makes a folder, a text file, any type Windows knows how to create, or one of your own templates, and says so before you commit if the name is taken. [Show me](locate:FileSystem_CreateConfirm)

## Extra views on a folder

- A folder holding a Git repository, a .NET project or a product tree carries a view of it above the files. Viewlet View gives that view the whole pane, File View returns to the list. [Show me](locate:Viewlet_ViewletView,Viewlet_FileView)

## Searching and the assistant

- Type `?` and a term into the bar to search the files under this folder, or across the PC from This PC. Results open in a Search tab, which also offers a manual scan when a folder is not in the Windows index.
- The assistant acts in the folder you are looking at. It can list the files, find them by name, read one, count its lines and report its size, and — with your approval — create a file or folder, copy, move, rename, or send something to the Recycle Bin.
