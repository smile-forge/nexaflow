# File System

Browses the drives and folders on this PC and acts on the files in them.

---

## Getting to a folder

- A new tab starts at This PC and lists the drives; Desktop, Documents, Downloads, Pictures, Music and Videos are offered as tabs of their own.
- Go to a path by typing it into the bar at the bottom after `>` — `> C:\data`, `> %AppData%`, or a folder name relative to where you are. A full path is recognised there without the `>`.
- Right-click a folder in the tree for Open in right pane, which puts a second folder beside this one.
- F5 re-reads the folder. Clicking the folder or file tally under the list shows only that kind; clicking it again shows both.

## Linux (WSL)

- Each WSL distro installed on this PC is a row of This PC, of type Linux (WSL) — nothing to set up. Opening it starts the distro if it is not running, and puts a small helper in your Linux account's cache folder the first time.
- Go to a folder in one by typing `> wsl://Ubuntu/etc` or `> \\wsl.localhost\Ubuntu\etc` into the bar at the bottom.
- A distro's folders list their Permissions, Owner and Group as `ls -l` does — a link's permissions start with `l` — and names that differ only by case are different files, as they are in Linux.
- Names in a distro are Linux's: Rename and New take any name Linux does — `notes:v2`, `draft.`, `50%` — show it as it is, and give the file exactly that name.
- A distro has no Recycle Bin: Delete there is for good, and asks first saying so. Shift+Delete does not ask. The assistant can delete there too, and asks you first in words that say it is for good.
- A distro's file goes to other programs by its `\\wsl.localhost` path — Open With, Copy and Cut to the Windows clipboard, dragging out, Windows' own actions for the file type and your external programs — so they open it as they would any file. Renames, moves and deletes in Nexaflow still happen inside the distro.
- Saving a distro's file keeps its permissions, owner and group, and so does copying a file over one there. A file new to the distro takes its usual defaults, so a script copied in from Windows may need `chmod +x`. Windows keeps no Linux permissions, so a file copied out to Windows has none.
- Cmd Here opens the distro's own shell at the folder.
- A folder that cannot be read — the distro was shut down, you may not read it — says why, with Try again. [Show me](locate:FileListRetryButton)

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
- Where the workspace has a model for it, Create new also offers Gen Image and Gen Video: say what to draw or what happens, pick from what the model offers — its shapes and sizes, a seed to draw the same thing again, what to keep out, a LoRA that draws in a style or faster — and it is made in the background under the name you give, then opens. A LoRA meant for adults only is offered only while Enhanced AI Protection Mode (Options → Shell) is off.

## Extra views on a folder

- A folder holding a Git repository, a .NET project or a product tree carries a view of it above the files. Viewlet View gives that view the whole pane, File View returns to the list. [Show me](locate:Viewlet_ViewletView,Viewlet_FileView)

## Searching and the assistant

- Type `?` and a term into the bar to search the files under this folder, or across the PC from This PC. Results open in a Search tab, which also offers a manual scan when a folder is not in the Windows index.
- The assistant acts in the folder you are looking at. It can list the files, find them by name, read one, count its lines and report its size, and — with your approval — create a file or folder, copy, move, rename, or send something to the Recycle Bin (in a distro, which has none, delete it for good).
