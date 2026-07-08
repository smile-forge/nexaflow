using System;
using System.Windows;

namespace Nexaflow.Visuals.Common;

/// <summary>
/// Watches the hosting window's minimize state for a view whose timers should sleep while nothing
/// is visible. A view's Loaded/Unloaded is the active-tab signal, but a minimized window keeps its
/// visual tree loaded — so without this, per-tab polling/render timers keep running invisibly.
/// Attach from the view's Loaded handler, dispose from Unloaded; the callback receives true on
/// minimize and false on restore.
/// </summary>
public sealed class WindowMinimizeWatcher : IDisposable
{
    private readonly Window?      _window;
    private readonly Action<bool> _onMinimizedChanged;

    public static WindowMinimizeWatcher Attach(DependencyObject view, Action<bool> onMinimizedChanged)
        => new(Window.GetWindow(view), onMinimizedChanged);

    private WindowMinimizeWatcher(Window? window, Action<bool> onMinimizedChanged)
    {
        _window             = window;
        _onMinimizedChanged = onMinimizedChanged;
        if (_window is not null) _window.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, EventArgs e)
        => _onMinimizedChanged(_window!.WindowState == WindowState.Minimized);

    public void Dispose()
    {
        if (_window is not null) _window.StateChanged -= OnStateChanged;
    }
}
