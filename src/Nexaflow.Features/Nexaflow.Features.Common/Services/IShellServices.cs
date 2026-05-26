using System;
using System.Collections.Generic;

namespace Nexaflow.Features.Common;

/// <summary>
/// Application-level singleton that manages tab lifetime across all windows.
/// Feature code, views, and query handlers call this instead of holding
/// any reference to the shell or window layer.
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
