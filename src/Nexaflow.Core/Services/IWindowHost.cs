using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.Windows;

namespace Nexaflow.Core.Services;

/// <summary>
/// Represents a single shell window from the perspective of <see cref="ShellServices"/>.
/// Implemented by <see cref="ViewModels.ShellViewModel"/>.
/// </summary>
internal interface IWindowHost
{
    IReadOnlyList<TabEntry> Tabs { get; }

    bool IsFocused { get; set; }

    /// <summary>The underlying WPF window — used for point-in-window hit testing.</summary>
    Window Window { get; }

    /// <summary>Prepends the tab and makes it active.</summary>
    void AddTab(TabEntry tab);

    /// <summary>
    /// Removes the tab.  If it was active, activates the adjacent tab
    /// (or clears the page area if the window is now empty).
    /// </summary>
    void RemoveTab(TabEntry tab);

    /// <summary>Moves the tab to position 0 in the strip without changing the active tab.</summary>
    void BringToFront(TabEntry tab);

    /// <summary>Deactivates all tabs, marks this one active, and loads its page content.</summary>
    void SetActiveTab(TabEntry tab);

    /// <summary>
    /// Syncs the breadcrumb bar to <paramref name="tab"/>'s segments.
    /// No-op when <paramref name="tab"/> is not the active tab of this window.
    /// </summary>
    void RefreshBreadcrumbs(TabEntry tab);

    /// <summary>Shows a transient error toast in this window.</summary>
    void ShowError(string message);

    /// <summary>Adds a persistent notification in this window.</summary>
    void ShowNotification(string message);
}
