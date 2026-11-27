using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Nexaflow.Localization;
using SessionTaskStatus = Nexaflow.Session.Models.TaskStatus;

namespace Nexaflow.AvaloniaShell.Shell;

/// <summary>
/// The line under the logo saying what the session is doing — the first of its background tasks, prefixed by how it stands
/// (<c>⟳ ✓ ✕ ◼</c>), "Idle" when it does nothing — as the WPF shell's ticker shows it: each new line fades in over 400 ms, and a
/// line too long for the column slides across and back, holding at each end.
/// </summary>
public sealed class ActivityTicker : Control
{
    public static readonly StyledProperty<IReadOnlyList<BackgroundTask>?> TasksProperty =
        AvaloniaProperty.Register<ActivityTicker, IReadOnlyList<BackgroundTask>?>(nameof(Tasks));

    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(1.5);
    private const double SlideSpeed = 18;   // px a second

    private static readonly Animation FadeIn = new()
    {
        Duration = TimeSpan.FromMilliseconds(400),
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d) } },
        },
    };

    private readonly TextBlock _line = new() { FontSize = 11 };
    private readonly TranslateTransform _slide = new();
    private BackgroundTask? _shown;
    private IDisposable? _colour;
    private CancellationTokenSource? _motion;
    private double _slidWidth = double.NaN;
    private string? _slidText;

    public ActivityTicker()
    {
        Height = 18;
        ClipToBounds = true;
        _line.RenderTransform = _slide;
        LogicalChildren.Add(_line);
        VisualChildren.Add(_line);
    }

    /// <summary>The session's background tasks; the first is the one told.</summary>
    public IReadOnlyList<BackgroundTask>? Tasks
    {
        get => GetValue(TasksProperty);
        set => SetValue(TasksProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TasksProperty) return;
        if (change.OldValue is INotifyCollectionChanged old) old.CollectionChanged -= OnTasksChanged;
        if (change.NewValue is INotifyCollectionChanged now) now.CollectionChanged += OnTasksChanged;
        Show();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _motion?.Cancel();
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _line.Measure(Size.Infinity);
        return new Size(0, 18);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _line.Arrange(new Rect(0, 2, _line.DesiredSize.Width, _line.DesiredSize.Height));
        Slide(finalSize.Width);
        return finalSize;
    }

    private void OnTasksChanged(object? sender, NotifyCollectionChangedEventArgs e) => Show();

    private void OnTaskChanged(object? sender, PropertyChangedEventArgs e) => Show();

    private void Show()
    {
        var first = Tasks is { Count: > 0 } tasks ? tasks[0] : null;
        if (!ReferenceEquals(first, _shown))
        {
            if (_shown is not null) _shown.PropertyChanged -= OnTaskChanged;
            if (first is not null) first.PropertyChanged += OnTaskChanged;
            _shown = first;
        }

        var idle = Str.Get("Shell.BackgroundTasks.Idle");
        var text = first is null ? idle : Prefix(first.Status) + (first.Description.Length > 0 ? first.Description : idle);
        if (text == _line.Text) return;

        _line.Text = text;
        _colour?.Dispose();
        _colour = _line.Bind(TextBlock.ForegroundProperty,
                             this.GetResourceObservable(first?.Status == SessionTaskStatus.Failed ? "WarningBrush" : "TextMutedBrush"));
        _ = FadeIn.RunAsync(_line);
        InvalidateMeasure();
    }

    private static string Prefix(SessionTaskStatus status) => status switch
    {
        SessionTaskStatus.Running   => "⟳ ",
        SessionTaskStatus.Completed => "✓ ",
        SessionTaskStatus.Failed    => "✕ ",
        _                           => "◼ ",
    };

    /// <summary>A line wider than the column slides to its end and back, holding at each, for as long as it is shown.</summary>
    private void Slide(double width)
    {
        // Layout passes that arrive at the same line and width keep the slide where it is.
        if (width == _slidWidth && ReferenceEquals(_line.Text, _slidText)) return;
        _slidWidth = width;
        _slidText  = _line.Text;

        _motion?.Cancel();
        _slide.X = 0;
        var overflow = _line.DesiredSize.Width - width;
        if (overflow <= 0 || width <= 0) return;

        var slide = TimeSpan.FromSeconds(Math.Max(overflow / SlideSpeed, 2));
        var whole = Hold + slide + Hold;
        var motion = new Animation
        {
            Duration = whole,
            IterationCount = IterationCount.Infinite,
            Children =
            {
                Frame(0, 0),
                Frame(Hold / whole, 0),
                Frame((Hold + slide) / whole, -overflow),
                Frame(1, -overflow),
            },
        };
        _motion = new CancellationTokenSource();
        _ = motion.RunAsync(_slide, _motion.Token);

        static KeyFrame Frame(double cue, double x) => new() { Cue = new Cue(cue), Setters = { new Setter(TranslateTransform.XProperty, x) } };
    }
}
