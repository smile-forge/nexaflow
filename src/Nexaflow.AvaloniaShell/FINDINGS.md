# Avalonia shell spike — findings

`Nexaflow.AvaloniaShell` is an Avalonia 12 desktop app that hosts Nexaflow's WPF-free session through the public surface of
`Nexaflow.Host`, `Nexaflow.Session` and `Nexaflow.Assistant` only — no `InternalsVisibleTo`, and no change to the session.
Its question: is that surface enough for a second UI toolkit, and can that toolkit look and move as the WPF shell does? This file
is its answer.

**Verdict: yes for the shell and its look; the pages are each feature's work.** One window starts the session through `ShellHost`,
draws the WPF shell's chrome to its measurements — caption, logo and ticker, ribbon, breadcrumb and tabs, AI bar, messages, toasts
— in the WPF app's themes, read from its own theme files, with the Ocean theme's animated reef behind it. It moves
between workspaces, sends through the AI bar, and opens feature pages through `FeatureManager`, all on the public surface as it
stands. What a second toolkit has to bring itself is what belongs to a toolkit: each page's view and view-model, each theme's
scenes, and the chrome's own controls.

Run it: `nfi build src/Nexaflow.AvaloniaShell/Nexaflow.AvaloniaShell.csproj`, then start
`src/Nexaflow.AvaloniaShell/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/Nexaflow.AvaloniaShell.exe`, with `--theme Ocean`
(or any `ThemeOption`) to show a theme other than the saved one. It keeps its own configuration under
`%LOCALAPPDATA%\Smile\nexaflow-avalonia-spike` (or under `NEXAFLOW_CONFIG_DIR`), apart from a running Nexaflow's.

## What worked, on the public surface alone

| Piece | Public members used |
|---|---|
| Starting and ending the session | `ShellHost(ShellHostOptions, ShellPlatform, AppUpdates)`, `FirstWindowDrawn`, `StartBackground`, `DisposeAsync`; `ShellLogging.Open`; `AppDataFolders` |
| Filling `ShellPlatform` | every member filled with an Avalonia implementation (below); `WasapiSpeechDevices` from IO.Common used as it is |
| The window | `IWindowHost` (all 32 members) on `ShellWindowModel`; `ShellServices.RegisterWindow` / `SetFocused` / `ClearFocused` / `UnregisterWindow` / `OpenWorkspaceTabs` / `CloseTab`; `WorkspaceRuntimes.WindowFactory` and `LastWindowClosed` |
| Workspace selector | `WorkspaceManager.Workspaces`; `WorkspaceWindows.Move` → `IWindowHost.Rejoin`; `WorkspaceWindows.CopyOf`; the runtime's `Color` and `Icon`. Moving to a copy starts its runtime; moving back resumes the dormant one with its tabs |
| Ribbon | `Workspace.Ribbon` (`WorkspaceRibbon.Entries`, `Changed`, `PinDropped`), each button opening its page with `ShellServices.OpenTab`; Fluent icons through `Nexaflow.Icons.FluentGlyphs` |
| Ticker, messages, toasts | `BackgroundActivityManager.Tasks`; `MessageCenter.UnreadCount` / `UnreadChanged` / `MessagePosted` / `ClaimToast` / `ClaimPendingToasts` / `MarkAllRead` / `Messages` |
| Tabs and breadcrumb | `Page.Title` / `Icon` / `Breadcrumbs` (`BreadcrumbSegment.Navigate`) |
| AI bar | `AssistantRequests` (`SendAsync`, `PickUpAsync`, `Cancel`, `BusyChanged`, `Failed`, `Cancelled`) over `IAssistantBar`; `TypedQueryPreview` (the status dot's states and the ghost completion); `BarConversation`; `runtime.AiService`. A request reaches the agent, which asked through `IWindowHost.ShowConfirmation` to download the workspace's model |
| Feature pages | `ShellServices.OpenTab` → `FeatureManager`; `FeatureCatalog.ViewTypeFor` with the app's own marker (`[AvaloniaViewFor]`); `Page.GetOrCreateViewModel` |
| Strings | the shell's own keys (`Shell.TopBar.Options`, `Shell.AiBar.Placeholder`, …) through `Str`, from the language packs the WPF app ships |
| First run | `SetupAgenda.Of` read, `AppRuns.Stamp`, `LanguageManager.Select`; what the wizard would ask goes to the inbox |

## Looking and moving as the WPF shell does

- **The chrome to the WPF shell's measurements.** Rows 19 / 72 / * / 5 / 90; the 200 / * / 180 top bar; the 10 px bevel with the
  workspace's colour along its first 240 px; ribbon buttons, tabs, caption buttons and the AI bar's fields and buttons with the WPF
  styles' sizes, radii, fonts, colours and states — every colour a theme key, bound dynamically. The window draws its own caption
  (`WindowDecorations="BorderOnly"`, `ExtendClientAreaToDecorationsHint`), drags from the top strip and maximises on a double click.
- **The WPF shell's motion.** The ticker fades each line in over 400 ms and slides one too long for its column; the AI line opens out
  of the status dot over 180 ms, eased out, while the user's field moves down; the status dot breathes while the assistant would
  answer the words and squashes while a run is busy; the tab strip eases in; a toast fades over 2 s.
- **Icons as WPF draws them.** WPF cannot draw colour fonts, so the shell's emoji icons are Segoe UI Emoji's plain outlines filled
  with the foreground. Skia draws the colour layers and gives no outline for them, so `EmojiOutlines` reads the outlines from the font
  through HarfBuzz (`hb_font_draw_glyph`, in the `libHarfBuzzSharp` Avalonia ships), shaped first; a character the emoji font lacks
  falls back to the text fonts, as WPF's fallback does.
- **Every theme, from the WPF app's own files.** A theme is plain data — `Colors.{Theme}.xaml` (the palette), `Tokens.xaml` (the
  region tokens) and `Theme.{Theme}.xaml` (overrides and scenes) hold only colours, solid and linear-gradient brushes, lengths and a
  template naming a region's scene. `ShellTheme` reads Core's files, embedded unchanged, into Avalonia resources in the same merge
  order, and takes the Fluent variant from the palette's own text colour. It reads every element kind the ten themes use; Dark and
  Ocean were run. None is copied.
