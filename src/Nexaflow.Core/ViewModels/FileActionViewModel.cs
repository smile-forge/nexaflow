using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Core.ViewModels;
/// <summary>
/// Wraps an <see cref="IFileAction"/> for presentation in the action strip.
/// Shift-held state is forwarded to <see cref="IFileAction.PerformAction(string, bool)"/>
/// as the <c>force</c> flag so individual actions can decide how to handle it.
/// </summary>
public partial class FileActionViewModel : ObservableObject
{
    private readonly IFileAction _action;
    private IReadOnlyList<string> _paths = [];

    /// <summary>Invoked after a successful action so the view can refresh the file list / tree.</summary>
    public event Action? ActionExecuted;

    /// <summary>Raised immediately after the action executes — dims all other buttons.</summary>
    public event Action<FileActionViewModel>? FlashBegan;

    /// <summary>Raised after the flash period ends for non-refresh actions — restores other buttons.</summary>
    public event Action<FileActionViewModel>? FlashEnded;

    public string Icon          => _action.Icon;
    public string DisplayName   => _action.DisplayName;
    public bool   IsDestructive => _action.IsDestructive;
    public ImageSource? IconImage => _action.IconImage;
    /// <summary>
    /// Returns <see cref="IFileAction.Tooltip"/> when set, otherwise <see cref="DisplayName"/>.
    /// Bound to the button's ToolTip in the view.
    /// </summary>
    public string ButtonTooltip => _action.Tooltip ?? DisplayName;

    /// <summary>True while the success-tick overlay is shown on this button.</summary>
    [ObservableProperty] private bool _isFlashing;

    /// <summary>True while another button in the strip is flashing (dims this one).</summary>
    [ObservableProperty] private bool _isDimmed;

    // ── Shift state ───────────────────────────────────────────────────────
    [ObservableProperty] private bool _shiftHeld;

    // ── Command ───────────────────────────────────────────────────────────

    [RelayCommand]
    private void Execute()
    {
        if (_paths.Count == 0) return;

        bool force = ShiftHeld
            || Keyboard.IsKeyDown(Key.LeftShift)
            || Keyboard.IsKeyDown(Key.RightShift);

        PerformAction(force);
    }

    private const int FlashDurationMs = 700;

    private async void PerformAction(bool force)
    {
        if (_paths.Count == 1)
            _action.PerformAction(_paths[0], force);
        else
            _action.PerformAction(_paths, force);

        // Show success state and dim all sibling buttons
        IsFlashing = true;
        FlashBegan?.Invoke(this);

        await Task.Delay(FlashDurationMs);

        IsFlashing = false;

        if (_action.RequiresRefresh)
        {
            // Refresh rebuilds the whole strip so no need to restore dim state
            ActionExecuted?.Invoke();
        }
        else
        {
            FlashEnded?.Invoke(this);
        }
    }

    public void UpdatePaths(IReadOnlyList<string> paths) => _paths = paths;

    public FileActionViewModel(IFileAction action) => _action = action;
}
