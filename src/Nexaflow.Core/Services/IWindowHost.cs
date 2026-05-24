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
    IReadOnlyList<Page> Tabs { get; }

    bool IsFocused { get; set; }

    /// <summary>The underlying WPF window — used for point-in-window hit testing.</summary>
    Window Window { get; }

    /// <summary>Prepends the tab and makes it active.</summary>
    void AddTab(Page tab);

    /// <summary>
    /// Removes the tab.  If it was active, activates the adjacent tab
    /// (or clears the page area if the window is now empty).
    /// </summary>
    void RemoveTab(Page tab);

    /// <summary>Moves the tab to position 0 in the strip without changing the active tab.</summary>
    void BringToFront(Page tab);

    /// <summary>Deactivates all tabs, marks this one active, and loads its page content.</summary>
    void SetActiveTab(Page tab);

    /// <summary>Shows a transient error toast in this window.</summary>
    void ShowError(string message);

    /// <summary>Adds a persistent notification in this window.</summary>
    void ShowNotification(string message);

    /// <summary>Shows the window-level confirmation overlay (independent of any page).</summary>
    void ShowConfirmation(string title, string prompt, System.Action onConfirm, System.Action? onCancel = null);

    /// <summary>Shows the window-level text-input prompt overlay (independent of any page).</summary>
    void ShowPrompt(string title, string label, string initialValue,
                    System.Action<string> onConfirm, System.Action? onCancel = null);
}