- **Scenes behind regions.** `ThemedRegion` draws a region's `Scene.{Region}` / `StillScene.{Region}` behind its `{Region}.Bg` veil,
  as the WPF one does, making the scene by the class name the theme writes. `OceanReefScene` is a port of the WPF reef on an
  `AnimatedScene` base that runs every motion on Avalonia's compositor (`Translation`, `RotationAngle`, `Scale`, `Opacity`), cached as
  bitmaps after each build, staggered by seeking into the cycle, stopped while the window is minimised or the scene hidden. Idle,
  the Ocean window took about **9.6 % of one core**, against the 10.7 % `docs/theming.md` measures for the WPF reef — not a
  controlled comparison, but the same order.

## Gaps, each with the type or member involved

### 1. Each feature's pages need an Avalonia view and view-model — and a model to build them on
*`ShellPlatform.ViewMarker`, `FeatureCatalog.ViewTypeFor`, the feature assemblies.*

A view-model supports its view, so an Avalonia page is an Avalonia view over an Avalonia view-model; the two apps share the
feature's *model*, not its view-model. The page-view mechanism carries over as it is: `ShellPlatform.ViewMarker` is the toolkit's
attribute, and the catalogue indexes it in `Nexaflow.Features.*.dll`, `Nexaflow.Visuals.*.dll` and the app's own assembly, so a
companion `Nexaflow.Features.X.Avalonia.dll` would be found with no session change.

What stands in the way is where the features keep their logic:
- Much of what a page *does* — its state, its data sources, its commands' work, its client tools — lives in the WPF view-model,
  so an Avalonia view-model would have to repeat it. That part has to come out into a WPF-free model each toolkit's view-model uses.
- Every feature project is `UseWPF`, model and WPF view-model and view in one assembly, so loading any feature brings the
  `Microsoft.WindowsDesktop.App` framework into the Avalonia process (`Nexaflow.AvaloniaShell.runtimeconfig.json` lists it).
- The spike's Processes page binds the feature's own `ProcessesViewModel`, written for its WPF view, which happens to name no WPF. That shows the catalogue
  route works; it is not the pattern — the Avalonia app would have its own Processes view-model over the feature's process model.
- `ViewModelWpfRatchetTests` holds the opposite position: it counts a view-model naming WPF as one to be rewritten for another
  toolkit, and ratchets 70 of them. Under per-toolkit view-models its target is the model logic in them, not the WPF in them.
- Pages the session opens with no feature behind them come as `Nexaflow.Features.Common.PlaceholderViewModel`, which the WPF app
  draws with Core's `PlaceholderPage`; each app supplies a view for it.

### 2. Themes carry over; scenes and feature contributions do not
*`IThemeHost.ApplyContributions(IReadOnlyList<Uri>)`, Core's `Themes/*.xaml`, the `*Scene` classes.*

The palettes, tokens and overrides are read from Core's files as they are (above). What does not carry over: each theme's scene is a
WPF control, so each needs a port — the Ocean reef is ported; Arctic, Desert, Flowerbed, Forest, Gothic, Sandstone and Sunny are
not, and draw their colours without their backdrop. A feature's theme contribution is a pack URI into its assembly, compiled to BAML,
which nothing outside WPF can read; `AvaloniaThemeHost` records each and applies none. The theme files living in `Nexaflow.Core`,
the WPF app, is now a placement problem: both apps read them.

### 3. The pickers port is synchronous
*`IFilePickers.PickOpenFile` / `PickOpenFiles` / `PickFolder` / `PickSaveFile`.*

Avalonia's `IStorageProvider` is asynchronous only, so `AvaloniaFilePickers` waits each pick out in a nested `DispatcherFrame`.
`ShellServices.Pick…Async` already hands every call to the UI thread and returns a task, so the port could return tasks too.

