using Nexaflow.Core.Models;
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

    /// <summary>Snapshots the window's tabs grouped by pane (0 = primary/left, 1 = the right split) with
    /// each pane's active tab flagged — the shape restored by <see cref="ShellServices.RestoreTabLayout"/>
    /// (or saved as a workspace default). Skips tabs that can't be recreated (no PageKind).</summary>
    IReadOnlyList<DefaultTabDescriptor> CaptureTabLayout();

    bool IsFocused { get; set; }

    /// <summary>The underlying WPF window — used for point-in-window hit testing.</summary>
    Window Window { get; }

    /// <summary>Prepends the tab and makes it active.</summary>
    void AddTab(Page tab);

    /// <summary>
    /// Ensures the tab area is split and makes the second (right) pane the focused one, so the
    /// next <see cref="AddTab"/> lands there. Splits off a new empty right pane if currently unsplit.
    /// </summary>
    void FocusSecondPane();

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

    /// <summary>Shows the window-level confirmation overlay (independent of any page).
    /// <paramref name="confirmLabel"/>/<paramref name="cancelLabel"/> override the button captions
    /// (null/blank → "Confirm"/"Cancel").</summary>
    void ShowConfirmation(string title, string prompt, System.Action onConfirm, System.Action? onCancel = null,
                          string? confirmLabel = null, string? cancelLabel = null);

    /// <summary>Shows the window-level text-input prompt overlay (independent of any page).</summary>
    void ShowPrompt(string title, string label, string initialValue,
                    System.Action<string> onConfirm, System.Action? onCancel = null);

    /// <summary>Pins an item to the ribbon via the handler registered for the request's content kind.</summary>
    void AddRibbonPin(RibbonPinRequest request);

    /// <summary>Inserts text into this window's AI input bar at the caret (focusing it).</summary>
    void InsertChatInput(string text);

    /// <summary>This window's ribbon buttons as quick-open targets (label + an action that opens the item
    /// via the same executor-or-open-tab path as a click). The ribbon is per-window shell chrome, so only a
    /// window can supply it.</summary>
    IReadOnlyList<QuickOpenTarget> GetRibbonQuickOpenTargets();

    /// <summary>Submits a query through this window's AI pipeline (as if typed in the bar).</summary>
    void SubmitAiQuery(string query);
}
