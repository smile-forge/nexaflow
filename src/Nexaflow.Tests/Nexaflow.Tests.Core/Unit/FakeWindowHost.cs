using System.Collections.Generic;
using System.Windows;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Minimal IWindowHost stub for ShellServices unit tests.
/// Window returns null — tests must not exercise code paths that dereference it.
/// </summary>
internal sealed class FakeWindowHost : IWindowHost
{
    private readonly List<Page> _tabs = [];

    public IReadOnlyList<Page> Tabs => _tabs;
    public bool IsFocused { get; set; }
    public Window Window => null!;

    public void AddTab(Page tab) => _tabs.Insert(0, tab);
    public void FocusSecondPane() { }   // no pane model in the stub; AddTab still records the tab
    public void RemoveTab(Page tab) => _tabs.Remove(tab);
    public void BringToFront(Page tab) { }
    public void SetActiveTab(Page tab) { }
    public void ShowError(string message) { }
    public void ShowNotification(string message) { }
    public void ShowConfirmation(string title, string prompt, Action onConfirm, Action? onCancel = null,
                                 string? confirmLabel = null, string? cancelLabel = null) { }
    public void ShowPrompt(string title, string label, string initialValue, Action<string> onConfirm, Action? onCancel = null) { }
    public void AddRibbonPin(RibbonPinRequest request) { }
    public void InsertChatInput(string text) { }
    public void SubmitAiQuery(string query) { }
}
