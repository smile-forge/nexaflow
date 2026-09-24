using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>A swatch as a picker offers it: the bank entry, and the id a journey clicks it by.</summary>
public sealed record SwatchChip(string Key, string Name, Brush Brush, string AutomationId);

/// <summary>
/// Picks a <see cref="ColorSpec"/>: the theme default, a token from the <c>Swatch.*</c> bank (which follows the
/// theme), or any colour at all — dragged on a saturation/value square with hue and alpha strips, typed as hex or
/// R/G/B/A, or sampled off the screen with <see cref="ScreenEyedropper"/>.
/// <para>
/// <see cref="Value"/> binds two-way. Set <see cref="AutomationPrefix"/> to the owner's prefix; the parts are
/// <c>{prefix}_Default</c>, <c>_Eyedropper</c>, <c>_Hex</c>, <c>_Red</c>/<c>_Green</c>/<c>_Blue</c>/<c>_Alpha</c> and
/// <c>_Swatch{Name}</c>.
/// </para>
/// </summary>
public partial class ColorPicker : UserControl
{
    private HsvColor _hsv = new(0, 0, 0.5);
    private byte _alpha = 255;
    private bool _pushing;
    private ColorSpec _original;
    private bool _hasOriginal;

    public ColorPicker()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            BuildSwatches();
            Refresh();
        };
        SizeChanged += (_, _) => PlaceThumbs();
    }

    // ── Value ────────────────────────────────────────────────────────────────

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(ColorSpec), typeof(ColorPicker),
        new FrameworkPropertyMetadata(ColorSpec.Default, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueChanged));

    public ColorSpec Value
    {
        get => (ColorSpec)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Whether "Theme default" is offered. Off for a slot that must always hold a colour.</summary>
    public static readonly DependencyProperty AllowDefaultProperty = DependencyProperty.Register(
        nameof(AllowDefault), typeof(bool), typeof(ColorPicker),
        new PropertyMetadata(true, (d, e) => ((ColorPicker)d).DefaultChip.Visibility =
            (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));

    public bool AllowDefault
    {
        get => (bool)GetValue(AllowDefaultProperty);
        set => SetValue(AllowDefaultProperty, value);
    }

    /// <summary>What "Theme default" looks like where this colour is used, so the square can start from it.</summary>
    public static readonly DependencyProperty DefaultColorProperty = DependencyProperty.Register(
        nameof(DefaultColor), typeof(Color), typeof(ColorPicker),
        new PropertyMetadata(Colors.Gray, (d, _) => ((ColorPicker)d).OnDefaultColorChanged()));

    public Color DefaultColor
    {
        get => (Color)GetValue(DefaultColorProperty);
        set => SetValue(DefaultColorProperty, value);
    }

    /// <summary>Forgets the colour the before/after preview compares against; the next value becomes "before".</summary>
    public void ResetOriginal() => _hasOriginal = false;

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ColorPicker)d;
        if (picker._pushing) return;
        picker.LoadFrom((ColorSpec)e.NewValue);
    }

    private void OnDefaultColorChanged()
    {
        if (Value.IsDefault) LoadFrom(Value);
    }

    private void LoadFrom(ColorSpec spec)
    {
        if (!_hasOriginal)
        {
            _original    = spec;
            _hasOriginal = true;
        }

        var color = spec.Resolve() ?? DefaultColor;
        var hsv   = HsvColor.FromColor(color);
        // A grey has no hue of its own; keep the one the strip is on rather than jumping it to red.
        _hsv   = hsv.S == 0 || hsv.V == 0 ? hsv with { H = _hsv.H } : hsv;
        _alpha = color.A;
        Refresh();
    }

    // ── Automation ids ───────────────────────────────────────────────────────

    public static readonly DependencyProperty AutomationPrefixProperty = DependencyProperty.Register(
        nameof(AutomationPrefix), typeof(string), typeof(ColorPicker),
        new PropertyMetadata(string.Empty, OnAutomationPrefixChanged));

    public string AutomationPrefix
    {
        get => (string)GetValue(AutomationPrefixProperty);
        set => SetValue(AutomationPrefixProperty, value);
    }

    private static readonly DependencyPropertyKey DefaultAutomationIdKey    = ReadOnlyId(nameof(DefaultAutomationId));
    private static readonly DependencyPropertyKey EyedropperAutomationIdKey = ReadOnlyId(nameof(EyedropperAutomationId));
    private static readonly DependencyPropertyKey HexAutomationIdKey        = ReadOnlyId(nameof(HexAutomationId));
    private static readonly DependencyPropertyKey RedAutomationIdKey        = ReadOnlyId(nameof(RedAutomationId));
    private static readonly DependencyPropertyKey GreenAutomationIdKey      = ReadOnlyId(nameof(GreenAutomationId));
    private static readonly DependencyPropertyKey BlueAutomationIdKey       = ReadOnlyId(nameof(BlueAutomationId));
    private static readonly DependencyPropertyKey AlphaAutomationIdKey      = ReadOnlyId(nameof(AlphaAutomationId));

    public static readonly DependencyProperty DefaultAutomationIdProperty    = DefaultAutomationIdKey.DependencyProperty;
    public static readonly DependencyProperty EyedropperAutomationIdProperty = EyedropperAutomationIdKey.DependencyProperty;
    public static readonly DependencyProperty HexAutomationIdProperty        = HexAutomationIdKey.DependencyProperty;
    public static readonly DependencyProperty RedAutomationIdProperty        = RedAutomationIdKey.DependencyProperty;
    public static readonly DependencyProperty GreenAutomationIdProperty      = GreenAutomationIdKey.DependencyProperty;
    public static readonly DependencyProperty BlueAutomationIdProperty       = BlueAutomationIdKey.DependencyProperty;
    public static readonly DependencyProperty AlphaAutomationIdProperty      = AlphaAutomationIdKey.DependencyProperty;

    public string DefaultAutomationId    => (string)GetValue(DefaultAutomationIdProperty);
    public string EyedropperAutomationId => (string)GetValue(EyedropperAutomationIdProperty);
    public string HexAutomationId        => (string)GetValue(HexAutomationIdProperty);
    public string RedAutomationId        => (string)GetValue(RedAutomationIdProperty);
    public string GreenAutomationId      => (string)GetValue(GreenAutomationIdProperty);
    public string BlueAutomationId       => (string)GetValue(BlueAutomationIdProperty);
    public string AlphaAutomationId      => (string)GetValue(AlphaAutomationIdProperty);

    private static DependencyPropertyKey ReadOnlyId(string name)
        => DependencyProperty.RegisterReadOnly(name, typeof(string), typeof(ColorPicker), new PropertyMetadata(string.Empty));

    private static void OnAutomationPrefixChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ColorPicker)d;
        var prefix = (string?)e.NewValue ?? string.Empty;
        picker.SetValue(DefaultAutomationIdKey,    $"{prefix}_Default");
        picker.SetValue(EyedropperAutomationIdKey, $"{prefix}_Eyedropper");
        picker.SetValue(HexAutomationIdKey,        $"{prefix}_Hex");
        picker.SetValue(RedAutomationIdKey,        $"{prefix}_Red");
        picker.SetValue(GreenAutomationIdKey,      $"{prefix}_Green");
        picker.SetValue(BlueAutomationIdKey,       $"{prefix}_Blue");
        picker.SetValue(AlphaAutomationIdKey,      $"{prefix}_Alpha");
        picker.BuildSwatches();
    }

    // ── Swatches ─────────────────────────────────────────────────────────────

    private static readonly DependencyPropertyKey SwatchesKey = DependencyProperty.RegisterReadOnly(
        nameof(Swatches), typeof(IReadOnlyList<SwatchChip>), typeof(ColorPicker),
        new PropertyMetadata(Array.Empty<SwatchChip>()));

    public static readonly DependencyProperty SwatchesProperty = SwatchesKey.DependencyProperty;

    public IReadOnlyList<SwatchChip> Swatches => (IReadOnlyList<SwatchChip>)GetValue(SwatchesProperty);

    private void BuildSwatches()
    {
        // The bank lives in the app's resources; with no app (a headless test) there is simply nothing to offer.
        if (Application.Current is null) return;
        SetValue(SwatchesKey, SwatchPalette.Keys
            .Select(key => (Key: key, Name: key["Swatch.".Length..]))
            .Select(s => new SwatchChip(s.Key, s.Name, SwatchPalette.Resolve(s.Key), $"{AutomationPrefix}_Swatch{s.Name}"))
            .ToList());
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SwatchChip chip }) Value = ColorSpec.FromSwatch(chip.Key);
    }

    private void Default_Click(object sender, RoutedEventArgs e) => Value = ColorSpec.Default;

    private async void Eyedropper_Click(object sender, RoutedEventArgs e)
    {
        if (await ScreenEyedropper.PickAsync() is { } color) Push(color);
    }

    // ── Dragging ─────────────────────────────────────────────────────────────

    private void Drag_Start(object sender, MouseButtonEventArgs e)
    {
        var area = (FrameworkElement)sender;
        area.CaptureMouse();
        DragTo(area, e.GetPosition(area));
        e.Handled = true;
    }

    private void Drag_Move(object sender, MouseEventArgs e)
    {
        var area = (FrameworkElement)sender;
        if (area.IsMouseCaptured) DragTo(area, e.GetPosition(area));
    }

    private void Drag_End(object sender, MouseButtonEventArgs e) => ((FrameworkElement)sender).ReleaseMouseCapture();

    private void DragTo(FrameworkElement area, Point p)
    {
        var x = area.ActualWidth  > 0 ? Math.Clamp(p.X / area.ActualWidth,  0, 1) : 0;
        var y = area.ActualHeight > 0 ? Math.Clamp(p.Y / area.ActualHeight, 0, 1) : 0;

        if (area == SvArea)          _hsv   = _hsv with { S = x, V = 1 - y };
        else if (area == HueStrip)   _hsv   = _hsv with { H = y * 360 };
        else if (area == AlphaStrip) _alpha = (byte)Math.Round((1 - y) * 255);

        Push(_hsv.ToColor(_alpha), keepHsv: true);
    }

    // ── Typed values ─────────────────────────────────────────────────────────

    private void Value_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Commit((TextBox)sender);
        e.Handled = true;
    }

    private void Value_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit((TextBox)sender);

    private void Commit(TextBox box)
    {
        var current = _hsv.ToColor(_alpha);
        if (box == HexBox)
        {
            if (TryParseHex(box.Text, out var parsed)) Push(parsed);
            else Refresh();
            return;
        }

        if (!byte.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var channel))
        {
            Refresh();
            return;
        }

        if (box == RedBox)        current.R = channel;
        else if (box == GreenBox) current.G = channel;
        else if (box == BlueBox)  current.B = channel;
        else if (box == AlphaBox) current.A = channel;
        Push(current);
    }

    /// <summary>Hex with or without its <c>#</c>, in any of the lengths WPF reads (<c>RGB</c>, <c>ARGB</c>,
    /// <c>RRGGBB</c>, <c>AARRGGBB</c>).</summary>
    public static bool TryParseHex(string? text, out Color color)
    {
        color = default;
        text  = text?.Trim();
        if (string.IsNullOrEmpty(text)) return false;
        if (!text.StartsWith('#')) text = "#" + text;
        if (text.Length is not (4 or 5 or 7 or 9)) return false;
        try
        {
            if (ColorConverter.ConvertFromString(text) is not Color c) return false;
            color = c;
            return true;
        }
        catch (FormatException) { return false; }
    }

    // ── Pushing out, painting in ─────────────────────────────────────────────

    private void Push(Color color, bool keepHsv = false)
    {
        if (!keepHsv)
        {
            var hsv = HsvColor.FromColor(color);
            _hsv   = hsv.S == 0 || hsv.V == 0 ? hsv with { H = _hsv.H } : hsv;
            _alpha = color.A;
        }

        _pushing = true;
        try { Value = ColorSpec.Custom(color); }
        finally { _pushing = false; }
        Refresh();
    }

    private void Refresh()
    {
        var color  = _hsv.ToColor(_alpha);
        var opaque = Color.FromRgb(color.R, color.G, color.B);

        SvHue.Fill     = new SolidColorBrush(new HsvColor(_hsv.H, 1, 1).ToColor());
        SvThumb.Fill   = new SolidColorBrush(opaque);
        AlphaFill.Fill = new LinearGradientBrush(opaque, Color.FromArgb(0, color.R, color.G, color.B), 90);

        PreviewNew.Background = Value.IsDefault ? new SolidColorBrush(DefaultColor) : new SolidColorBrush(color);
        PreviewOld.Background = new SolidColorBrush(_original.Resolve() ?? DefaultColor);

        SelectionLabel.Text = Value.Kind switch
        {
            ColorSpecKind.Default => Str.Get("Common.ColorPicker.IsThemeDefault"),
            ColorSpecKind.Swatch  => Str.Format("Common.ColorPicker.IsSwatch", Value.SwatchKey!["Swatch.".Length..]),
            _                     => Str.Get("Common.ColorPicker.IsCustom"),
        };

        SetUnlessEditing(HexBox,   color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : color.ToString());
        SetUnlessEditing(RedBox,   color.R.ToString(CultureInfo.InvariantCulture));
        SetUnlessEditing(GreenBox, color.G.ToString(CultureInfo.InvariantCulture));
        SetUnlessEditing(BlueBox,  color.B.ToString(CultureInfo.InvariantCulture));
        SetUnlessEditing(AlphaBox, color.A.ToString(CultureInfo.InvariantCulture));

        PlaceThumbs();
    }

    private static void SetUnlessEditing(TextBox box, string text)
    {
        if (!box.IsKeyboardFocused) box.Text = text;
    }

    private void PlaceThumbs()
    {
        Canvas.SetLeft(SvThumb, _hsv.S * SvArea.ActualWidth - SvThumb.Width / 2);
        Canvas.SetTop(SvThumb, (1 - _hsv.V) * SvArea.ActualHeight - SvThumb.Height / 2);

        HueThumb.Width = HueStrip.ActualWidth;
        Canvas.SetTop(HueThumb, _hsv.H / 360 * HueStrip.ActualHeight - HueThumb.Height / 2);

        AlphaThumb.Width = AlphaStrip.ActualWidth;
        Canvas.SetTop(AlphaThumb, (1 - _alpha / 255.0) * AlphaStrip.ActualHeight - AlphaThumb.Height / 2);
    }
}
