using Nexaflow.Features.Hex.ViewModels;
using Nexaflow.Visuals.Common.Theming;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.Hex.Controls;

/// <summary>
/// What the hex and evaluate panels share: the monospace face they draw with, the cell metrics derived
/// from it, and how many rows fit. Both are fixed-grid surfaces drawn cell by cell, so a change to the
/// text size is a change to the whole layout — the column positions, the row height and the row count
/// the ViewModel pages by all fall out of one measurement, and there is no sense in the two panels
/// disagreeing about it.
/// </summary>
public abstract class HexPanelBase : FrameworkElement
{
    /// <summary>
    /// Point size the grid is drawn at. Named <c>TextSize</c> rather than <c>FontSize</c> because
    /// <see cref="System.Windows.Documents.TextElement.FontSize"/> is an inherited attached property a
    /// <see cref="FrameworkElement"/> already carries: a same-named DP here would read as that one and
    /// silently pick up a parent's value.
    /// <para>
    /// Defaults to <c>NaN</c>, meaning "follow the shell's
    /// <see cref="TextTypography.BaseFontSize"/>" — a DP default is evaluated once per type, so baking
    /// the setting in at first use would freeze every later panel at whatever it was then.
    /// </para>
    /// </summary>
    public static readonly DependencyProperty TextSizeProperty = DependencyProperty.Register(
        nameof(TextSize), typeof(double), typeof(HexPanelBase),
        new PropertyMetadata(double.NaN, (d, _) => ((HexPanelBase)d).InvalidateMetrics()));

    public double TextSize
    {
        get => (double)GetValue(TextSizeProperty);
        set => SetValue(TextSizeProperty, value);
    }

    /// <summary>Size in effect — an explicit <see cref="TextSize"/>, else the shell's text size.</summary>
    protected double EffectiveTextSize
        => double.IsNaN(TextSize) ? TextTypography.BaseFontSize : TextSize;

    protected HexViewModel? Vm;

    private bool _metricsReady;

    /// <summary>Width of one character cell.</summary>
    protected double CharW { get; private set; }

    /// <summary>Height of one row.</summary>
    protected double RowH { get; private set; } = 18;

    /// <summary>Device pixels per DIP, for <see cref="FormattedText"/>.</summary>
    protected double Dpi { get; private set; }

    protected Typeface Face { get; } = new("Cascadia Code, Consolas, Courier New");

    /// <summary>Attaches the ViewModel and starts repainting on its invalidations.</summary>
    public void Attach(HexViewModel vm)
    {
        if (Vm != null) Vm.InvalidateView -= OnInvalidate;
        Vm = vm;
        Vm.InvalidateView += OnInvalidate;
        InvalidateVisual();
    }

    private void OnInvalidate() => InvalidateVisual();

    /// <summary>Measures the cell once per size change. Cheap to call on every render.</summary>
    protected void EnsureMetrics()
    {
        if (_metricsReady) return;
        Dpi   = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ft = MakeText("M", Brushes.White);
        CharW = ft.Width;
        RowH  = Math.Max(ft.Height + 4, 18);
        _metricsReady = true;
        OnMetricsChanged();
    }

    /// <summary>Hook for a panel that caches anything derived from the cell size (column positions).</summary>
    protected virtual void OnMetricsChanged() { }

    /// <summary>
    /// Drops the measurement so the next render re-takes it, and re-runs layout — a bigger cell means
    /// fewer rows fit, and the ViewModel pages by that count, so it has to be told before the repaint.
    /// </summary>
    private void InvalidateMetrics()
    {
        _metricsReady = false;
        InvalidateMeasure();
        PublishVisibleRowCount();
        InvalidateVisual();
    }

    /// <summary>Rows that fit at the current height. At least one, so an empty viewport still renders.</summary>
    public int VisibleRowCount
    {
        get
        {
            EnsureMetrics();
            return ActualHeight > 0 ? Math.Max(1, (int)(ActualHeight / RowH)) : 1;
        }
    }

    /// <summary>Tells the ViewModel how many rows now fit, if that has changed.</summary>
    protected void PublishVisibleRowCount()
    {
        if (Vm is null) return;
        var count = VisibleRowCount;
        if (Vm.VisibleRowCount != count) Vm.VisibleRowCount = count;
    }

    protected FormattedText MakeText(string s, Brush brush)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
               Face, EffectiveTextSize, brush, Dpi);

    /// <summary>Looks a theme brush up by key. <see cref="FrameworkElement.FindResource"/> throws (naming
    /// the key) if it is missing — no silent literal fallback that would hide a mis-themed reference.</summary>
    protected Brush Res(string key) => (Brush)FindResource(key);

    /// <summary>The accent at selection strength.</summary>
    protected static Brush SemiAccent(Brush accent)
    {
        var c = ((SolidColorBrush)accent).Color;
        var b = new SolidColorBrush(Color.FromArgb(60, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        PublishVisibleRowCount();
    }
}
