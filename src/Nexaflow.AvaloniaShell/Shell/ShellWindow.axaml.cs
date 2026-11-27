using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Nexaflow.AvaloniaShell.Views;
using Nexaflow.Localization;

namespace Nexaflow.AvaloniaShell.Shell;

/// <summary>
/// A shell window, drawing its own caption as the WPF shell does: registered with the runtime it shows from the moment it is made,
/// focused with it while active, and gone from it as it closes — what Nexaflow.Core's <c>MainWindow</c> does with the session. Which
/// runtime that is changes when the window moves, so each handler reaches the one it shows now.
/// <para>
/// What is the view's own lives here, as it does in the WPF window: the caption buttons and the drag bar, the AI line opening out of
/// the status dot, the input's keys and its ghost completion, the tab strip easing in, and the toast fading.
/// </para>
/// </summary>
public partial class ShellWindow : Window
{
    /// <summary>The AI line opens out of the status dot, and closes back into it, over this long.</summary>
    private static readonly TimeSpan AiLineMotion = TimeSpan.FromMilliseconds(180);

    /// <summary>The ghost completion is drawn in this, after what is typed — the WPF bar's colour.</summary>
    private static readonly IBrush GhostBrush = new SolidColorBrush(Color.Parse("#6A7290"));

    private bool _left;
    private CancellationTokenSource? _aiLineMotion;

    /// <summary>Only for Avalonia's designer and loader; a shell window is always made over a runtime.</summary>
    public ShellWindow() => InitializeComponent();

    /// <param name="openWorkspaceTabs">Open the workspace's tabs now: a launch's window. A window the session makes (a tear-off, a new
    /// window) is given its tabs by the session.</param>
    internal ShellWindow(WindowServices session, AvaloniaPageViews views, IUiThread ui, WorkspaceRuntime runtime, bool openWorkspaceTabs)
    {
        InitializeComponent();
        Model = new ShellWindowModel(this, session, views, ui, runtime);
        DataContext = Model;
        Model.PropertyChanged += OnModelChanged;
        Model.OpenTabs.CollectionChanged += OnTabsChanged;

        MinimiseButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximiseButton.Click += (_, _) => ToggleMaximised();
        CloseButton.Click    += (_, _) => Close();
        DragBar.PointerPressed += OnDragBarPressed;
        AiInput.AddHandler(KeyDownEvent, OnInputKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WindowStateProperty) return;
        var maximised = WindowState == WindowState.Maximized;
        MaximiseButton.Content = maximised ? "" : "";
        ToolTip.SetTip(MaximiseButton, Str.Get(maximised ? "Shell.Caption.Restore" : "Shell.Caption.Maximise"));
    }

    private void ToggleMaximised() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>The whole top strip drags the window, and a double click maximises or restores it — the WPF caption's behaviour.</summary>
    private void OnDragBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) ToggleMaximised();
        else BeginMoveDrag(e);
    }

    /// <summary>Enter sends, Shift+Enter starts a new line, Tab takes the completion offered.</summary>
    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            Model.SendCommand.Execute(null);
        }
        else if (e.Key == Key.Tab && Model.Completion is { Length: > 0 } completion)
        {
            e.Handled = true;
            Model.AiInputText += completion;
            AiInput.CaretIndex = Model.AiInputText.Length;
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellWindowModel.ShowsAiLine):
                LayOutAiLine(Model.ShowsAiLine);
                break;
            case nameof(ShellWindowModel.Completion):
            case nameof(ShellWindowModel.AiInputText):
                ShowCompletion();
                break;
            case nameof(ShellWindowModel.IsToastShown):
                ShowToast(Model.IsToastShown);
                break;
        }
    }

    /// <summary>
    /// Opens the AI line out of the status dot — its field widening from the dot to the bar's width while the user's field moves down
    /// to make room — or closes it back, over <see cref="AiLineMotion"/>, eased out.
    /// </summary>
    private void LayOutAiLine(bool open)
    {
        _aiLineMotion?.Cancel();
        _aiLineMotion = new CancellationTokenSource();
        var cancel = _aiLineMotion.Token;

        // Each goes to where it ends at once, and the animation carries it there from where it was; done opening, the field
        // stretches with the bar from then on.
        var wide  = AiBarGrid.ColumnDefinitions[0].ActualWidth + AiBarGrid.ColumnDefinitions[1].ActualWidth;
        var width = (From: AiField.Bounds.Width, To: open ? wide : 30);
        var place = (From: UserField.Margin, To: new Thickness(6, open ? AiField.Bounds.Height + 4 : 0, 0, 0));

        AiField.HorizontalAlignment = HorizontalAlignment.Left;
        AiField.Width = width.To;
        UserField.Margin = place.To;

        _ = Run(AiField, WidthProperty, width.From, width.To, cancel).ContinueWith(_ =>
        {
            if (cancel.IsCancellationRequested || !open) return;
            AiField.Width = double.NaN;
            AiField.HorizontalAlignment = HorizontalAlignment.Stretch;
        }, TaskScheduler.FromCurrentSynchronizationContext());
        _ = Run(UserField, MarginProperty, place.From, place.To, cancel);
    }

    private static Task Run<T>(Animatable target, AvaloniaProperty property, T from, T to, CancellationToken cancel) => new Animation
    {
        Duration = AiLineMotion,
        Easing = new CubicEaseOut(),
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } },
        },
    }.RunAsync(target, cancel).ContinueWith(_ => { }, TaskScheduler.Default);

    /// <summary>The completion the owning handler offers, drawn after what is typed, in the ghost colour.</summary>
    private void ShowCompletion()
    {
        Completion.Inlines!.Clear();
        if (Model.Completion is not { Length: > 0 } completion) return;
        Completion.Inlines.Add(new Run(Model.AiInputText) { Foreground = Brushes.Transparent });
        Completion.Inlines.Add(new Run(completion) { Foreground = GhostBrush });
    }

    /// <summary>A toast shows at once, and fades over two seconds when it is done.</summary>
    private void ShowToast(bool shown)
    {
        Toast.Transitions = shown ? null : [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromSeconds(2) }];
        Toast.Opacity = shown ? 1 : 0;
    }

    /// <summary>The tab strip eases in the first time it has tabs to show.</summary>
    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || Model.OpenTabs.Count != e.NewItems?.Count) return;
        _ = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(200),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d) } },
            },
        }.RunAsync(TabStripItems);
    }

    private void Leave()
    {
        if (_left) return;
        _left = true;
        Model.PropertyChanged -= OnModelChanged;
        Model.OpenTabs.CollectionChanged -= OnTabsChanged;
        Model.CurrentRuntime.ShellServices.UnregisterWindow(Model);
        Model.Detach();
    }
}
