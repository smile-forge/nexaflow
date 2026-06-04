using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    /// </summary>
    void OpenTab(string pageKind, Dictionary<string, string>? pageParams = null,
                 IPageView? caller = null);

    /// <summary>Closes and removes a tab from the global tab registry.</summary>
    void CloseTab(Page tab);

    /// <summary>
    /// Lightweight <see cref="Page"/> definitions for the pages that can be created without specific
    /// context (<see cref="IPageRegistration.CanBeContextItem"/>) in this workspace — surfaced in the
    /// AI conversation's "add context" menu, which reads each page's Title/Icon. Built per-workspace,
    /// so it reflects the active profile's enablement (e.g. Projects only when enabled). Content is not
    /// realized; pin a chosen page by calling <c>GetOrCreateContent</c> on it.
    /// </summary>
    IReadOnlyList<Page> GetContextItemPages();

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
    /// Returns the first globally-open tab whose <see cref="Page.PageKind"/>
    /// matches and whose params are compatible (see param-matching rules), or null.
    /// </summary>
    Page? FindTab(string pageKind, Dictionary<string, string>? pageParams = null);

    /// <summary>Shows a transient error toast in the focused window.</summary>
    void ShowError(string message);

    /// <summary>Adds a persistent notification in the focused window.</summary>
    void ShowNotification(string message);

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
    /// Requests a refresh of any view that cares (e.g. a file list).
    /// No-op at the shell level — kept for callers whose work is now driven by
    /// file-system watchers in the view layer.
    /// </summary>
    void RequestRefresh();

    /// <summary>
    /// Pins <paramref name="payload"/> to the focused window's ribbon using the
    /// <see cref="Nexaflow.Features.Common.Ribbon.IRibbonPinHandler"/> registered for
    /// <paramref name="contentKind"/>.
    /// </summary>
    void PinToRibbon(string contentKind, object payload);

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
}
