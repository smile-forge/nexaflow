using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// A slider over a span rather than a value: two thumbs, its lower and upper ends, and the band between them, which drags both
/// at once — "from here to here" said in one control, where two sliders say it in two. A press on the rail beside the band
/// brings the nearer end there. The rail runs half an end's width in from either side, so a host laying something under it
/// insets that as much to line up with it.
/// <para>
/// Each end is a slider of its own to UI Automation, its id the control's with <c>Lower</c> or <c>Upper</c> after it, so it is
/// read and set as any slider is. A drag of either end or of the band raises the ordinary <c>Thumb.DragStarted</c> /
/// <c>DragCompleted</c>, so a host that makes a slider's drag one undo step makes this one's one too.
/// </para>
/// <para>
/// The ends are kept in order where they are moved here — dragged, keyed, pressed or set through UI Automation. Set from
/// outside, as a binding does, they are held only to the bounds: a host moving a whole span sets one end before the other, and
/// an end held to the other's old place would be written back to the host as a span it never asked for.
/// </para>
/// </summary>
[TemplatePart(Name = LowerPart, Type = typeof(RangeThumb))]
[TemplatePart(Name = UpperPart, Type = typeof(RangeThumb))]
[TemplatePart(Name = SpanPart, Type = typeof(Thumb))]
[TemplatePart(Name = RailPart, Type = typeof(FrameworkElement))]
public class RangeSlider : Control
{
    public const string LowerPart = "PART_Lower";
    public const string UpperPart = "PART_Upper";
    public const string SpanPart = "PART_Span";
    public const string RailPart = "PART_Rail";

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(RangeSlider), new FrameworkPropertyMetadata(0.0, OnBoundsChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(RangeSlider), new FrameworkPropertyMetadata(100.0, OnBoundsChanged));

