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
/// Self-contained ribbon component: owns its <see cref="RibbonViewModel"/>, applies
/// the work-context frame styling (bevel + accent + selector), and renders the
/// items via <see cref="RibbonBar"/>. The shell hands it a <see cref="WorkContext"/>
/// and an <see cref="OpenPageCommand"/>; everything else lives inside.
/// </summary>
public partial class RibbonControl : UserControl
{
    public RibbonViewModel ViewModel { get; } = new();

    // ── Public DPs (the shell's interface to the ribbon) ───────────────────

    public static readonly DependencyProperty WorkContextProperty =
        DependencyProperty.Register(nameof(WorkContext), typeof(WorkContext), typeof(RibbonControl),
            new PropertyMetadata(null, OnWorkContextChanged));

    public static readonly DependencyProperty WorkContextsProperty =
        DependencyProperty.Register(nameof(WorkContexts), typeof(IEnumerable), typeof(RibbonControl));

    public static readonly DependencyProperty SelectWorkContextCommandProperty =
        DependencyProperty.Register(nameof(SelectWorkContextCommand), typeof(ICommand), typeof(RibbonControl));

    public static readonly DependencyProperty OpenPageCommandProperty =
        DependencyProperty.Register(nameof(OpenPageCommand), typeof(ICommand), typeof(RibbonControl));

    public static readonly DependencyProperty OpenPageInNewWindowCommandProperty =
        DependencyProperty.Register(nameof(OpenPageInNewWindowCommand), typeof(ICommand), typeof(RibbonControl));

    public static readonly DependencyProperty ShellProperty =
        DependencyProperty.Register(nameof(Shell), typeof(IShellServices), typeof(RibbonControl));

    public WorkContext? WorkContext
    {
        get => (WorkContext?)GetValue(WorkContextProperty);
        set => SetValue(WorkContextProperty, value);
    }

    public IEnumerable? WorkContexts
    {
        get => (IEnumerable?)GetValue(WorkContextsProperty);
        set => SetValue(WorkContextsProperty, value);
    }

    public ICommand? SelectWorkContextCommand
    {
        get => (ICommand?)GetValue(SelectWorkContextCommandProperty);
        set => SetValue(SelectWorkContextCommandProperty, value);
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

    private static void OnWorkContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((RibbonControl)d).ViewModel.SetWorkContext(e.NewValue as WorkContext);
}
