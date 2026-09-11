using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace Nexaflow.Visuals.Common.Dialogs;

/// <summary>
/// The one yes/no model. The shell's window-modal confirmation renders it: a feature asks through
/// <c>IShellServices.ConfirmAsync</c> / <c>ShowConfirmation</c> rather than hosting one, because a question
/// is window-modal and only a form is tab-modal (arch review §E1). Self-closing — Confirm/Cancel flip
/// <see cref="IsOpen"/> and fire the callback once, so a host only assigns a new request and never clears
/// state or declares per-dialog commands. <see cref="PromptRequest"/> is its text-input sibling.
/// </summary>
public sealed partial class ConfirmationRequest : ObservableObject
{
    private readonly Action  _onConfirm;
    private readonly Action? _onCancel;

    public ConfirmationRequest(string title, string prompt, Action onConfirm, Action? onCancel = null,
                               string confirmLabel = "Confirm", string cancelLabel = "Cancel")
    {
        Title        = title;
        Prompt       = prompt;
        ConfirmLabel = confirmLabel;
        CancelLabel  = cancelLabel;
        _onConfirm   = onConfirm;
        _onCancel    = onCancel;
    }

    public string Title        { get; }
    public string Prompt       { get; }
    public string ConfirmLabel { get; }
    public string CancelLabel  { get; }

    /// <summary>True until either button runs; the dialog binds its visibility to this.</summary>
    [ObservableProperty] private bool _isOpen = true;

    [RelayCommand]
    private void Confirm()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _onConfirm();
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _onCancel?.Invoke();
    }
}
