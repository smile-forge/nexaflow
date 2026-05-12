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

    public ActivityTicker()
    {
        _label = new TextBlock
        {
            FontSize     = 11,
            Foreground   = new SolidColorBrush(Color.FromRgb(0x4A, 0x52, 0x70)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Content = _label;
        Height  = 18;
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

        _label.Text      = prefix + latest.Description;
        _label.Foreground = latest.Status == TaskStatus.Failed
            ? new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16))
            : new SolidColorBrush(Color.FromRgb(0x4A, 0x52, 0x70));

        // Fade-in
        var anim = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(400)));
        _label.BeginAnimation(OpacityProperty, anim);
    }
}
