# Installed Apps

Lists the applications installed on this PC and lets you remove, modify or repair them.

---

## What the list covers
- Win32 programs, read from Windows' own uninstall records: per-machine (64-bit and 32-bit) and per-user. The Source column shows Win32. [Show me](locate:WindowsApps_List)
- Microsoft Store packages installed for your account. The Source column shows Store. Framework, resource and Windows system packages are left out, and an add-on is listed under its app rather than beside it.
- Refresh re-scans both. [Show me](locate:WindowsApps_Refresh)
- A dash under Installed or Size means the app did not report one. Sizes no installer recorded are measured in the background and fill in after the list is already showing.
- Click a column header to sort by it; click the same header again to reverse the order.
- Select several rows with Ctrl or Shift.

## Finding an app
- Type `?` and part of a name in the AI bar to narrow the list. [Show me](locate:AiInputBox)
- Write it as `?/pattern/` to match with a regular expression; add `c` after the closing slash to make it case-sensitive.
- The filter matches the application name only, not the publisher.

## Row actions
The ⋯ button on a row opens its menu. Which entries appear depends on the app.

- **Open Location** opens the install folder in a File Explorer tab. Offered when a folder is recorded and still exists on disk.
- **Uninstall** asks you to confirm first. For a Win32 program it runs that program's own uninstaller, which may show its own prompts or ask for administrator approval; for a Store app it removes the package for your account. The list re-scans afterwards, and a failure is reported with the reason the uninstaller gave.
- **Modify** reopens a Win32 program's installer in maintenance mode so you can add or remove its features. Offered only when the program registered such a command and did not forbid it.
- **Remove from list** appears only for a Win32 entry whose install folder and uninstaller are both missing. It deletes the leftover registry record and no files. Removing a machine-wide entry may require running as administrator, and says so if it cannot.
- **Move…** and **Advanced options** appear for Store apps, and both open the pane described below.

## Advanced options
A pane beside the list, for one Store app. Drag its edge to resize it.

- **Let this app run in background** — Power optimized (recommended), Always or Never. The choice is written to the same setting Windows uses.
- **Terminate** stops the app and its background processes and says how many it stopped. **Repair** re-registers the app from its own files, leaving your data alone. **Reset** deletes the app's data, settings and sign-in details and reinstalls it; it asks first and cannot be undone. [Show me](locate:WindowsApps_Terminate,WindowsApps_Repair,WindowsApps_Reset)
- **Move** relocates the app's files to another drive: pick the drive, then choose Move. It is offered only when this PC has another drive that can hold apps. [Show me](locate:WindowsApps_MoveVolume,WindowsApps_Move)
- **App add-ons & downloadable content** lists the optional packages installed against the app, each with its own Remove.

## The assistant on this page
- It is given the rows you have selected, or the names of the applications loaded, as context. While the first scan is still running it is told so instead.
- `list_installed_applications` gives it every loaded name, ignoring the current filter and selection. `get_application_details` gives it the publisher, version, install date, size, location and source of one app by name.
- Both only read. Nothing is uninstalled, modified or reset except by you, from the menus above.