    public static readonly DependencyProperty LowerValueProperty = DependencyProperty.Register(
        nameof(LowerValue), typeof(double), typeof(RangeSlider),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnEndChanged, CoerceLower));

    public static readonly DependencyProperty UpperValueProperty = DependencyProperty.Register(
        nameof(UpperValue), typeof(double), typeof(RangeSlider),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnEndChanged, CoerceUpper));

    public static readonly DependencyProperty SmallChangeProperty = DependencyProperty.Register(
        nameof(SmallChange), typeof(double), typeof(RangeSlider), new FrameworkPropertyMetadata(1.0));

    public static readonly DependencyProperty LargeChangeProperty = DependencyProperty.Register(
        nameof(LargeChange), typeof(double), typeof(RangeSlider), new FrameworkPropertyMetadata(10.0));

    public static readonly DependencyProperty RailHeightProperty = DependencyProperty.Register(
        nameof(RailHeight), typeof(double), typeof(RangeSlider), new FrameworkPropertyMetadata(4.0));

    private RangeThumb? _lower;
    private RangeThumb? _upper;
    private Thumb? _span;
    private FrameworkElement? _rail;

    /// <summary>What a drag began from: where the pointer was on the rail, and the ends then.</summary>
    private (double Pointer, double Lower, double Upper) _dragFrom;

    static RangeSlider()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RangeSlider), new FrameworkPropertyMetadata(typeof(RangeSlider)));
        FocusableProperty.OverrideMetadata(typeof(RangeSlider), new FrameworkPropertyMetadata(false));
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Where the span begins, <see cref="Minimum"/>…<see cref="UpperValue"/> where it is moved here.</summary>
    public double LowerValue
    {
        get => (double)GetValue(LowerValueProperty);
        set => SetValue(LowerValueProperty, value);
    }

    /// <summary>Where the span ends, <see cref="LowerValue"/>…<see cref="Maximum"/> where it is moved here.</summary>
    public double UpperValue
    {
        get => (double)GetValue(UpperValueProperty);
        set => SetValue(UpperValueProperty, value);
    }

    /// <summary>How far an arrow key moves an end.</summary>
    public double SmallChange
    {
        get => (double)GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }

    /// <summary>How far Page Up or Page Down moves an end.</summary>
    public double LargeChange
    {
        get => (double)GetValue(LargeChangeProperty);
        set => SetValue(LargeChangeProperty, value);
    }

    /// <summary>How tall the rail is drawn: a thin line, or as tall as the control where something lies under it.</summary>
    public double RailHeight
    {
        get => (double)GetValue(RailHeightProperty);
        set => SetValue(RailHeightProperty, value);
    }

    /// <summary>The value of <paramref name="end"/>.</summary>
    public double ValueOf(RangeEnd end) => end == RangeEnd.Lower ? LowerValue : UpperValue;

    /// <summary><paramref name="end"/> moved to <paramref name="value"/>, held between the bounds and the other end.</summary>
    public void SetEnd(RangeEnd end, double value)
    {
        if (end == RangeEnd.Lower) SetCurrentValue(LowerValueProperty, Math.Clamp(value, Minimum, Math.Max(Minimum, UpperValue)));
        else SetCurrentValue(UpperValueProperty, Math.Clamp(value, Math.Min(Maximum, LowerValue), Maximum));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new RangeSliderAutomationPeer(this);

    public override void OnApplyTemplate()
    {
        foreach (var end in new[] { _lower, _upper })
            if (end is not null) end.DragStarted -= EndDragStarted;
        if (_lower is not null) _lower.DragDelta -= LowerDragged;
        if (_upper is not null) _upper.DragDelta -= UpperDragged;
        if (_span is not null)
        {
            _span.DragStarted -= EndDragStarted;
            _span.DragDelta -= SpanDragged;
        }
        if (_rail is not null) _rail.SizeChanged -= RailResized;

        base.OnApplyTemplate();
        _lower = GetTemplateChild(LowerPart) as RangeThumb;
        _upper = GetTemplateChild(UpperPart) as RangeThumb;
        _span = GetTemplateChild(SpanPart) as Thumb;
        _rail = GetTemplateChild(RailPart) as FrameworkElement;

        if (_lower is not null)
        {
            _lower.Attach(this, RangeEnd.Lower);
            _lower.DragStarted += EndDragStarted;
            _lower.DragDelta += LowerDragged;
        }
        if (_upper is not null)
        {
            _upper.Attach(this, RangeEnd.Upper);
            _upper.DragStarted += EndDragStarted;
            _upper.DragDelta += UpperDragged;
        }
        if (_span is not null)
        {
            _span.DragStarted += EndDragStarted;
            _span.DragDelta += SpanDragged;
        }
        if (_rail is not null) _rail.SizeChanged += RailResized;
        Place();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled || _rail is null || !IsEnabled) return;
        double at = ValueAt(e.GetPosition(_rail).X);
        var nearer = Math.Abs(at - LowerValue) <= Math.Abs(at - UpperValue) ? RangeEnd.Lower : RangeEnd.Upper;
        SetEnd(nearer, at);
        (nearer == RangeEnd.Lower ? _lower : _upper)?.Focus();
        e.Handled = true;
    }

    private void EndDragStarted(object sender, DragStartedEventArgs e)
        => _dragFrom = (_rail is null ? 0 : Mouse.GetPosition(_rail).X, LowerValue, UpperValue);

    private double Dragged() => _rail is null ? 0 : (Mouse.GetPosition(_rail).X - _dragFrom.Pointer) * PerPixel();

    private void LowerDragged(object sender, DragDeltaEventArgs e) => SetEnd(RangeEnd.Lower, _dragFrom.Lower + Dragged());

    private void UpperDragged(object sender, DragDeltaEventArgs e) => SetEnd(RangeEnd.Upper, _dragFrom.Upper + Dragged());

    /// <summary>The band dragged: both ends by the same, held so the span keeps its width against either bound.</summary>
    private void SpanDragged(object sender, DragDeltaEventArgs e)
    {
        double by = Math.Clamp(Dragged(), Minimum - _dragFrom.Lower, Maximum - _dragFrom.Upper);
        // The end it moves towards first, so the other never pushes against it.
        if (by > 0)
        {
            SetEnd(RangeEnd.Upper, _dragFrom.Upper + by);
            SetEnd(RangeEnd.Lower, _dragFrom.Lower + by);
        }
        else
        {
            SetEnd(RangeEnd.Lower, _dragFrom.Lower + by);
            SetEnd(RangeEnd.Upper, _dragFrom.Upper + by);
        }
    }

    private void RailResized(object sender, SizeChangedEventArgs e) => Place();

    /// <summary>Half an end's width each side of the rail, so an end at either bound still lies wholly on it.</summary>
    private double Inset => (_lower?.Width is > 0 and var width ? width : 0) / 2;

    private double Usable => _rail is { ActualWidth: > 0 } rail ? Math.Max(1, rail.ActualWidth - 2 * Inset) : 0;

    private double PerPixel() => Usable > 0 ? (Maximum - Minimum) / Usable : 0;

    private double ValueAt(double x) => Minimum + Math.Clamp(x - Inset, 0, Usable) * PerPixel();

    private double PixelOf(double value) => Maximum > Minimum ? Inset + (value - Minimum) / (Maximum - Minimum) * Usable : Inset;

    /// <summary>
    /// The thumbs and the band laid where the ends are. A band too narrow for both ends to sit on it side by side — they would lie
    /// over each other, and over the band, so only one could be taken hold of — has its ends step aside, each to its own side of
    /// the band, and the band between them kept half an end wide to be dragged by; the step grows as the band narrows, so
    /// nothing jumps. The pair stays on the rail at either bound.
    /// </summary>
    private void Place()
    {
        if (_rail is null) return;
        // Set from outside, the ends may cross for a moment, one set before the other: the band is drawn between them either way.
        double from = PixelOf(Math.Min(LowerValue, UpperValue)), to = PixelOf(Math.Max(LowerValue, UpperValue));
        double end = _lower?.Width is > 0 and var width ? width : 0, grip = end / 2;
        double lower = from, upper = to;
        if (to - from < end + grip)
        {
            double centre = (from + to) / 2, apart = (end + grip) / 2;
            (lower, upper) = (centre - apart, centre + apart);
            // Kept on the rail: an end may not step past either bound's place.
            double low = Inset, high = Inset + Usable;
            if (lower < low) (lower, upper) = (low, low + 2 * apart);
            if (upper > high) (lower, upper) = (high - 2 * apart, high);
        }
        if (_span is not null)
        {
            double left = to - from < end + grip ? lower + end / 2 : from, right = to - from < end + grip ? upper - end / 2 : to;
            Canvas.SetLeft(_span, left);
            _span.Width = Math.Max(0, right - left);
        }
        if (_lower is not null) Canvas.SetLeft(_lower, lower - _lower.Width / 2);
        if (_upper is not null) Canvas.SetLeft(_upper, upper - _upper.Width / 2);
    }

    private static void OnBoundsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slider = (RangeSlider)d;
        slider.CoerceValue(LowerValueProperty);
        slider.CoerceValue(UpperValueProperty);
        slider.Place();
    }

    private static void OnEndChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slider = (RangeSlider)d;
        slider.Place();
        var thumb = e.Property == LowerValueProperty ? slider._lower : slider._upper;
        if (thumb is not null && UIElementAutomationPeer.FromElement(thumb) is { } peer)
            peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, e.OldValue, e.NewValue);
    }

    private static object CoerceLower(DependencyObject d, object value) => ((RangeSlider)d).WithinBounds((double)value);

    private static object CoerceUpper(DependencyObject d, object value) => ((RangeSlider)d).WithinBounds((double)value);

    private double WithinBounds(double value) => Math.Clamp(value, Minimum, Math.Max(Minimum, Maximum));
}

