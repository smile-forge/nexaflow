using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Nexaflow.Core.Models;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;

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

    public static readonly DependencyProperty SplitTabCommandProperty =
        DependencyProperty.Register(nameof(SplitTabCommand), typeof(ICommand), typeof(PaneView));

    public static readonly DependencyProperty SplitEmptyCommandProperty =
        DependencyProperty.Register(nameof(SplitEmptyCommand), typeof(ICommand), typeof(PaneView));

    public static readonly DependencyProperty ClosePaneCommandProperty =
        DependencyProperty.Register(nameof(ClosePaneCommand), typeof(ICommand), typeof(PaneView));

    public static readonly DependencyProperty CloseOthersCommandProperty =
        DependencyProperty.Register(nameof(CloseOthersCommand), typeof(ICommand), typeof(PaneView));

    public static readonly DependencyProperty PaneActivatedCommandProperty =
        DependencyProperty.Register(nameof(PaneActivatedCommand), typeof(ICommand), typeof(PaneView));

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

    /// <summary>"Split right": split the tab area, moving the passed <see cref="Page"/> into a new pane.</summary>
    public ICommand? SplitTabCommand
    {
        get => (ICommand?)GetValue(SplitTabCommandProperty);
        set => SetValue(SplitTabCommandProperty, value);
    }

    /// <summary>"Split": split the tab area with a new empty pane.</summary>
    public ICommand? SplitEmptyCommand
    {
        get => (ICommand?)GetValue(SplitEmptyCommandProperty);
        set => SetValue(SplitEmptyCommandProperty, value);
    }

    /// <summary>"Close pane": collapse the split, taking this pane's parameter.</summary>
    public ICommand? ClosePaneCommand
    {
        get => (ICommand?)GetValue(ClosePaneCommandProperty);
        set => SetValue(ClosePaneCommandProperty, value);
    }

    /// <summary>"Close except this": close the other tabs in this pane (parameter = the kept <see cref="Page"/>).</summary>
    public ICommand? CloseOthersCommand
    {
        get => (ICommand?)GetValue(CloseOthersCommandProperty);
        set => SetValue(CloseOthersCommandProperty, value);
    }

    /// <summary>Raised (with this view's <see cref="Pane"/>) when the pane is interacted with, so the shell
    /// can mark it the focused pane.</summary>
    public ICommand? PaneActivatedCommand
    {
        get => (ICommand?)GetValue(PaneActivatedCommandProperty);
        set => SetValue(PaneActivatedCommandProperty, value);
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

        // Mark this pane the focused one when the user clicks or tabs into it, so new tabs and AI
        // context route here. Harmless when unsplit (there is only one pane).
        PreviewMouseDown        += (_, _) => RaisePaneActivated();
        PreviewGotKeyboardFocus += (_, _) => RaisePaneActivated();
    }

    private void RaisePaneActivated()
    {
        if (Pane is not null && PaneActivatedCommand?.CanExecute(Pane) == true)
            PaneActivatedCommand.Execute(Pane);
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
        var page = Pane?.ActivePage;

        // Read before GetOrCreateContent, which is the thing that would build it: null here means this page
        // has never been shown, and only that first show earns the settle-in animation.
        var firstShow = page?.Content is null;

        var content = page?.GetOrCreateContent();

        // GetOrCreateContent no longer throws — it records the failure and hands back a blank control. A
        // blank tab is indistinguishable from a feature that drew nothing, so swap in the panel that says
        // what happened. Cached back onto the page so switching away and back doesn't rebuild it.
        if (page?.LoadException is { } error)
            content = page.Content as PageLoadErrorView
                   ?? page.ReplaceContent(new PageLoadErrorView(page, error));

        if (ReferenceEquals(ContentHost.Content, content)) return;

        MeasureSwitch(page, firstShow);

        // A page's content control is cached and shared as it moves between panes/windows; a WPF element
        // can have only one parent. Detach it from a prior host (e.g. the other side of a collapsing
        // split) before adopting, so re-parenting never throws.
        DetachFromParent(content);
        ContentHost.Content = content;
        if (content is null) return;

        if (firstShow) AnimateContentIn();
        else           ShowContentAtRest();
    }

    /// <summary>
    /// Times the whole switch — the swap plus the layout and render pass it provokes — when
    /// <c>NEXAFLOW_TIMING=1</c>. The scope closes at <see cref="DispatcherPriority.ContextIdle"/>, which the
    /// dispatcher reaches only once that frame's work is done, so the number is what the user waited for
    /// rather than what this method returned in. Free when timing is off.
    /// </summary>
    private void MeasureSwitch(Page? page, bool firstShow)
    {
        if (!Timing.Enabled) return;
        var scope = Timing.Measure($"Tab.show {page?.PageKind ?? "none"} first={firstShow} (swap to idle)");
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => scope?.Dispose());
    }

    /// <summary>
    /// Shows a page that already existed, with no animation at all. Returning to an open tab is not an
    /// arrival: the content was on screen moments ago, so easing it back in only adds ~190ms of waiting to
    /// every switch, and an opacity below 1 makes WPF composite the whole page through an intermediate
    /// surface for as long as it runs. Animations are removed rather than left holding their last value.
    /// </summary>
    private void ShowContentAtRest()
    {
        ContentHost.BeginAnimation(OpacityProperty, null);
        ContentHost.Opacity = 1.0;
        if (ContentHost.RenderTransform is TranslateTransform slide)
            slide.BeginAnimation(TranslateTransform.YProperty, null);
        ContentHost.RenderTransform = Transform.Identity;
    }

    // Subtle settle-in the FIRST time a page is shown, so content that has just been built eases in rather
    // than popping — the debounced startup load, and a newly opened tab. A switch back to a page that
    // already exists takes ShowContentAtRest instead: there is nothing arriving to announce.
    private void AnimateContentIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ContentHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });

        var slide = new TranslateTransform();
        ContentHost.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(6.0, 0.0, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });
    }

    private static void DetachFromParent(UIElement? element)
    {
        if (element is null) return;
        switch (LogicalTreeHelper.GetParent(element))
        {
            case ContentPresenter cp when ReferenceEquals(cp.Content, element): cp.Content = null; break;
            case ContentControl   cc when ReferenceEquals(cc.Content, element): cc.Content = null; break;
        }
    }
}
