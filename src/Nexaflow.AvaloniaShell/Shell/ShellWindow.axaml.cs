using Avalonia.Controls;
using Nexaflow.AvaloniaShell.Views;

namespace Nexaflow.AvaloniaShell.Shell;

/// <summary>
/// A shell window: registered with the runtime it shows from the moment it is made, focused with it while active, and gone from it
/// as it closes — what Nexaflow.Core's <c>MainWindow</c> does with the session. Which runtime that is changes when the window moves,
/// so each handler reaches the one it shows now.
/// </summary>
public partial class ShellWindow : Window
{
    private bool _left;

    /// <summary>Only for Avalonia's designer and loader; a shell window is always made over a runtime.</summary>
    public ShellWindow() => InitializeComponent();

    /// <param name="openWorkspaceTabs">Open the workspace's tabs now: a launch's window. A window the session makes (a tear-off, a new
    /// window) is given its tabs by the session.</param>
    internal ShellWindow(WindowServices session, AvaloniaPageViews views, IUiThread ui, WorkspaceRuntime runtime, bool openWorkspaceTabs)
    {
        InitializeComponent();
        Model = new ShellWindowModel(this, session, views, ui, runtime);
        DataContext = Model;

        runtime.ShellServices.RegisterWindow(Model);
        if (openWorkspaceTabs)
        {
            runtime.ShellServices.SetFocused(Model);
            runtime.ShellServices.OpenWorkspaceTabs();
        }

        Activated   += (_, _) => Model.CurrentRuntime.ShellServices.SetFocused(Model);
        Deactivated += (_, _) => Model.CurrentRuntime.ShellServices.ClearFocused(Model);
        Closing     += (_, _) => Leave();
    }

    internal ShellWindowModel Model { get; } = null!;

    private void Leave()
    {
        if (_left) return;
        _left = true;
        Model.CurrentRuntime.ShellServices.UnregisterWindow(Model);
        Model.Detach();
    }
}
