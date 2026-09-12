using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Visuals.Common.Dialogs;

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
public sealed class OverlayCoordinator : ObservableObject
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
            ?? (object?)Confirmation
            ?? Prompt;

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
        else if (Confirmation is { } c)   c.CancelCommand.Execute(null);
        else if (Prompt is { } p)         p.CancelCommand.Execute(null);
    }

    // ── Confirmation modal ────────────────────────────────────────────────
    // A window-level yes/no overlay (ribbon right-click Delete, IShellServices.ShowConfirmation /
    // ConfirmAsync, …). The request is the whole state — it closes itself and fires its callback once — so
    // this only holds the open one, and drops it the moment it is answered.

    /// <summary>The open confirmation, or null. The overlay host renders it by its type's DataTemplate.</summary>
    public ConfirmationRequest? Confirmation { get; private set; }

    public bool ConfirmationVisible => Confirmation is not null;

    public void ShowConfirmation(string title, string prompt, Action onConfirm, Action? onCancel = null,
                                 string? confirmLabel = null, string? cancelLabel = null)
    {
        // A new question supersedes an open one, which is answered Cancel — so an awaiting ConfirmAsync
        // completes rather than hanging on a dialog nobody can reach any more.
        Confirmation?.CancelCommand.Execute(null);

        ConfirmationRequest? request = null;
        request = new ConfirmationRequest(title, prompt,
            onConfirm: () => { CloseConfirmation(request!); onConfirm(); },
            onCancel:  () => { CloseConfirmation(request!); onCancel?.Invoke(); },
            confirmLabel: string.IsNullOrWhiteSpace(confirmLabel) ? "Confirm" : confirmLabel,
            cancelLabel:  string.IsNullOrWhiteSpace(cancelLabel)  ? "Cancel"  : cancelLabel);
        SetConfirmation(request);
    }

    // Runs before the caller's callback, so a callback that asks again opens a fresh question.
    private void CloseConfirmation(ConfirmationRequest request)
    {
        if (ReferenceEquals(Confirmation, request)) SetConfirmation(null);
    }

    private void SetConfirmation(ConfirmationRequest? request)
    {
        Confirmation = request;
        OnPropertyChanged(nameof(Confirmation));
        OnPropertyChanged(nameof(ConfirmationVisible));
        Sync();
    }

    // ── Input-prompt modal ────────────────────────────────────────────────
    // Window-level text-input prompt; the destination for IShellServices.ShowPrompt. Same shape as the
    // confirmation: the request owns its editable Value and its one-shot OK / Cancel.

    /// <summary>The open prompt, or null. Its template two-way-binds the request's own <c>Value</c>.</summary>
    public PromptRequest? Prompt { get; private set; }

    public bool PromptVisible => Prompt is not null;

    public void ShowPrompt(string title, string label, string initialValue,
                           Action<string> onConfirm, Action? onCancel = null)
    {
        Prompt?.CancelCommand.Execute(null);

        PromptRequest? request = null;
        request = new PromptRequest(title, label, initialValue,
            onConfirm: value => { ClosePrompt(request!); onConfirm(value); },
            onCancel:  ()    => { ClosePrompt(request!); onCancel?.Invoke(); });
        SetPrompt(request);
    }

    private void ClosePrompt(PromptRequest request)
    {
        if (ReferenceEquals(Prompt, request)) SetPrompt(null);
    }

    private void SetPrompt(PromptRequest? request)
    {
        Prompt = request;
        OnPropertyChanged(nameof(Prompt));
        OnPropertyChanged(nameof(PromptVisible));
        Sync();
    }
}
