using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.Models;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Controls;

/// <summary>
/// Self-contained ribbon component: owns its <see cref="RibbonViewModel"/>, applies the workspace
/// frame styling (bevel + accent + selector), and renders the items via <see cref="RibbonBar"/>.
/// The shell hands it the active <see cref="Workspace"/> (for pin handlers), its <see cref="Workspace"/>
/// (which drives the shared ribbon layout + live-sync), and an <see cref="OpenPageCommand"/>.
/// </summary>
public partial class RibbonControl : UserControl
{
    public RibbonViewModel ViewModel { get; } = new();

    // ── Public DPs (the shell's interface to the ribbon) ───────────────────

    public static readonly DependencyProperty RuntimeProperty =
        DependencyProperty.Register(nameof(Runtime), typeof(WorkspaceRuntime), typeof(RibbonControl),
            new PropertyMetadata(null, OnRuntimeChanged));

    /// <summary>The active workspace — drives the shared ribbon layout + live-sync.</summary>
    public static readonly DependencyProperty WorkspaceProperty =
        DependencyProperty.Register(nameof(Workspace), typeof(Workspace), typeof(RibbonControl),
            new PropertyMetadata(null, OnWorkspaceChanged));

    public static readonly DependencyProperty WorkspacesProperty =
        DependencyProperty.Register(nameof(Workspaces), typeof(IEnumerable), typeof(RibbonControl));

    public static readonly DependencyProperty SelectWorkspaceCommandProperty =
        DependencyProperty.Register(nameof(SelectWorkspaceCommand), typeof(ICommand), typeof(RibbonControl));

    /// <summary>Right-click "Configure" on the workspace selector — configures the current workspace.</summary>
    public static readonly DependencyProperty ConfigureWorkspaceCommandProperty =
        DependencyProperty.Register(nameof(ConfigureWorkspaceCommand), typeof(ICommand), typeof(RibbonControl));

    /// <summary>Right-click "New workspace" on the workspace selector — clones the current workspace + configures it.</summary>
    public static readonly DependencyProperty NewWorkspaceCommandProperty =
        DependencyProperty.Register(nameof(NewWorkspaceCommand), typeof(ICommand), typeof(RibbonControl));

    /// <summary>Right-click "Use Tabset as Default" — saves the current tabs/panes as the workspace's startup tabset.</summary>
    public static readonly DependencyProperty UseTabsetAsDefaultCommandProperty =
        DependencyProperty.Register(nameof(UseTabsetAsDefaultCommand), typeof(ICommand), typeof(RibbonControl));

    public static readonly DependencyProperty CanSwitchWorkspaceProperty =
        DependencyProperty.Register(nameof(CanSwitchWorkspace), typeof(bool), typeof(RibbonControl),
            new PropertyMetadata(true));

    public static readonly DependencyProperty OpenPageCommandProperty =
        DependencyProperty.Register(nameof(OpenPageCommand), typeof(ICommand), typeof(RibbonControl));

    public static readonly DependencyProperty OpenPageInNewWindowCommandProperty =
        DependencyProperty.Register(nameof(OpenPageInNewWindowCommand), typeof(ICommand), typeof(RibbonControl));

    public static readonly DependencyProperty ShellProperty =
        DependencyProperty.Register(nameof(Shell), typeof(IShellServices), typeof(RibbonControl));

    public static readonly DependencyProperty PinFromHandlerCommandProperty =
        DependencyProperty.Register(nameof(PinFromHandlerCommand), typeof(ICommand), typeof(RibbonControl));

    public WorkspaceRuntime? Runtime
    {
        get => (WorkspaceRuntime?)GetValue(RuntimeProperty);
        set => SetValue(RuntimeProperty, value);
    }

    public Workspace? Workspace
    {
        get => (Workspace?)GetValue(WorkspaceProperty);
        set => SetValue(WorkspaceProperty, value);
    }

