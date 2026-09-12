# Process detail

Shows one process — its identity, resource use, threads, modules and open handles.

---

## General

- Read the image name, description, company and file version.
- Copy the full path or the command line it was started with: the **Copy** button beside each.
- Read the user account it runs as, its parent process and PID, its architecture and its start time.
- Copy part of any value: click into the field and select it.
- [The tab](locate:TabItem_ProcessDetail) is named for the process and its PID.

## Performance

- A sparkline holds sixty CPU samples, with the scale it is drawn to printed above it.
- Private bytes, working set, peak working set, thread count, handle count, GDI objects and USER objects are listed beside it.
- Turn sampling on or off with the **● Live** toggle in the header bar. Live re-samples once a second.
- Take one fresh reading: **↻** beside the toggle.
- Sampling pauses while the tab is in the background or the window is minimised.

## Threads, modules and handles

- Threads lists thread id, state, priority, CPU time and start time, updated each second.
- Modules lists every loaded DLL with its version, size and path. Sort by clicking a header, click again to reverse.
- Handles lists open handles by type, handle, access and name. Reading them needs administrator rights: press **Load handles (admin)** and Windows will prompt.
- Threads and Modules also need administrator rights for some processes; when they are refused, the page says so instead of showing an empty list.
- Copy from a row: right-click it. Modules also offer **Open file location**.

## Priority and killing

- Change the priority: the dropdown on the General tab, which offers Idle, BelowNormal, Normal, AboveNormal, High and RealTime and applies at once.
- End the process: the **Kill** button in the header bar, after a confirmation.
- If your account cannot make the change, Nexaflow retries with administrator rights and Windows shows one prompt.
- When the process exits, the page says so.

## Search and the assistant

- Filter the list on the sub-tab you are looking at: type `?` and a term in the ask box. Threads match on id, modules on name and path, handles on name.
- See or clear the current search: [the search chip](locate:ProcessDetail_SearchStatus,ProcessDetail_SearchClear) in the header.
- Switching sub-tab drops the search.
- The assistant sees this process's name, PID, description, CPU, private bytes, working set, thread and handle counts, path, start time and user.
- [Processes](help:Processes) opens this tab; [close it](locate:CloseTab_ProcessDetail) and the list is still there.
