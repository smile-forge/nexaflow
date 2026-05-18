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
    void CloseTab(TabEntry tab);

    /// <summary>
    /// Updates a tab's title, breadcrumbs, and/or current params. If the tab is currently
    /// active in its host window the breadcrumb bar is refreshed immediately.
    /// Pages call this to keep <see cref="TabEntry.PageParams"/> in sync with their actual
    /// display state (e.g. after navigation or a refined search).
    /// </summary>
    void UpdateTabMeta(TabEntry tab, string? title = null,
                       IReadOnlyList<BreadcrumbSegment>? breadcrumbs = null,
                       Dictionary<string, string>? pageParams = null);

    /// <summary>
    /// Returns the first globally-open tab whose <see cref="TabEntry.PageKind"/>
    /// matches and whose params are compatible (see param-matching rules), or null.
    /// </summary>
    TabEntry? FindTab(string pageKind, Dictionary<string, string>? pageParams = null);

    /// <summary>Shows a transient error toast in the focused window.</summary>
    void ShowError(string message);

    /// <summary>Adds a persistent notification in the focused window.</summary>
    void ShowNotification(string message);

    // ── Per-view contextual services ─────────────────────────────────────────

    /// <summary>
    /// Shows a text-input overlay pre-filled with <paramref name="initialValue"/>.
    /// No-op on the global singleton; meaningful only on the per-tab implementation.
    /// </summary>
    void ShowPrompt(string title, string label, string initialValue,
                    Action<string> onConfirm, Action onCancel);

    /// <summary>
    /// Shows a yes/no confirmation overlay.
    /// No-op on the global singleton; meaningful only on the per-tab implementation.
    /// </summary>
    void ShowConfirmation(string title, string message, Action onConfirm, Action onCancel);

    /// <summary>
    /// Requests a refresh of the file list and tree in the owning tab.
    /// No-op on the global singleton; meaningful only on the per-tab implementation.
    /// </summary>
    void RequestRefresh();
}
