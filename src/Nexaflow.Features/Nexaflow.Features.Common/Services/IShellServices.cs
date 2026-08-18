using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.Features.Common;

/// <summary>
/// The active workspace's shell handle: manages tab/window lifetime within one
/// workspace. Each workspace owns its own instance (built by WorkspaceManager),
/// so this is NOT an application-wide singleton. Feature code, views, and query handlers
/// call this instead of holding any reference to the shell or window layer.
/// </summary>
public interface IShellServices
{
    /// <summary>
    /// Opens (or activates) a tab for <paramref name="pageKind"/>.
    /// When a matching tab already exists in another window it is moved to
    /// the window that owns the <paramref name="caller"/> page; if caller is
    /// null the focused window is used.
    /// <para>
    /// When <paramref name="inRightPane"/> is true the tab area is split first if needed and a
    /// <em>fresh</em> tab is always created in the window's second (right) pane — existing matching
    /// tabs are not adopted, so the same location can sit side-by-side in both panes.
    /// </para>
    /// </summary>
    void OpenTab(string pageKind, Dictionary<string, string>? pageParams = null,
                 IPageView? caller = null, bool inRightPane = false);

    /// <summary>Closes and removes a tab from the global tab registry.</summary>
    void CloseTab(Page tab);

    /// <summary>
    /// Lightweight <see cref="Page"/> definitions for the pages that can be created without specific
    /// context (<see cref="IPageRegistration.CanBeContextItem"/>) in this workspace — surfaced in the
    /// AI conversation's "add context" menu, which reads each page's Title/Icon. Built per-workspace,
    /// so it reflects the active workspace's enablement (e.g. Projects only when enabled). Content is not
    /// realized; pin a chosen page by calling <c>GetOrCreateContent</c> on it.
    /// </summary>
    IReadOnlyList<Page> GetContextItemPages();

    /// <summary>
    /// Every tab currently open across this workspace's windows, in window then pane then tab-strip order.
    /// A live snapshot of the shell's tab registry — the same <see cref="Page"/> objects the strip holds,
    /// not copies — so a feature can act on an open tab without owning or closing it (e.g. the AI
    /// conversation's "Open tabs" context menu, the keyboard route to what its drop target already allows).
    /// Re-ask rather than caching: tabs open and close under you.
    /// </summary>
    IReadOnlyList<Page> GetOpenTabs();

    /// <summary>Every page + ribbon shortcut the AI-input quick-open can jump to (label + open action),
    /// deduped by label. Lets a query handler match by name and open one without touching shell chrome.</summary>
    IReadOnlyList<QuickOpenTarget> GetQuickOpenTargets();

    /// <summary>
    /// Hands <paramref name="task"/> to the shell's background-activity manager: it is reported in
    /// the activity area and its <see cref="IBackgroundTask.RunAsync"/> runs off the UI thread.
    /// <paramref name="onComplete"/>, if supplied, is invoked on the UI thread when the task ends
    /// (true on success, false if it threw). Cancelling <paramref name="ct"/> requests the task abort
    /// (the token is passed to <see cref="IBackgroundTask.RunAsync"/>); a cancelled task ends quietly
    /// — no failure is reported and <paramref name="onComplete"/> is not invoked.
    /// </summary>
    void QueueBackgroundTask(IBackgroundTask task, Action<bool>? onComplete = null,
                             CancellationToken ct = default);

    /// <summary>
    /// Registers a user-mediated background task: the shell shows <paramref name="registration"/>'s control just
    /// left of the background-activity ticker so the user can keep driving a page's work (e.g. audio transport)
    /// after switching away from its tab. Shared across the workspace's windows (each realizes its own control
    /// instance via the registration's factory). Dispose the returned handle to remove it — typically when the
    /// page reactivates and retakes ownership, or when it closes. Safe to call off the UI thread.
    /// </summary>
    IDisposable RegisterMediatedTask(MediatedTaskRegistration registration);

    /// <summary>
    /// Watches <paramref name="path"/> for content changes, invoking <paramref name="onChanged"/> on this
    /// workspace's UI thread (debounced). One underlying watcher is shared across all callers of the same
    /// path, and watchers are torn down with the workspace. Disable the returned handle while your view is
    /// unloaded to hold and coalesce notifications; dispose it to stop watching. Features call this instead
    /// of creating their own watcher, so they never touch the UI dispatcher.
    /// </summary>
    IFileWatch WatchFile(string path, Action onChanged);

