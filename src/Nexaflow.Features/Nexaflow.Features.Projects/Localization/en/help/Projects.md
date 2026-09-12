# Projects

Lists the project folders you keep, and opens one.

---

## What a project is
- A project is a folder. Every folder directly under the Project folder is listed here, whether or not it has been given any project detail.
- Its name, description, completion criteria and backlog are stored in a `.project` file inside the folder. A folder without one is listed under its folder name.
- Projects, Shelf and Archives read three separate folders. [Show me](locate:Projects_BucketProjects,Projects_BucketShelf,Projects_BucketArchive)
- Those three folders, the backlog statuses, and whether Projects is on at all are set per workspace, under Projects in Options.

## The list
- A row shows the project's name, the first two lines of its description, and how many backlog items it has. [Show me](locate:Projects_List)
- Selecting a row shows, on the right, a pie chart of its backlog by status, its description, and when the project was last changed.
- Nothing here watches the disk. Read the folders again with the refresh button. [Show me](locate:Projects_Refresh)
- There is no button that makes a project. Create the folder yourself, then open it here and fill in the detail.

## Opening a project
- Open Project opens the project's own page. Open Files opens its folder in the File System page. [Show me](locate:Projects_OpenProject,Projects_OpenFiles)
- Everything you can change about a project is on its [project page](help:ProjectDetail).
- In the File System page, a folder holding a `.project` shows a backlog breakdown and an Open project shortcut.

## Moving a project
- Archive and Shelf move the selected project's folder into the Archive folder or the Shelf folder. [Show me](locate:Projects_Archive,Projects_Shelf)
- Reactivate, offered in Shelf and Archives, moves the folder back to the Project folder.
- The folder itself is moved on disk. The detail pane is replaced while that runs, and the list is read again afterwards.

## Finding a project
- Type `?` and a word in the AI bar to narrow the list to projects whose name, folder name or description match.
- The search covers the bucket you are on, and switching bucket drops it.
- Filename filters have nothing to match here — a project is a folder with a name, not a file.

## With the assistant
- It can list every project in the bucket you are on, with folder name, backlog count and last-modified time.
- It can read one project in full — description, completion criteria and every backlog item — named by folder name or display name, or the one you have selected.
- It reads; it does not change projects.
