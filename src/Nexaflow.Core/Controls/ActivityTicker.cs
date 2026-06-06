using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Nexaflow.Core.Models;
using TaskStatus = Nexaflow.Core.Models.TaskStatus;

namespace Nexaflow.Core.Controls;

/// <summary>
/// Displays the most-recent background task description, fading in on each change.
/// Implemented as a ContentControl hosting a TextBlock.
/// </summary>
public class ActivityTicker : ContentControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource),
            typeof(ObservableCollection<BackgroundTask>), typeof(ActivityTicker),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public ObservableCollection<BackgroundTask>? ItemsSource
    {
        get => (ObservableCollection<BackgroundTask>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private readonly TextBlock _label;
    private readonly Canvas    _surface;

    public ActivityTicker()
    {
        _label = new TextBlock { FontSize = 11 };
        // Theme-driven (DynamicResource) so the ticker stays legible on every theme and retints on switch.
        _label.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        Canvas.SetLeft(_label, 0);
        Canvas.SetTop(_label, 2);   // vertically centre the ~14px line in the 18px strip

        // Host the label in a Canvas: a Canvas child is always measured/arranged at its full desired
        // width (a ContentPresenter would constrain it to the control width and clip the text), so the
        // whole string renders and ClipToBounds + the scroll animation reveal the part past the edge.
        _surface = new Canvas();
        _surface.Children.Add(_label);

        Content      = _surface;
        Height       = 18;
        ClipToBounds = true;
        SizeChanged += (_, _) => UpdateMarquee();
    }

    /// <summary>
    /// When the text is wider than the available space, slowly scroll it left and restart from the
    /// beginning so the full description is readable; otherwise sit still.
    /// </summary>
    private void UpdateMarquee()
    {
        if (_label.RenderTransform is TranslateTransform prev)
            prev.BeginAnimation(TranslateTransform.XProperty, null);   // stop any prior scroll
        _label.RenderTransform = null;

        if (string.IsNullOrEmpty(_label.Text) || ActualWidth <= 0) return;

        _label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double overflow = _label.DesiredSize.Width - ActualWidth;
        if (overflow <= 0) return;       // fits — no scroll needed

        var tt = new TranslateTransform();
        _label.RenderTransform = tt;

        double scrollSeconds = Math.Max(overflow / 18.0, 2);   // ~18 px/sec
        var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0,         KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0,         KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5))));                  // dwell at start
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5 + scrollSeconds))));  // scroll to the end
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.0 + scrollSeconds))));  // dwell at end, then restart
        tt.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var t = (ActivityTicker)d;
        if (e.OldValue is ObservableCollection<BackgroundTask> old)
            old.CollectionChanged -= t.Items_Changed;
        if (e.NewValue is ObservableCollection<BackgroundTask> @new)
            @new.CollectionChanged += t.Items_Changed;
        t.Refresh();
    }

    private void Items_Changed(object? s, NotifyCollectionChangedEventArgs e)
        => Dispatcher.Invoke(Refresh);

    private void Refresh()
    {
        if (ItemsSource is null || ItemsSource.Count == 0)
        {
            _label.Text = string.Empty;
            return;
        }

        var latest = ItemsSource[0];
        string prefix = latest.Status switch
        {
            TaskStatus.Running   => "⟳ ",
            TaskStatus.Completed => "✓ ",
            TaskStatus.Failed    => "✕ ",
            _                    => ""
        };

        _label.Text = prefix + latest.Description;
        _label.SetResourceReference(TextBlock.ForegroundProperty,
            latest.Status == TaskStatus.Failed ? "WarningBrush" : "TextMutedBrush");

        // Fade-in
        var anim = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(400)));
        _label.BeginAnimation(OpacityProperty, anim);

        UpdateMarquee();
    }
}
