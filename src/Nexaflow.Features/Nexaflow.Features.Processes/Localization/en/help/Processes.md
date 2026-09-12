# Processes

Lists the processes running on this machine and lets you inspect or end them.

---

## The list

- Sort by a column: click its header, click again to reverse. It opens sorted by CPU, highest first.
- Resize a column: drag the right edge of its header.
- Open one process: double-click its row, or right-click and choose **View details**.
- The title row reads *N shown · M total*, so you can see how much a filter is hiding.

## Tree, flat and filtered

- Switch between the tree and a flat list with the **Tree** toggle. In the tree each process nests under the one that started it. [Show me](locate:Proc_ToggleTree)
- Open one branch: click the row's chevron. Rows start collapsed.
- Open or close every branch: [Expand all and Collapse all](locate:Proc_ExpandAll,Proc_CollapseAll).
- Filter the list: type in [the search box](locate:Proc_Filter,Proc_ClearFilter). It matches name, PID, company, description or image path, any case.
- Clear the filter with the ✕ at the end of the box.
- While the box has text the results are a flat list, whatever the Tree toggle says.

## Refreshing

- Turn sampling on or off with [the ● Live toggle](locate:Proc_ToggleLive,Proc_Refresh). Live re-samples once a second; turning it off stops the numbers moving and discards nothing.
- Take one fresh sample: the ↻ button beside it, live or not.
- Two sparklines in the title row show machine-wide CPU and RAM, sixty samples each, with the current percentage.
- Sampling pauses while the tab is in the background or the window is minimised.
- Selection, expanded branches and scroll position survive a refresh.

## Right-click a row

- **View details** opens that process in [its own tab](help:ProcessDetail).
- **Open file location** opens the program's folder in a [File System](help:FileSystem) tab.
- **Copy name**, **Copy PID**, **Copy path** and **Copy details** put those on the clipboard.
- **Kill process** ends it after a confirmation naming the process.
- **Kill process tree** ends it and every process it started, after its own confirmation.
- If your account cannot end a process, Nexaflow retries with administrator rights and Windows shows one prompt.

## Search and the assistant

- Search the same five fields: type `?` and a term in [the ask box](locate:AiInputBox). Quoted phrases, `chrome*` and `/regex/` all work. The query appears in the search box; typing over it goes back to a plain filter.
- The assistant sees how many processes are running, machine-wide CPU and memory, and the top three by CPU and by memory.
- It can list processes, rank the top consumers by CPU or memory, read one process's details or loaded modules, and print the parent/child tree, including processes the filter hides.
- Ending a process or a tree and changing a priority need your approval first, and Windows may then ask for administrator rights.

## Opening the page

- [The Processes button on the ribbon](locate:Ribbon_Processes).
- [Process detail](help:ProcessDetail) adds threads, modules, open handles, the command line and the priority selector for one process.
