using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nexaflow.Core.ViewModels.Overlays;

/// <summary>
/// Owns the shell's window-modal overlay state: the single overlay host's content
/// (<see cref="ActiveOverlay"/>, rendered by type-matched DataTemplates), feature-pushed overlays
/// (<c>IShellServices.ShowOverlay</c>), and the built-in confirmation / input-prompt modals.
/// <para>
/// The Options / Manage-AI <b>flags</b> stay on <see cref="ShellViewModel"/> — they gate workspace
/// switching and deep links — and the shell pushes those lazily-built panels in via
/// <see cref="OptionsPanel"/> / <see cref="WorkspaceConfigPanel"/>; this class derives what the host
/// shows. <see cref="ShellViewModel"/> exposes thin forwarders and re-raises this object's
/// <c>PropertyChanged</c> under the same names, so existing XAML bindings and callers are unchanged.
/// </para>
/// </summary>
public sealed partial class OverlayCoordinator : ObservableObject
{
    private readonly Func<bool> _optionsOpen;
    private readonly Func<bool> _workspaceConfigOpen;
    private readonly Action     _closeOptions;
    private readonly Action     _closeWorkspaceConfig;

    public OverlayCoordinator(Func<bool> optionsOpen, Func<bool> workspaceConfigOpen,
                              Action closeOptions, Action closeWorkspaceConfig)
    {
        _optionsOpen          = optionsOpen;
        _workspaceConfigOpen  = workspaceConfigOpen;
        _closeOptions         = closeOptions;
        _closeWorkspaceConfig = closeWorkspaceConfig;
    }

    // ── Overlay host ──────────────────────────────────────────────────────

    /// <summary>The content view-model shown in the shell's overlay host, or null when nothing is open.</summary>
    public object? ActiveOverlay { get; private set; }

    private object? _featureOverlay;
    private object? _confirmationOverlay;
    private object? _promptOverlay;
    private object? _optionsPanel;
    private object? _workspaceConfigPanel;

    /// <summary>The lazily-built Options panel; set by the shell when its flag flips.</summary>
    public object? OptionsPanel { get => _optionsPanel; set { _optionsPanel = value; Sync(); } }

    /// <summary>The lazily-built Configure/Manage-AI panel; set by the shell when its flag flips.</summary>
    public object? WorkspaceConfigPanel { get => _workspaceConfigPanel; set { _workspaceConfigPanel = value; Sync(); } }

    /// <summary>True while a feature-pushed overlay (not a built-in modal) is showing.</summary>
    public bool HasFeatureOverlay => _featureOverlay is not null;

    private void Sync()
    {
        object? next =
            _featureOverlay
            ?? (_optionsOpen()         ? _optionsPanel         : null)
            ?? (_workspaceConfigOpen() ? _workspaceConfigPanel : null)
            ?? (ConfirmationVisible    ? _confirmationOverlay  : null)
            ?? (PromptVisible          ? _promptOverlay        : null);

        if (ReferenceEquals(next, ActiveOverlay)) return;
        ActiveOverlay = next;
        OnPropertyChanged(nameof(ActiveOverlay));
    }

    /// <summary>Shows a feature-supplied overlay view-model (rendered by a DataTemplate the feature ships
    /// via its IThemeContribution). Backs <c>IShellServices.ShowOverlay</c>.</summary>
    public void ShowOverlay(object overlayViewModel)
    {
        _featureOverlay = overlayViewModel;
        Sync();
    }

    /// <summary>Closes whatever overlay is currently active (feature overlay or the built-in modal).</summary>
    public void CloseOverlay()
    {
        if (_featureOverlay is not null) { _featureOverlay = null; Sync(); return; }
        if (_optionsOpen())               _closeOptions();
        else if (_workspaceConfigOpen())  _closeWorkspaceConfig();
        else if (ConfirmationVisible)     CancelShellConfirmation();
        else if (PromptVisible)           CancelShellPrompt();
    }

    // ── Confirmation modal ────────────────────────────────────────────────
    // A window-level yes/no overlay (ribbon right-click Delete, IShellServices.ShowConfirmation, …).

