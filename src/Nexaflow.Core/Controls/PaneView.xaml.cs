using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Controls;

/// <summary>
/// Renders one <see cref="Pane"/>: tab strip + breadcrumb + active page content.
/// The shell composes one or more of these — today one root pane per window;
/// in future a SplitPaneView hosting two.
/// </summary>
public partial class PaneView : UserControl
{
    // ── Dependency properties ──────────────────────────────────────────────

    public static readonly DependencyProperty PaneProperty =
        DependencyProperty.Register(nameof(Pane), typeof(Pane), typeof(PaneView),
            new PropertyMetadata(null, OnPaneChanged));

    public static readonly DependencyProperty ActivateTabCommandProperty =
        DependencyProperty.Register(nameof(ActivateTabCommand), typeof(ICommand), typeof(PaneView));

    public static readonly DependencyProperty CloseTabCommandProperty =
        DependencyProperty.Register(nameof(CloseTabCommand), typeof(ICommand), typeof(PaneView));

    public static readonly DependencyProperty PinTabToRibbonCommandProperty =
        DependencyProperty.Register(nameof(PinTabToRibbonCommand), typeof(ICommand), typeof(PaneView));

    public static readonly DependencyProperty TearOffTabCommandProperty =
        DependencyProperty.Register(nameof(TearOffTabCommand), typeof(ICommand), typeof(PaneView));

    public static readonly DependencyProperty ReceiveTabCommandProperty =
        DependencyProperty.Register(nameof(ReceiveTabCommand), typeof(ICommand), typeof(PaneView));

    public static readonly DependencyProperty OpenPageCommandProperty =
        DependencyProperty.Register(nameof(OpenPageCommand), typeof(ICommand), typeof(PaneView));

    public Pane? Pane
    {
        get => (Pane?)GetValue(PaneProperty);
        set => SetValue(PaneProperty, value);
    }

    public ICommand? ActivateTabCommand
    {
        get => (ICommand?)GetValue(ActivateTabCommandProperty);
        set => SetValue(ActivateTabCommandProperty, value);
    }

    public ICommand? CloseTabCommand
    {
        get => (ICommand?)GetValue(CloseTabCommandProperty);
        set => SetValue(CloseTabCommandProperty, value);
    }

    public ICommand? PinTabToRibbonCommand
    {
        get => (ICommand?)GetValue(PinTabToRibbonCommandProperty);
        set => SetValue(PinTabToRibbonCommandProperty, value);
    }

    public ICommand? TearOffTabCommand
    {
        get => (ICommand?)GetValue(TearOffTabCommandProperty);
        set => SetValue(TearOffTabCommandProperty, value);
    }

    public ICommand? ReceiveTabCommand
    {
        get => (ICommand?)GetValue(ReceiveTabCommandProperty);
        set => SetValue(ReceiveTabCommandProperty, value);
    }

    public ICommand? OpenPageCommand
    {
        get => (ICommand?)GetValue(OpenPageCommandProperty);
        set => SetValue(OpenPageCommandProperty, value);
    }

    // ── Wiring ─────────────────────────────────────────────────────────────

    public PaneView()
    {
        InitializeComponent();
        BreadcrumbBarControl.OpenTabRequested += (pageKind, pageParams) =>
        {
            var req = new OpenPageRequest(pageKind, pageParams);
            if (OpenPageCommand?.CanExecute(req) == true)
                OpenPageCommand.Execute(req);
        };
    }

    private static void OnPaneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (PaneView)d;
        if (e.OldValue is Pane oldPane)
            oldPane.PropertyChanged -= view.OnPanePropertyChanged;
        if (e.NewValue is Pane newPane)
        {
            newPane.PropertyChanged += view.OnPanePropertyChanged;
            view.UpdateContent();
        }
        else
        {
            view.ContentHost.Content = null;
        }
    }

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.Pane.ActivePage))
            UpdateContent();
    }

    private void UpdateContent()
    {
        ContentHost.Content = Pane?.ActivePage?.GetOrCreateContent();
    }
}
