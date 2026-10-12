using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Visuals.Common.Behaviors;

/// <summary>
/// Keeps a <see cref="ScrollViewer"/> at its end while what it shows grows — a reply being written, a log
/// being appended to — as long as the reader was at the end when it grew. Scrolled back to read something
/// earlier, they are left where they are. Switch it on with <c>beh:ScrollFollowsEnd.IsEnabled="True"</c>.
/// </summary>
public static class ScrollFollowsEnd
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(ScrollFollowsEnd),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value)
        => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element)
        => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer) return;

        viewer.ScrollChanged -= OnScrollChanged;
        if (e.NewValue is true) viewer.ScrollChanged += OnScrollChanged;
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange <= 0) return;

        // Where the end was before this growth: the reader was following it if the viewport reached it.
        var endBefore = e.ExtentHeight - e.ExtentHeightChange;
        if (e.VerticalOffset + e.ViewportHeight >= endBefore - 1)
            ((ScrollViewer)sender).ScrollToEnd();
    }
}