    /// <summary>
    /// Runs <paramref name="action"/> on this workspace's UI thread — inline if the caller is already on
    /// it, otherwise marshalled — completing when it has run. For feature code that produces work on a
    /// background thread the shell does not own (e.g. a PTY read loop) and needs to touch UI state;
    /// features should never reach for <c>Application.Current</c>.
    /// </summary>
    Task RunOnUiAsync(Action action);

    /// <summary>
    /// Async, result-returning form of <see cref="RunOnUiAsync(Action)"/>: runs <paramref name="action"/>
    /// on this workspace's UI thread (inline if already there) and returns its result. Use when the UI
    /// work itself awaits — e.g. showing an approval and awaiting the user's decision.
    /// </summary>
    Task<T> RunOnUiAsync<T>(Func<Task<T>> action);

    /// <summary>
    /// Returns the first globally-open tab whose <see cref="Page.PageKind"/>
    /// matches and whose params are compatible (see param-matching rules), or null.
    /// </summary>
    Page? FindTab(string pageKind, Dictionary<string, string>? pageParams = null);

    /// <summary>Shows a transient error toast in the focused window.</summary>
    void ShowError(string message);

    /// <summary>Posts a notification: it lands in the inbox and pops a transient, self-dismissing toast.</summary>
    void ShowNotification(string message);

    /// <summary>
    /// Posts a notification carrying an "Open" link that activates <paramref name="tab"/> (bringing its
    /// window to the front and selecting it). Used when a page produces a result while it isn't the active
    /// tab — e.g. the console reporting a background command that finished after the user moved on.
    /// </summary>
    void ShowNotification(string message, Page tab);

    /// <summary>
    /// Inserts <paramref name="text"/> into the AI input bar at the caret (focusing it). The single route
    /// behind drag-to-insert, paste-into-bar, and any feature that wants to seed the bar — so insertion
    /// behaviour lives in one place rather than each feature poking the bar.
    /// </summary>
    void InsertChatInput(string text);

    /// <summary>
    /// Runs <paramref name="query"/> through the AI pipeline as if it had been submitted in the input bar,
    /// in the context of the active page. Used by the terminal's command/query routing: a line that isn't a
    /// shell command is handed to the model (which can then run commands via the page's tools).
    /// </summary>
    void SubmitAiQuery(string query);

    // ── Shell-level overlays (window-modal, routed to the focused window) ────

    /// <summary>
    /// Shows a window-level text-input overlay pre-filled with <paramref name="initialValue"/>.
    /// The overlay belongs to the focused window's shell, not any particular tab.
    /// </summary>
    void ShowPrompt(string title, string label, string initialValue,
                    Action<string> onConfirm, Action onCancel);

    /// <summary>
    /// Shows a window-level yes/no confirmation overlay in the focused window.
    /// </summary>
    void ShowConfirmation(string title, string message, Action onConfirm, Action onCancel);

    /// <summary>
    /// Async form of <see cref="ShowConfirmation"/>: shows the confirmation overlay and completes with
    /// true (confirmed) or false (cancelled). Lets a tool's <c>InvokeAsync</c> await the user's choice.
    /// Marshals to the UI thread.
    /// </summary>
    Task<bool> ConfirmAsync(string title, string message, CancellationToken ct = default);

    /// <summary>
    /// Overload of <see cref="ConfirmAsync(string,string,CancellationToken)"/> with custom button captions
    /// (e.g. <c>"Import"</c> / <c>"Do not Import"</c>). Null/blank falls back to "Confirm" / "Cancel".
    /// </summary>
    Task<bool> ConfirmAsync(string title, string message, string? confirmLabel, string? cancelLabel,
                            CancellationToken ct = default);

    /// <summary>
    /// Shows the shell's file picker and returns the chosen existing file's full path, or null if
    /// cancelled. Reuses the shell's themed picker window — features must use this rather than a
    /// <c>Microsoft.Win32</c> dialog. <paramref name="extensions"/> (e.g. <c>[".reg"]</c>) limits the
    /// selectable files; null/empty offers any. Marshals to the UI thread.
    /// </summary>
    Task<string?> PickOpenFileAsync(IReadOnlyList<string>? extensions = null, string? initialPath = null);

