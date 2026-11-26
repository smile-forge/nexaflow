# Avalonia shell spike — findings

`Nexaflow.AvaloniaShell` is an Avalonia 12 desktop app that hosts Nexaflow's WPF-free session through the public surface of
`Nexaflow.Host`, `Nexaflow.Session` and `Nexaflow.Assistant` only — no `InternalsVisibleTo`, and no change to the session.
Its question: is that surface enough for a second UI toolkit? This file is its answer.

**Verdict: yes for the shell, no for the pages.** One window starts the session through `ShellHost`, shows a workspace, its
ribbon, the inbox and a working AI bar, moves between workspaces, and opens feature pages through `FeatureManager` — all on
the public surface as it stands. What does not carry over is everything a *view* holds: every feature's view is WPF, many
view-models still name WPF, and a few pieces the WPF app keeps (theme dictionaries, the Windows clipboard codec, the shell
folder watcher, the setup and settings panels, the bar's reply state) are re-done or missing in a second app.

Run it: `nfi build src/Nexaflow.AvaloniaShell/Nexaflow.AvaloniaShell.csproj`, then start
`src/Nexaflow.AvaloniaShell/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/Nexaflow.AvaloniaShell.exe`. It keeps its own
configuration under `%LOCALAPPDATA%\Smile\nexaflow-avalonia-spike` (or under `NEXAFLOW_CONFIG_DIR`), apart from a running Nexaflow's.

## What worked, on the public surface alone

| Piece | Public members used |
|---|---|
| Starting and ending the session | `ShellHost(ShellHostOptions, ShellPlatform, AppUpdates)`, `FirstWindowDrawn`, `StartBackground`, `DisposeAsync`; `ShellLogging.Open`; `AppDataFolders` |
| Filling `ShellPlatform` | every member filled with an Avalonia implementation (below); `WasapiSpeechDevices` from IO.Common used as it is |
| The window | `IWindowHost` (all 32 members) on `ShellWindowModel`; `ShellServices.RegisterWindow` / `SetFocused` / `ClearFocused` / `UnregisterWindow` / `OpenWorkspaceTabs` / `CloseTab`; `WorkspaceRuntimes.WindowFactory` and `LastWindowClosed` |
| Workspace selector | `WorkspaceManager.Workspaces`; `WorkspaceWindows.Move` → `IWindowHost.Rejoin`; `WorkspaceWindows.CopyOf`. Moving to a copy starts its runtime; moving back resumes the dormant one with its tabs |
| Ribbon | `Workspace.Ribbon` (`WorkspaceRibbon.Entries`, `Changed`, `PinDropped`) as plain buttons, each opening its page with `ShellServices.OpenTab` |
| Inbox | `MessageCenter.UnreadCount` / `UnreadChanged` / `MessagePosted` / `ClaimToast` / `ClaimPendingToasts` / `MarkAllRead` / `Messages` |
| AI bar | `AssistantRequests` (`SendAsync`, `PickUpAsync`, `Cancel`, `BusyChanged`, `Failed`, `Cancelled`) over `IAssistantBar`; `TypedQueryPreview`; `BarConversation`; `runtime.AiService` (the Assistant's extension). A request reaches the agent, which asked through `IWindowHost.ShowConfirmation` to download the workspace's model; declining ends the run |
| Feature pages | `ShellServices.OpenTab` → `FeatureManager`; `FeatureCatalog.ViewTypeFor` with the app's own marker (`[AvaloniaViewFor]`); `Page.GetOrCreateViewModel`. The Processes page is the feature's own `ProcessesViewModel`, unchanged, drawn by an Avalonia view with live data |
| First run | `SetupAgenda.Of` read, `AppRuns.Stamp`, `LanguageManager.Select`; what the wizard would ask goes to the inbox |

## Gaps, each with the type or member involved

### 1. Every feature view is WPF — and so are 70 view-models
*`ShellPlatform.ViewMarker`, `FeatureCatalog.ViewTypeFor`, the feature assemblies.*

The page-view mechanism itself carries over: `ShellPlatform.ViewMarker` is the toolkit's attribute, and the catalogue indexes it
in `Nexaflow.Features.*.dll`, `Nexaflow.Visuals.*.dll` and the app's own assembly. A companion assembly such as
`Nexaflow.Features.Processes.Avalonia.dll` would be found with no session change. The spike's `ProcessesView` lives in the app
assembly and is found the same way.

What blocks the pages is the features themselves:
- Every feature project is `UseWPF`, its views and view-models in one assembly, so loading any feature brings the
  `Microsoft.WindowsDesktop.App` framework into the Avalonia process (`Nexaflow.AvaloniaShell.runtimeconfig.json` lists it).
- `ViewModelWpfRatchetTests` lists 70 view-models that still name `System.Windows` — `ScratchpadViewModel` among them — and
  their models cannot be drawn by another toolkit's view as they are. `ProcessesViewModel` is not on the list, which is why it
  drew.
- A page whose feature is not loaded — FileSystem here, which the workspace opens by default — comes as
  `Nexaflow.Features.Common.PlaceholderViewModel`, which the WPF app draws with Core's `PlaceholderPage`; the spike has no view
  for it yet, and draws it as the gap like any other.

### 2. Theme contributions and named themes are WPF resource dictionaries
*`IThemeHost.ApplyContributions(IReadOnlyList<Uri>)`, `ShellConfig.Theme` / `ThemeOption`.*

A contribution is a pack URI to a WPF `ResourceDictionary`; Avalonia cannot read one, so `AvaloniaThemeHost` records each and
applies none. `ThemeOption` names palettes that exist only as WPF dictionaries, and nothing in the session says whether a theme
is light or dark — the spike maps `Light` to Fluent's light variant and draws every other theme dark.

### 3. The pickers port is synchronous
*`IFilePickers.PickOpenFile` / `PickOpenFiles` / `PickFolder` / `PickSaveFile`.*

Avalonia's `IStorageProvider` is asynchronous only, so `AvaloniaFilePickers` waits each pick out in a nested `DispatcherFrame`.
`ShellServices.Pick…Async` already hands every call to the UI thread and returns a task, so the port could return tasks too.

### 4. The clipboard is the process's, but Avalonia's hangs off a window — and the Windows formats are decoded in WPF code
*`ShellPlatform.Clipboard` (`IShellClipboard`), `Visuals.Common`'s `WindowsDragPayload` / `WindowsDataObject`.*

The session makes its clipboard before any window; Avalonia's is `TopLevel.Clipboard`, so `AvaloniaClipboard` holds nothing until
a window is open. It reads and writes text and files only: `FilesCut`, `FileContents`, `Html` and `Image` are Windows formats the
WPF app decodes through WPF's `IDataObject` in `Visuals.Common`, which another toolkit cannot reuse.

### 5. The shell folder watcher is toolkit-free code kept in the WPF app
*`ShellPlatform.FolderWatchers`, Core's `ShellFolderWatcher`.*

`ShellChangeWatcher` is Core's watcher line for line (SHChangeNotify), less WPF's `HwndSource` and `DispatcherTimer`: a raw
message-only window (`MessageWindow`) and a timer from the session's `IUiThread` do the same job. Nothing in it is a toolkit's.

### 6. Deploying the session is each app's own csproj wiring
*`RibbonLayoutService.LoadDefaults`, `AiHostInstall.ExecutablePath`, `LanguageManager`, `ElevatedBridgeLauncher`.*

The session reads `Ribbon\default-ribbon.json`, `AiHost\`, `PrivilegeBridge\` and `Languages\` beside the exe. The spike's
csproj repeats Core's wiring for all four, and links `default-ribbon.json` out of `src/Nexaflow.Core/Ribbon/` because the
workspace's starting ribbon ships as a Core content file.

### 7. The bar's reply state lives in Core
*`IAIResponseHandler.Enqueue` / `TakeInterjections` / `DrainQueue`, Core's `AiResponseHandlerBase` and `ShellAIResponseHandler.BarStyle`.*

`AssistantRequests` and `BarConversation` carried over whole; what sits between them and the drawing did not. `BarSurface`
re-implements the interjection queue and the approval bookkeeping, and `ShellWindowModel` repeats the bar's reply-style prompt
(`BarStyle`) word for word.

### 8. Setup, Options and Configure are Core view-models
*`IWindowHost.OpenOptionsAt`, `IWindowHost.OpenConfigureAt`, Core's `SetupWizardViewModel` / `ConfigPageModels`; `SetupAgenda`, `WorkspaceAiSetup`.*

`SetupAgenda` reads well, and `WorkspaceAiSetup` is public, but the steps and panels that use them are Core's. The spike posts
the agenda to the inbox and answers `OpenOptionsAt` / `OpenConfigureAt` with a notice. A workspace set up nowhere has no model,
so the AI bar reaches the agent but the agent has nothing to run on.

### 9. Smaller ones
- **Icons** — `RibbonEntry.Icon` (`IconRef`) outside the emoji set needs the WPF icon catalogue in `Visuals.Icons`; the spike draws emoji only.
- **Updates** — `ShellHost` takes an `AppUpdates`, whose only source in the session, `UpdatumReleaseSource`, is the WPF app's release channel; the spike passes `NoReleaseChannel`.
- **`IWindowHost` is wide** — 32 members mixing the window (show, placement), its panes (`FocusSecondPane`, `CaptureTabLayout`), dialogs (`ShowConfirmation`, `ShowPrompt`, overlays) and the AI bar (`InsertChatInput`, `SubmitAiQuery`, `GetRibbonQuickOpenTargets`, `ContinueAfterBackgroundJobAsync`). All were fillable; a one-pane window answers `FocusSecondPane` by opening in its only pane.
- **`ShellPlatform.FenceDrawn`** — toolkit-specific by design; the spike draws no fenced block as anything but text.

## Recommendation — what to move or open next

1. **Split each feature's views from its view-models** — `Nexaflow.Features.X` WPF-free, `Nexaflow.Features.X.Wpf` (or `.Avalonia`)
   holding the views — and work the 70-entry `view-models-on-wpf.txt` ratchet down feature by feature. This is the gate for pages;
   the catalogue already finds companion view assemblies by name.
2. **Move the shell folder watcher into the session** (`FolderActivity` beside it), on a message-only window and `IUiThread` timers,
   and drop `ShellPlatform.FolderWatchers`.
3. **Move the Windows clipboard codec to `Nexaflow.Interaction`** over OLE directly, so `IShellClipboard` is filled once for Windows;
   the toolkit then supplies only change notification, if that.
4. **Move the bar's reply state into `Nexaflow.Assistant`** — the interjection queue, approval bookkeeping and the bar's reply style
   — leaving each toolkit only the drawing.
5. **Make `IFilePickers` asynchronous.**
6. **Ship the session's deployment as a `Nexaflow.Host` targets file** — default ribbon, AiHost, PrivilegeBridge, language packs —
   with the default ribbon moved from Core into the session.
7. **Themes as toolkit-neutral tokens** in the session (each theme's colours and whether it is light), which each toolkit maps; feature
   contributions likewise.
8. Later: settings and setup panels as session models a toolkit can draw; a narrower `IWindowHost` split into window, dialog and bar ports.
