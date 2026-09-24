using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Core.Models;
using Nexaflow.Visuals.Icons;

namespace Nexaflow.Core.Controls;

/// <summary>
/// The one visual for a ribbon button — on the ribbon, in the editor's preview and in its shape gallery — full or
/// <see cref="IsCompact"/>, in any <see cref="RibbonButtonShape"/>, with the item's own colours or the theme's.
/// <para>
/// The ribbon is on screen in every window and restyled rarely, so this is built for drawing: it reads its
/// <see cref="Item"/> directly (a weak listener re-reads it when a property changes) and hands the template plain
/// values through <c>TemplateBinding</c> — no per-button bindings or converters. Frame outlines are rebuilt only
/// when the size changes; badge outlines are shared (<see cref="RibbonShapeGeometry"/>). The look lives in the
/// <c>RibbonItemButton</c> style in <c>Styles.xaml</c>.
/// </para>
/// </summary>
public sealed class RibbonItemButton : Button
{
    private const int MaxLabelChars = 14;

    private (RibbonButtonShape Shape, Size Size, bool Compact, double Stroke) _frameFor;

    public RibbonItemButton() => SetResourceReference(StyleProperty, "RibbonItemButton");

    // ── Inputs ───────────────────────────────────────────────────────────────

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(RibbonItem), typeof(RibbonItemButton),
        new PropertyMetadata(null, OnItemChanged));

    public RibbonItem? Item
    {
        get => (RibbonItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(RibbonItemButton),
        new PropertyMetadata(false, (d, _) => ((RibbonItemButton)d).Apply()));

    /// <summary>Icon and label side by side at the smaller size, rather than stacked full height.</summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    // ── What the template draws ──────────────────────────────────────────────

    private static readonly DependencyPropertyKey IconKey          = ReadOnly<IconRef>(nameof(Icon), default);
    private static readonly DependencyPropertyKey CaptionKey       = ReadOnly<string>(nameof(Caption), string.Empty);
    private static readonly DependencyPropertyKey FrameGeometryKey = ReadOnly<Geometry?>(nameof(FrameGeometry), null);
    private static readonly DependencyPropertyKey BadgeGeometryKey = ReadOnly<Geometry?>(nameof(BadgeGeometry), null);
    private static readonly DependencyPropertyKey BadgeFillKey     = ReadOnly<Brush?>(nameof(BadgeFill), null);
    private static readonly DependencyPropertyKey BadgeSizeKey     = ReadOnly<double>(nameof(BadgeSize), 0d);
    private static readonly DependencyPropertyKey FrameStrokeKey   = ReadOnly<double>(nameof(FrameStroke), 0d);
    private static readonly DependencyPropertyKey BadgeStrokeKey   = ReadOnly<double>(nameof(BadgeStroke), 0d);
    private static readonly DependencyPropertyKey IsBadgeKey       = ReadOnly<bool>(nameof(IsBadge), false);
    private static readonly DependencyPropertyKey IsShapedKey      = ReadOnly<bool>(nameof(IsShaped), false);
    private static readonly DependencyPropertyKey HasCustomFrameKey = ReadOnly<bool>(nameof(HasCustomFrame), false);
    private static readonly DependencyPropertyKey BadgeForegroundKey = ReadOnly<Brush?>(nameof(BadgeForeground), null);
    private static readonly DependencyPropertyKey HasBadgeForegroundKey = ReadOnly<bool>(nameof(HasBadgeForeground), false);

    public static readonly DependencyProperty IconProperty           = IconKey.DependencyProperty;
    public static readonly DependencyProperty CaptionProperty        = CaptionKey.DependencyProperty;
    public static readonly DependencyProperty FrameGeometryProperty  = FrameGeometryKey.DependencyProperty;
    public static readonly DependencyProperty BadgeGeometryProperty  = BadgeGeometryKey.DependencyProperty;
    public static readonly DependencyProperty BadgeFillProperty      = BadgeFillKey.DependencyProperty;
    public static readonly DependencyProperty BadgeSizeProperty      = BadgeSizeKey.DependencyProperty;
    public static readonly DependencyProperty FrameStrokeProperty    = FrameStrokeKey.DependencyProperty;
    public static readonly DependencyProperty BadgeStrokeProperty    = BadgeStrokeKey.DependencyProperty;
    public static readonly DependencyProperty IsBadgeProperty        = IsBadgeKey.DependencyProperty;
    public static readonly DependencyProperty IsShapedProperty       = IsShapedKey.DependencyProperty;
    public static readonly DependencyProperty HasCustomFrameProperty = HasCustomFrameKey.DependencyProperty;
    public static readonly DependencyProperty BadgeForegroundProperty    = BadgeForegroundKey.DependencyProperty;
    public static readonly DependencyProperty HasBadgeForegroundProperty = HasBadgeForegroundKey.DependencyProperty;

    public IconRef Icon           => (IconRef)GetValue(IconProperty);
    public string Caption         => (string)GetValue(CaptionProperty);
    public Geometry? FrameGeometry => (Geometry?)GetValue(FrameGeometryProperty);
    public Geometry? BadgeGeometry => (Geometry?)GetValue(BadgeGeometryProperty);
    public Brush? BadgeFill       => (Brush?)GetValue(BadgeFillProperty);
    public double BadgeSize       => (double)GetValue(BadgeSizeProperty);
    public double FrameStroke     => (double)GetValue(FrameStrokeProperty);
    public double BadgeStroke     => (double)GetValue(BadgeStrokeProperty);

    /// <summary>The shape is a badge behind the icon rather than a frame round the button.</summary>
    public bool IsBadge => (bool)GetValue(IsBadgeProperty);

    /// <summary>Anything but <see cref="RibbonButtonShape.Standard"/> — the content keeps off the rounded edges.</summary>
    public bool IsShaped => (bool)GetValue(IsShapedProperty);

    /// <summary>The frame wears the item's own background, so hovering tints it rather than replacing it.</summary>
    public bool HasCustomFrame => (bool)GetValue(HasCustomFrameProperty);

    /// <summary>The icon's colour on a badge when the item leaves it to the theme: whichever of the theme's text and page
    /// colours reads on the badge. The label, off the badge, keeps the theme's own.</summary>
    public Brush? BadgeForeground => (Brush?)GetValue(BadgeForegroundProperty);

    public bool HasBadgeForeground => (bool)GetValue(HasBadgeForegroundProperty);

    private static DependencyPropertyKey ReadOnly<T>(string name, T fallback)
        => DependencyProperty.RegisterReadOnly(name, typeof(T), typeof(RibbonItemButton), new PropertyMetadata(fallback));

    /// <summary>Visible caption: cut with an ellipsis past <see cref="MaxLabelChars"/>; the tooltip has it whole.</summary>
    public static string DisplayLabel(string label)
        => label.Length > MaxLabelChars ? label[..(MaxLabelChars - 1)] + "…" : label;

    // ── Reading the item ─────────────────────────────────────────────────────

    private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var button = (RibbonItemButton)d;
        if (e.OldValue is RibbonItem old) PropertyChangedEventManager.RemoveHandler(old, button.OnItemPropertyChanged, string.Empty);
        if (e.NewValue is RibbonItem @new) PropertyChangedEventManager.AddHandler(@new, button.OnItemPropertyChanged, string.Empty);
        button.Apply();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e) => Apply();

    private void Apply()
    {
        if (Item is not { } item) return;

        SetValue(IconKey, item.Icon);
        SetValue(CaptionKey, DisplayLabel(item.Label));
        ToolTip = item.Label;
        // A preview names itself in markup; on the ribbon a button is found by its label.
        var id = AutomationProperties.GetAutomationId(this);
        if (string.IsNullOrEmpty(id) || id.StartsWith("Ribbon_", StringComparison.Ordinal))
            AutomationProperties.SetAutomationId(this, "Ribbon_" + item.Label);

        // Local values outrank the template's hover trigger, so an item's own colour holds on hover while the theme's
        // muted default brightens as it always has.
        if (item.Foreground.ToBrush() is { } foreground) Foreground = foreground;
        else if (RibbonColourSlots.EffectiveForeground(item, TryFindResource) is { } readable) Foreground = Frozen(readable);
        else if (item.IsActive) SetResourceReference(ForegroundProperty, "AccentBrush");
        else ClearValue(ForegroundProperty);

        var isBadge    = RibbonShapeGeometry.IsBadge(item.Shape);
        var background = item.Background.ToBrush();
        var stroke     = RibbonShapeGeometry.StrokeThickness(item.BorderWeight);

        if (!isBadge && background is not null) Background = background;
        else ClearValue(BackgroundProperty);
        SetValue(HasCustomFrameKey, !isBadge && background is not null);

        if (item.BorderColor.ToBrush() is { } border) BorderBrush = border;
        else ClearValue(BorderBrushProperty);

        SetValue(IsBadgeKey, isBadge);
        SetValue(IsShapedKey, item.Shape != RibbonButtonShape.Standard);
        SetValue(FrameStrokeKey, isBadge ? 0d : stroke);
        SetValue(BadgeStrokeKey, isBadge ? stroke : 0d);
        SetValue(BadgeSizeKey, RibbonShapeGeometry.BadgeSize(IsCompact));
        SetValue(BadgeGeometryKey, RibbonShapeGeometry.Badge(item.Shape, IsCompact, item.BorderWeight));
        var badgeFill = isBadge ? background ?? TryFindResource("AccentSubtleBrush") as Brush : null;
        SetValue(BadgeFillKey, badgeFill);

        Brush? badgeForeground = null;
        if (item.Foreground.IsDefault && badgeFill is SolidColorBrush { Color.A: > 0x80 } fill)
            badgeForeground = Frozen(RibbonColourSlots.ReadableOn(fill.Color, TryFindResource));
        SetValue(BadgeForegroundKey, badgeForeground);
        SetValue(HasBadgeForegroundKey, badgeForeground is not null);

        UpdateFrame();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateFrame();
    }

    private void UpdateFrame()
    {
        if (Item is not { } item || RenderSize.Width <= 0 || RenderSize.Height <= 0) return;

        var shape = IsBadge ? RibbonButtonShape.Standard : item.Shape;
        var key   = (shape, RenderSize, IsCompact, FrameStroke);
        if (key == _frameFor && FrameGeometry is not null) return;

        _frameFor = key;
        SetValue(FrameGeometryKey, RibbonShapeGeometry.Frame(shape, RenderSize, IsCompact, FrameStroke));
    }

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }
}
