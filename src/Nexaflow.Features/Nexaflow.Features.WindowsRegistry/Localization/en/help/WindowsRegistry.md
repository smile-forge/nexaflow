# Registry

The Registry page browses the Windows registry and edits keys and values in it.

---

## Browsing keys and values

- The Hive box picks the root the tree shows: HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE or HKEY_CLASSES_ROOT. Those three are the only hives this page opens. [Show me](locate:Registry_RootSelector)
- Selecting a key in the tree lists its values and updates the breadcrumbs; a breadcrumb takes you back up. [Show me](locate:Registry_KeyTree, Registry_ValueList)
- Subkeys are read as you expand them, so opening a large hive does not read all of it up front. A key Windows will not let the app read shows an empty value list rather than an error.
- Pinning the tab to the ribbon re-roots the tree at the key you are on, and the ribbon button reopens at that key.
- (Default) is always listed; a key that has not set it shows `(value not set)`.
- DWORD and QWORD data is shown as hex and decimal, binary as hex bytes, multi-string as its entries separated by commas. Expandable string data is shown as stored, so `%VARIABLES%` are not resolved.

## Changing keys and values

Right-click in the key tree for New Key…, Rename…, Delete and Export…. [Show me](locate:Registry_KeyTree)

- New Key… asks for a name, creates it under the key you are on, and moves there. Rename… and Delete are refused at the top of a hive.
- Right-click a value row for Modify…, New and Delete. [Show me](locate:Registry_ValueList)
- New creates a String, Expandable String, DWORD, QWORD, Binary or Multi-String value. It starts empty; use Modify… to put data in it.
- Modify…, or double-clicking a row, opens the data for editing. Type DWORD and QWORD data as decimal or 0x hex, binary as hex bytes, multi-string one entry per line. OK writes it; there is no separate save.
- Deleting a key deletes everything under it. Deleting a key or a value asks first, names what it would remove, and cannot be undone. (Default) cannot be deleted, only cleared.

## Administrator rights

- Each change is attempted as you first. Only when Windows refuses does the app retry through a separate elevated helper, and Windows then shows its own administrator prompt — for `Nexaflow.PrivilegeBridge.exe` rather than for Nexaflow itself. Declining leaves the registry unchanged.
- Whether a change needs this depends on the key you are writing to, not on which hive it is in.

## Exporting and importing

- Export, in the toolbar or in the key's right-click menu, writes the key you are on and everything under it to a .reg file, offering the key's name as the file name.
- Import, in the toolbar, reads a .reg file back. It applies the whole file, wherever the keys in it live — not only under the key you are on — and retries elevated if Windows refuses. Both run Windows' own `reg.exe`.

## Searching

- Type `?` and what you are looking for in the AI bar. The walk covers key names, value names and value data under the key the tab is rooted at, so a tab pinned at a key searches that subtree only.
- Matches are keys: several matching values in one key count once, and going to a match selects the value row that matched.
- Step through the matches and clear the search from the chip in the toolbar. [Show me](locate:Registry_SearchPrevious, Registry_SearchNext, Registry_SearchClear)
- Searching a whole hive can take a while. The walk stops after 100,000 keys or 200 matches; when it stops early the count is shown with a `+` and is a floor rather than a total.

## With the assistant

- It can list the subkeys and the values of the key you are on, or of any key below it. It cannot move above that key.
- It can change an existing value and create a new value under the current key. Both ask your approval before writing.
- It cannot create, rename or delete keys, delete values, or export and import.