    [ObservableProperty] private bool   _confirmationVisible;
    [ObservableProperty] private string _confirmationTitle  = string.Empty;
    [ObservableProperty] private string _confirmationPrompt = string.Empty;

    private Action? _confirmationOnConfirm;
    private Action? _confirmationOnCancel;
    private string  _confirmationConfirmLabel = "Confirm";
    private string  _confirmationCancelLabel  = "Cancel";

    public void ShowConfirmation(string title, string prompt, Action onConfirm, Action? onCancel = null,
                                 string? confirmLabel = null, string? cancelLabel = null)
    {
        ConfirmationTitle         = title;
        ConfirmationPrompt        = prompt;
        _confirmationConfirmLabel = string.IsNullOrWhiteSpace(confirmLabel) ? "Confirm" : confirmLabel!;
        _confirmationCancelLabel  = string.IsNullOrWhiteSpace(cancelLabel)  ? "Cancel"  : cancelLabel!;
        _confirmationOnConfirm    = onConfirm;
        _confirmationOnCancel     = onCancel;
        ConfirmationVisible       = true;
    }

    [RelayCommand]
    private void ConfirmShellConfirmation()
    {
        ConfirmationVisible = false;
        var cb = _confirmationOnConfirm;
        _confirmationOnConfirm = _confirmationOnCancel = null;
        cb?.Invoke();
    }

    [RelayCommand]
    private void CancelShellConfirmation()
    {
        ConfirmationVisible = false;
        var cb = _confirmationOnCancel;
        _confirmationOnConfirm = _confirmationOnCancel = null;
        cb?.Invoke();
    }

    partial void OnConfirmationVisibleChanged(bool value)
    {
        // Title/Prompt/labels are set before ConfirmationVisible flips true (see ShowConfirmation).
        _confirmationOverlay = value
            ? new ConfirmationOverlay
              {
                  Title          = ConfirmationTitle,
                  Prompt         = ConfirmationPrompt,
                  ConfirmCommand = ConfirmShellConfirmationCommand,
                  CancelCommand  = CancelShellConfirmationCommand,
                  ConfirmLabel   = _confirmationConfirmLabel,
                  CancelLabel    = _confirmationCancelLabel,
              }
            : null;
        Sync();
    }

    // ── Input-prompt modal ────────────────────────────────────────────────
    // Window-level text-input prompt; the destination for IShellServices.ShowPrompt.

    [ObservableProperty] private bool   _promptVisible;
    [ObservableProperty] private string _promptTitle = string.Empty;
    [ObservableProperty] private string _promptLabel = string.Empty;
    [ObservableProperty] private string _promptValue = string.Empty;

    private Action<string>? _promptOnConfirm;
    private Action?         _promptOnCancel;

    public void ShowPrompt(string title, string label, string initialValue,
                           Action<string> onConfirm, Action? onCancel = null)
    {
        PromptTitle      = title;
        PromptLabel      = label;
        PromptValue      = initialValue;
        _promptOnConfirm = onConfirm;
        _promptOnCancel  = onCancel;
        PromptVisible    = true;
    }

    [RelayCommand]
    private void ConfirmShellPrompt()
    {
        PromptVisible = false;
        var value = PromptValue;
        var cb    = _promptOnConfirm;
        _promptOnConfirm = null;
        _promptOnCancel  = null;
        cb?.Invoke(value);
    }

    [RelayCommand]
    private void CancelShellPrompt()
    {
        PromptVisible = false;
        var cb = _promptOnCancel;
        _promptOnConfirm = null;
        _promptOnCancel  = null;
        cb?.Invoke();
    }

    partial void OnPromptVisibleChanged(bool value)
    {
        // The editable PromptValue stays on this object; the template two-way-binds to it via the window.
        _promptOverlay = value
            ? new PromptOverlay
              {
                  Title          = PromptTitle,
                  Label          = PromptLabel,
                  ConfirmCommand = ConfirmShellPromptCommand,
                  CancelCommand  = CancelShellPromptCommand,
              }
            : null;
        Sync();
    }
}
