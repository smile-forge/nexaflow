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
/// Self-contained ribbon component: owns its <see cref="RibbonViewModel"/>, applies the profile
/// frame styling (bevel + accent + selector), and renders the items via <see cref="RibbonBar"/>.
/// The shell hands it the active <see cref="Workspace"/> (for pin handlers), its <see cref="Profile"/>
/// (which drives the shared ribbon layout + live-sync), and an <see cref="OpenPageCommand"/>.
/// </summary>
public partial class RibbonControl : UserControl
{
    public RibbonViewModel ViewModel { get; } = new();

    // ── Public DPs (the shell's interface to the ribbon) ───────────────────

    public static readonly DependencyProperty WorkspaceProperty =
        DependencyProperty.Register(nameof(Workspace), typeof(Workspace), typeof(RibbonControl),
            new PropertyMetadata(null, OnWorkspaceChanged));

    /// <summary>The active workspace's profile — drives the shared ribbon layout + live-sync.</summary>
    public static readonly DependencyProperty ProfileProperty =
        DependencyProperty.Register(nameof(Profile), typeof(Profile), typeof(RibbonControl),
            new PropertyMetadata(null, OnProfileChanged));

    public static readonly DependencyProperty ProfilesProperty =
        DependencyProperty.Register(nameof(Profiles), typeof(IEnumerable), typeof(RibbonControl));

    public static readonly DependencyProperty SelectProfileCommandProperty =
        DependencyProperty.Register(nameof(SelectProfileCommand), typeof(ICommand), typeof(RibbonControl));

    public static readonly DependencyProperty CanSwitchProfileProperty =
        DependencyProperty.Register(nameof(CanSwitchProfile), typeof(bool), typeof(RibbonControl),
            new PropertyMetadata(true));

    public static readonly DependencyProperty OpenPageCommandProperty =
        DependencyProperty.Register(nameof(OpenPageCommand), typeof(ICommand), typeof(RibbonControl));

    public static readonly DependencyProperty OpenPageInNewWindowCommandProperty =
        DependencyProperty.Register(nameof(OpenPageInNewWindowCommand), typeof(ICommand), typeof(RibbonControl));

    public static readonly DependencyProperty ShellProperty =
        DependencyProperty.Register(nameof(Shell), typeof(IShellServices), typeof(RibbonControl));

    public static readonly DependencyProperty PinFromHandlerCommandProperty =
        DependencyProperty.Register(nameof(PinFromHandlerCommand), typeof(ICommand), typeof(RibbonControl));

    public Workspace? Workspace
    {
        get => (Workspace?)GetValue(WorkspaceProperty);
        set => SetValue(WorkspaceProperty, value);
    }

    public Profile? Profile
    {
        get => (Profile?)GetValue(ProfileProperty);
        set => SetValue(ProfileProperty, value);
    }

    public IEnumerable? Profiles
    {
        get => (IEnumerable?)GetValue(ProfilesProperty);
        set => SetValue(ProfilesProperty, value);
    }

    public ICommand? SelectProfileCommand
    {
        get => (ICommand?)GetValue(SelectProfileCommandProperty);
        set => SetValue(SelectProfileCommandProperty, value);
    }

    public bool CanSwitchProfile
    {
        get => (bool)GetValue(CanSwitchProfileProperty);
        set => SetValue(CanSwitchProfileProperty, value);
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

    private static void OnWorkspaceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((RibbonControl)d).ViewModel.SetWorkspace(e.NewValue as Workspace);

    private static void OnProfileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((RibbonControl)d).ViewModel.SetProfile(e.NewValue as Profile);
}