    public IEnumerable? Workspaces
    {
        get => (IEnumerable?)GetValue(WorkspacesProperty);
        set => SetValue(WorkspacesProperty, value);
    }

    public ICommand? SelectWorkspaceCommand
    {
        get => (ICommand?)GetValue(SelectWorkspaceCommandProperty);
        set => SetValue(SelectWorkspaceCommandProperty, value);
    }

    public ICommand? ConfigureWorkspaceCommand
    {
        get => (ICommand?)GetValue(ConfigureWorkspaceCommandProperty);
        set => SetValue(ConfigureWorkspaceCommandProperty, value);
    }

    public ICommand? NewWorkspaceCommand
    {
        get => (ICommand?)GetValue(NewWorkspaceCommandProperty);
        set => SetValue(NewWorkspaceCommandProperty, value);
    }

    public ICommand? UseTabsetAsDefaultCommand
    {
        get => (ICommand?)GetValue(UseTabsetAsDefaultCommandProperty);
        set => SetValue(UseTabsetAsDefaultCommandProperty, value);
    }

    public bool CanSwitchWorkspace
    {
        get => (bool)GetValue(CanSwitchWorkspaceProperty);
        set => SetValue(CanSwitchWorkspaceProperty, value);
    }

    public ICommand? OpenPageCommand
    {
        get => (ICommand?)GetValue(OpenPageCommandProperty);
        set => SetValue(OpenPageCommandProperty, value);
    }

    public ICommand? OpenPageInNewWindowCommand
    {
        get => (ICommand?)GetValue(OpenPageInNewWindowCommandProperty);
        set => SetValue(OpenPageInNewWindowCommandProperty, value);
    }

    /// <summary>Shell services — used to route the Delete confirmation through the window-level overlay.</summary>
    public IShellServices? Shell
    {
        get => (IShellServices?)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public ICommand? PinFromHandlerCommand
    {
        get => (ICommand?)GetValue(PinFromHandlerCommandProperty);
        set => SetValue(PinFromHandlerCommandProperty, value);
    }

    // ── Internal: button-click + context-menu commands ────────────────────

    public ICommand InternalRibbonActionCommand { get; }
    public ICommand InternalOpenInNewWindowCommand { get; }
    public ICommand InternalDeleteCommand { get; }

    public RibbonControl()
    {
        // Commands must be assigned BEFORE InitializeComponent so XAML bindings
        // (RibbonBar.RibbonActionCommand etc.) latch the real instances, not null.
        InternalRibbonActionCommand = new RelayCommand<RibbonItem>(item =>
        {
            if (item is null) return;
            if (item.PageKind is not null)
            {
                if (OpenPageCommand?.CanExecute(item) == true)
                    OpenPageCommand.Execute(item);
            }
            else
            {
                item.Command?.Execute(null);
            }
        });

        InternalOpenInNewWindowCommand = new RelayCommand<RibbonItem>(item =>
        {
            if (item is null) return;
            if (OpenPageInNewWindowCommand?.CanExecute(item) == true)
                OpenPageInNewWindowCommand.Execute(item);
        });

        InternalDeleteCommand = new RelayCommand<RibbonItem>(item =>
        {
            if (item is null) return;
            var label = string.IsNullOrEmpty(item.Label) ? "this button" : $"\"{item.Label}\"";
            Shell?.ShowConfirmation(
                "Delete ribbon button",
                $"Remove {label} from the ribbon?",
                () => ViewModel.Items.Remove(item),
                () => { });
        });

        InitializeComponent();

        ViewModel.FlashItem = item => RibbonBarControl.FlashItem(item);
    }

    private static void OnRuntimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((RibbonControl)d).ViewModel.SetRuntime(e.NewValue as WorkspaceRuntime);

    private static void OnWorkspaceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((RibbonControl)d).ViewModel.SetWorkspace(e.NewValue as Workspace);
}