    /// <summary>
    /// Shows the shell's themed folder picker and returns the chosen folder path, or null if cancelled.
    /// Use this rather than a <c>Microsoft.Win32</c> dialog. Marshals to the UI thread.
    /// </summary>
    Task<string?> PickFolderAsync(string? initialPath = null);

    /// <summary>
    /// Shows the shell's save-target picker and returns the chosen full destination path (folder +
    /// file name), or null if cancelled. Reuses the shell's themed picker window — features must use
    /// this rather than a <c>Microsoft.Win32</c> dialog. <paramref name="defaultFileName"/> pre-fills
    /// the name; <paramref name="extensions"/> is advisory (the first is appended if the name has no
    /// extension). Marshals to the UI thread.
    /// </summary>
    Task<string?> PickSaveFileAsync(string defaultFileName, IReadOnlyList<string>? extensions = null,
                                    string? initialPath = null);

    /// <summary>
    /// Refreshes the focused window's active page (re-initialises it with its current params).
    /// Used by actions that mutate the file system from a shell-level overlay — e.g. rename / delete —
    /// to re-enumerate the file list once the change is committed.
    /// </summary>
    void RequestRefresh();

    /// <summary>
    /// Marks <paramref name="folderPath"/> as having an in-flight mutation (e.g. a worktree deletion). While
    /// marked, any file browser showing that folder displays <paramref name="message"/> in place of its file
    /// list and holds off refreshing. Dispose the returned handle when the mutation finishes — browsers then
    /// re-evaluate the folder (refresh, or navigate up if it's gone). Ref-counted per path; safe to dispose
    /// from any thread. Lets a feature run a long mutation off the UI thread without blocking the user, who
    /// can keep navigating and even queue more mutations.
    /// </summary>
    IDisposable MarkFolderBusy(string folderPath, string message);

    /// <summary>The busy message if a mutation is in flight on <paramref name="folderPath"/>, else null.</summary>
    string? GetFolderBusyMessage(string folderPath);

    /// <summary>Raised on the UI thread whenever any folder's busy state changes (marked or cleared).
    /// A file browser re-checks <see cref="GetFolderBusyMessage"/> for its current folder in response.</summary>
    event Action? FolderBusyChanged;

    /// <summary>
    /// Moves a folder to a new location on a background thread as a SAFE move (copy-then-delete, so a file
    /// held open elsewhere can't corrupt the move — the destination gets a complete copy before the source
    /// is removed). While it runs, the source folder and both parents are marked busy via
    /// <see cref="MarkFolderBusy"/>, so any file browser showing them shows a "please wait" panel and
    /// refreshes when it clears. <paramref name="onComplete"/> is invoked on the UI thread with
    /// <c>true</c> on success. Used for the projects archive / shelf / reactivate relocations.
    /// </summary>
    void MoveFolderInBackground(string sourcePath, string destinationPath, string busyMessage,
                                Action<bool>? onComplete = null);

    /// <summary>
    /// Persists a global feature config (an <see cref="IFeatureConfig"/>) to its on-disk store —
    /// the same versioned-JSON location the Options panel writes. Lets a feature commit a config
    /// change made outside the Options flow (e.g. a wizard adding an external-app mapping) without
    /// referencing the Core <c>ConfigManager</c>.
    /// </summary>
    void SaveFeatureConfig(IFeatureConfig config);

    /// <summary>
    /// Opens the focused window's Options panel, landing on the section whose
    /// <see cref="IFeatureConfig.ConfigName"/> matches <paramref name="configName"/> (e.g.
    /// <c>"externalapps"</c>). Lets a feature deep-link the user to a config editor — the
    /// editor itself decides whether to pre-select a specific item.
    /// </summary>
    void OpenOptions(string configName);

    /// <summary>
    /// Opens the per-workspace Configure overlay for this workspace, landing on the section
    /// whose <see cref="IFeatureConfig.ConfigName"/> matches <paramref name="configName"/> (e.g.
    /// <c>"console"</c>). The workspace-scoped sibling of <see cref="OpenOptions"/> — for configs marked
    /// <c>[WorkspaceScopedConfig]</c>, which live in the Configure overlay rather than global Options.
    /// </summary>
    void OpenWorkspaceConfig(string configName);

