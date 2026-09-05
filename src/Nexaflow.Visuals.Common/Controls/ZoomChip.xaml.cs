using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// The zoom control every text surface wears: the current percentage, and a click-through popup of
/// presets. It drives a <see cref="TextZoom"/>; the keyboard and Ctrl+wheel routes belong to the host,
/// because they are gestures over the <em>content</em> rather than over this chip.
/// <para>
/// Set <see cref="PagePrefix"/> to the page's automation prefix ("Text", "Markdown", "Hex") and the
/// chip's ids compose from it — <c>{PagePrefix}_ZoomLabel</c> and <c>{PagePrefix}_Zoom{percent}</c> — so
/// each page keeps ids of its own without a per-page copy of the markup. The presets are built here
/// rather than declared in the markup for exactly that reason: an id per row has to be composed, and a
/// bound one is invisible to a journey.
/// </para>
/// </summary>
public partial class ZoomChip : UserControl
{
    public ZoomChip() => InitializeComponent();

    /// <summary>The page's zoom state. Required — with none the chip renders nothing to click.</summary>
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(TextZoom), typeof(ZoomChip),
        new PropertyMetadata(null, (d, _) => ((ZoomChip)d).BuildPresets()));

    public TextZoom? Zoom
    {
        get => (TextZoom?)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>Automation prefix for the host page — "Text", "Markdown", "Hex".</summary>
    public static readonly DependencyProperty PagePrefixProperty = DependencyProperty.Register(
        nameof(PagePrefix), typeof(string), typeof(ZoomChip),
        new PropertyMetadata(string.Empty, OnPagePrefixChanged));

    public string PagePrefix
    {
        get => (string)GetValue(PagePrefixProperty);
        set => SetValue(PagePrefixProperty, value);
    }

    // Exposed as a read-only DP so the markup can bind it, matching SearchStatusChip.
    private static readonly DependencyPropertyKey AutomationIdLabelKey =
        DependencyProperty.RegisterReadOnly(nameof(AutomationIdLabel), typeof(string), typeof(ZoomChip),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty AutomationIdLabelProperty = AutomationIdLabelKey.DependencyProperty;

    public string AutomationIdLabel => (string)GetValue(AutomationIdLabelProperty);

    private static void OnPagePrefixChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chip   = (ZoomChip)d;
        var prefix = (string?)e.NewValue ?? string.Empty;
        chip.SetValue(AutomationIdLabelKey, $"{prefix}_ZoomLabel");
        chip.BuildPresets();
    }

    /// <summary>
    /// Rebuilds the preset rows. Runs on either input changing because an id is composed from the prefix
    /// and the row from the zoom's own preset list — neither is knowable until both are set, and the
    /// order a host sets two properties in is not ours to assume.
    /// </summary>
    private void BuildPresets()
    {
        PresetPanel.Children.Clear();
        if (Zoom is not { } zoom) return;

        foreach (var percent in zoom.Presets)
        {
            var button = new Button
            {
                Content = $"{percent}%",
                Style   = (Style)FindResource("ZoomPresetButton"),
                Tag     = percent,
            };
            AutomationProperties.SetAutomationId(button, $"{PagePrefix}_Zoom{percent}");
            button.Click += OnPresetClick;
            PresetPanel.Children.Add(button);
        }
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int percent } && Zoom is { } zoom)
            zoom.Percent = percent;
        ZoomPopup.IsOpen = false;
    }

    private void ZoomLabel_Click(object sender, MouseButtonEventArgs e)
    {
        if (Zoom is null) return;
        ZoomPopup.IsOpen = true;
        e.Handled = true;
    }
}