/// <summary>One end of a <see cref="RangeSlider"/>.</summary>
public enum RangeEnd { Lower, Upper }

/// <summary>
/// A <see cref="RangeSlider"/>'s end: a thumb that arrow keys, Page Up and Down, Home and End move, and that UI Automation reads
/// and sets as a slider of its own.
/// </summary>
public class RangeThumb : Thumb
{
    static RangeThumb()
    {
        FocusableProperty.OverrideMetadata(typeof(RangeThumb), new FrameworkPropertyMetadata(true));
    }

    internal RangeSlider? Slider { get; private set; }

    internal RangeEnd End { get; private set; }

    internal void Attach(RangeSlider slider, RangeEnd end) => (Slider, End) = (slider, end);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Slider is not { } slider) return;
        double now = slider.ValueOf(End);
        double? to = e.Key switch
        {
            Key.Left or Key.Down => now - slider.SmallChange,
            Key.Right or Key.Up => now + slider.SmallChange,
            Key.PageDown => now - slider.LargeChange,
            Key.PageUp => now + slider.LargeChange,
            Key.Home => slider.Minimum,
            Key.End => slider.Maximum,
            _ => null,
        };
        if (to is not { } value) return;
        slider.SetEnd(End, value);
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new RangeThumbAutomationPeer(this);
}

/// <summary>A <see cref="RangeSlider"/> to UI Automation: a group holding its two ends, each a slider.</summary>
internal sealed class RangeSliderAutomationPeer(RangeSlider owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(RangeSlider);
}

/// <summary>A <see cref="RangeThumb"/> to UI Automation: a slider whose value is its end's.</summary>
internal sealed class RangeThumbAutomationPeer(RangeThumb owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
{
    private RangeThumb Thumb => (RangeThumb)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

    protected override string GetClassNameCore() => nameof(RangeThumb);

    /// <summary>Its slider's id with its end after it — composed when asked, so an id given the slider at any time carries.</summary>
    protected override string GetAutomationIdCore()
        => Thumb.Slider is { } slider && AutomationProperties.GetAutomationId(slider) is { Length: > 0 } id ? id + Thumb.End : base.GetAutomationIdCore();

    protected override string GetNameCore()
        => Thumb.Slider is { } slider && AutomationProperties.GetName(slider) is { Length: > 0 } name ? $"{name} ({Thumb.End})" : base.GetNameCore();

    public override object? GetPattern(PatternInterface patternInterface)
        => patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);

    public double Value => Thumb.Slider?.ValueOf(Thumb.End) ?? 0;

    public double Minimum => Thumb.Slider?.Minimum ?? 0;

    public double Maximum => Thumb.Slider?.Maximum ?? 0;

    public double SmallChange => Thumb.Slider?.SmallChange ?? 0;

    public double LargeChange => Thumb.Slider?.LargeChange ?? 0;

    public bool IsReadOnly => !IsEnabled();

    public void SetValue(double value)
    {
        if (!IsEnabled()) throw new ElementNotEnabledException();
        Thumb.Slider?.SetEnd(Thumb.End, value);
    }
}