    /// <summary>
    /// Shows <paramref name="overlayViewModel"/> as a shell-modal overlay in the focused window. The shell
    /// renders it lazily via a <c>DataTemplate</c> matched on the VM's type — ship that template in a
    /// <see cref="System.Windows.ResourceDictionary"/> advertised through your feature's
    /// <see cref="IThemeContribution"/>. Implement <see cref="IShellOverlay"/> on the VM to opt into
    /// backdrop-click dismissal. Dismiss via <see cref="CloseOverlay"/> or the VM's own command.
    /// </summary>
    void ShowOverlay(object overlayViewModel);

    /// <summary>Closes the active shell overlay in the focused window (whatever <see cref="ShowOverlay"/>
    /// last showed).</summary>
    void CloseOverlay();

    /// <summary>
    /// Pins <paramref name="payload"/> to the focused window's ribbon using the
    /// <see cref="Nexaflow.Features.Common.IRibbonPinHandler"/> that accepts the drag-data
    /// <paramref name="format"/>.
    /// </summary>
    void PinToRibbon(string format, object payload);

    /// <summary>
    /// Dispatches <paramref name="obj"/> to the first registered
    /// <see cref="IGenericObjectHandler"/> whose <c>CanHandleObject</c> returns true, and
    /// invokes its <c>Handle</c>. Returns true if a handler claimed it, false if none did
    /// (the caller can then fall back, e.g. open in the OS browser). Runs on the UI thread.
    /// Lets a feature act on an object owned by another feature without a direct reference —
    /// e.g. the scratchpad opens a clicked file/URL link without knowing the file-system or
    /// web features exist.
    /// </summary>
    bool HandleObject(object obj);

    /// <summary>
    /// Enumerates every concrete, non-abstract type across the loaded
    /// <c>Nexaflow.*</c> assemblies (Core + each <c>Nexaflow.Features.*</c>)
    /// that is assignable to <typeparamref name="TInterface"/>.
    /// </summary>
    /// <remarks>
    /// Central point for cross-assembly type discovery so feature services
    /// (e.g. file-system action / viewlet registries) don't each duplicate
    /// the reflection walk. Assumes <c>FeatureManager.RegisterFeatures</c>
    /// has already loaded every feature DLL into the AppDomain.
    /// </remarks>
    IEnumerable<Type> DiscoverImplementations<TInterface>();

    /// <summary>
    /// The feature-provided extractor that understands <paramref name="path"/>'s format, or null when none
    /// claims it — the caller then reads the file as plain text.
    /// <para>
    /// A specific accessor rather than a general "construct this type for me", because the shell keeps
    /// ownership: instances are built through the feature DI (so an extractor may take
    /// <see cref="IShellServices"/>, an <c>IAIService</c> or its own <c>IFeatureConfig</c>), cached per
    /// workspace, and released when that workspace is reconfigured or disposed. A page ViewModel doesn't own
    /// its own lifetime and must not be handed responsibility for anyone else's, so <b>do not dispose or
    /// retain the returned instance</b> — ask again per file; the lookup is a cache hit.
    /// </para>
    /// </summary>
    Nexaflow.Features.Common.Search.IFileTextExtractor? GetFileTextExtractor(string path);

    /// <summary>
    /// Performs an admin-only <paramref name="request"/> by launching the out-of-process privilege bridge
    /// elevated via UAC — one prompt per call; batch several operations into one request to use a single
    /// prompt. The host itself stays non-elevated (<c>asInvoker</c>); the request and its argument values
    /// travel over a private named pipe, never on the command line.
    /// <para>
    /// Never throws for an expected outcome: a declined UAC returns a result with
    /// <see cref="ElevatedErrorKind.UserDeclinedElevation"/> (check <see cref="ElevatedResult.WasDeclined"/>);
    /// a failed operation returns <see cref="ElevatedResult.Success"/> = false with the relevant error kind.
    /// Safe to call off the UI thread.
    /// </para>
    /// </summary>
    Task<ElevatedResult> RunElevatedAsync(ElevatedRequest request, CancellationToken ct = default);
}