### 4. The clipboard is the process's, but Avalonia's hangs off a window — and the Windows formats are decoded in WPF code
*`ShellPlatform.Clipboard` (`IShellClipboard`), `Visuals.Common`'s `WindowsDragPayload` / `WindowsDataObject`.*

The session makes its clipboard before any window; Avalonia's is `TopLevel.Clipboard`, so `AvaloniaClipboard` holds nothing until a
window is open. It reads and writes text and files only: `FilesCut`, `FileContents`, `Html` and `Image` are Windows formats the WPF
app decodes through WPF's `IDataObject` in `Visuals.Common`, which another toolkit cannot reuse.

### 5. The shell folder watcher is toolkit-free code kept in the WPF app
*`ShellPlatform.FolderWatchers`, Core's `ShellFolderWatcher`.*

`ShellChangeWatcher` is Core's watcher line for line (SHChangeNotify), less WPF's `HwndSource` and `DispatcherTimer`: a raw
message-only window (`MessageWindow`) and a timer from the session's `IUiThread` do the same job. Nothing in it is a toolkit's.

### 6. Deploying the session is each app's own csproj wiring
*`RibbonLayoutService.LoadDefaults`, `AiHostInstall.ExecutablePath`, `LanguageManager`, `ElevatedBridgeLauncher`.*

The session reads `Ribbon\default-ribbon.json`, `AiHost\`, `PrivilegeBridge\` and `Languages\` beside the exe. The spike's csproj
repeats Core's wiring for all four, and links `default-ribbon.json`, the theme files and the Fluent icon font out of Core and
Visuals.Icons.

### 7. The bar's reply state lives in Core
*`IAIResponseHandler.Enqueue` / `TakeInterjections` / `DrainQueue`, Core's `AiResponseHandlerBase` and `ShellAIResponseHandler.BarStyle`.*

`AssistantRequests` and `BarConversation` carried over whole; what sits between them and the drawing did not. `BarSurface`
re-implements the interjection queue and the approval bookkeeping, and `ShellWindowModel` repeats the bar's reply-style prompt
(`BarStyle`) word for word.

### 8. Setup, Options, Configure, Help and the ribbon editor are Core view-models
*`IWindowHost.OpenOptionsAt`, `IWindowHost.OpenConfigureAt`, Core's `SetupWizardViewModel` / `ConfigPageModels` / `RibbonEditor`; `SetupAgenda`, `WorkspaceAiSetup`.*

`SetupAgenda` reads well and `WorkspaceAiSetup` is public, but the steps and panels that use them are Core's. The spike posts the
agenda to the inbox and answers Options, Configure, Help and the ribbon's edit toggle with a notice. A workspace set up nowhere has
no model, so the AI bar reaches the agent but the agent has nothing to run on.

### 9. Smaller ones
- **Updates** — `ShellHost` takes an `AppUpdates`, whose only source in the session, `UpdatumReleaseSource`, is the WPF app's release channel; the spike passes `NoReleaseChannel`.
- **`IWindowHost` is wide** — 32 members mixing the window, its panes, dialogs and the AI bar. All were fillable; a one-pane window answers `FocusSecondPane` by opening in its only pane.
- **`ShellPlatform.FenceDrawn`** — toolkit-specific by design; the spike draws no fenced block as anything but text.

### Not ported in the spike's chrome
The ribbon's compact layout and overflow, split panes, dragging and tearing off tabs, the reply card, voice, the background-tasks
panel, the battery policy for scenes, and the AI row's resize cap. None needs anything of the session the spike did not already use.

## Recommendation — what to move or open next

1. **Take the model out of each feature's view-models**, into WPF-free models (or the session) that each toolkit's view-model uses —
   state, data sources, the work behind commands, client tools — and keep each view-model to supporting its own view. Retarget
   `ViewModelWpfRatchetTests` from "view-models that name WPF" to the model logic still inside them.
2. **Move the theme files out of `Nexaflow.Core`** to a place both apps read from, and ship feature contributions as the same plain
   data rather than compiled BAML, so a contribution reaches every toolkit.
3. **Move the shell folder watcher into the session** (`FolderActivity` beside it), on a message-only window and `IUiThread` timers,
   and drop `ShellPlatform.FolderWatchers`.
4. **Move the Windows clipboard codec to `Nexaflow.Interaction`** over OLE directly, so `IShellClipboard` is filled once for Windows.
5. **Move the bar's reply state into `Nexaflow.Assistant`** — the interjection queue, approval bookkeeping and the bar's reply style —
   leaving each toolkit only the drawing.
6. **Make `IFilePickers` asynchronous.**
7. **Ship the session's deployment as a `Nexaflow.Host` targets file** — default ribbon, AiHost, PrivilegeBridge, language packs —
   with the default ribbon moved from Core into the session.
8. Later: settings and setup panels as session models a toolkit's view-models can use; a narrower `IWindowHost` split into window,
   dialog and bar ports.

