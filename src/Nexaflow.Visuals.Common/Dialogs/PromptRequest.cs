using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace Nexaflow.Visuals.Common.Dialogs;

/// <summary>
/// The text-input sibling of <see cref="ConfirmationRequest"/>: a title, a label, an editable
/// <see cref="Value"/> and one-shot OK / Cancel. Self-closing the same way — either command flips
/// <see cref="IsOpen"/> and fires its callback once, so a stale callback can never run against a later
/// question. The shell's window-modal prompt (<c>IShellServices.ShowPrompt</c>) renders one.
/// </summary>
public sealed partial class PromptRequest : ObservableObject
{
    private readonly Action<string> _onConfirm;
    private readonly Action?        _onCancel;

    public PromptRequest(string title, string label, string initialValue,
                         Action<string> onConfirm, Action? onCancel = null)
    {
        Title      = title;
        Label      = label;
        Value      = initialValue;
        _onConfirm = onConfirm;
        _onCancel  = onCancel;
    }

    public string Title { get; }
    public string Label { get; }

    /// <summary>The text being edited — two-way bound by the prompt's box, handed to the OK callback.</summary>
    [ObservableProperty] private string _value = string.Empty;

    /// <summary>True until either button runs.</summary>
    [ObservableProperty] private bool _isOpen = true;

    [RelayCommand]
    private void Confirm()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _onConfirm(Value);
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _onCancel?.Invoke();
    }
}
