using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Common.Theming;
using SkiaSharp;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>The units a new canvas's width and height are given in.</summary>
public enum CanvasUnit { Pixels, Millimetres, Inches }

/// <summary>What fills a new canvas behind its layers.</summary>
public enum CanvasBackgroundKind { Transparent, White, Colour }

/// <summary>A canvas size offered by name: <see cref="Width"/> × <see cref="Height"/> pixels at <see cref="Dpi"/>. The
/// custom one, chosen by typing a size, has none.</summary>
public sealed record CanvasPreset(string Label, int Width, int Height, double Dpi)
{
    public bool IsCustom => Width == 0;
}

/// <summary>A unit offered for the width and height, with its short name.</summary>
public sealed record CanvasUnitChoice(CanvasUnit Unit, string Label);

/// <summary>
/// A new canvas as it is set up before it is made: a size — named, or typed as a width and height in pixels, millimetres
/// or inches at a resolution — and a background. What the New picker's layered picture and the start screen make a blank
/// canvas from.
/// </summary>
public sealed partial class CanvasSetup : ObservableObject
{
    private const double MillimetresPerInch = 25.4;

    /// <summary>The resolution print sizes are offered at.</summary>
    private const double PrintDpi = 300;

    // While the fields follow a size chosen by name or a unit changed, rather than what is typed.
    private bool _showing;

    public CanvasSetup()
    {
        double screenDpi = LayeredPicture.DefaultDpi;
        Presets =
        [
            new(Str.Get("Images.Canvas.Preset.Square"), 1024, 1024, screenDpi),
            new(Str.Get("Images.Canvas.Preset.FullHd"), 1920, 1080, screenDpi),
            new(Str.Get("Images.Canvas.Preset.FourK"), 3840, 2160, screenDpi),
            new(Str.Get("Images.Canvas.Preset.A4"), PrintPixels(210 / MillimetresPerInch), PrintPixels(297 / MillimetresPerInch), PrintDpi),
            new(Str.Get("Images.Canvas.Preset.Letter"), PrintPixels(8.5), PrintPixels(11), PrintDpi),
            new(Str.Get("Images.Canvas.Preset.Social"), 1080, 1350, screenDpi),
            Custom,
        ];
        Units =
        [
            new(CanvasUnit.Pixels, Str.Get("Images.Canvas.Unit.Pixels")),
            new(CanvasUnit.Millimetres, Str.Get("Images.Canvas.Unit.Millimetres")),
            new(CanvasUnit.Inches, Str.Get("Images.Canvas.Unit.Inches")),
        ];
        _unit = Units[0];
        Preset = Presets[1];   // Full HD until the screen's size is known, which is then chosen instead
        _chosen = false;
    }

    private static int PrintPixels(double inches) => (int)Math.Round(inches * PrintDpi);

    /// <summary>The size typed rather than chosen by name.</summary>
    public CanvasPreset Custom { get; } = new(Str.Get("Images.Canvas.Preset.Custom"), 0, 0, 0);

    /// <summary>The sizes offered by name — the screen's first, once it is known — and the custom one last.</summary>
    public ObservableCollection<CanvasPreset> Presets { get; }

    public IReadOnlyList<CanvasUnitChoice> Units { get; }

    /// <summary>The size chosen by name; <see cref="Custom"/> once one is typed.</summary>
    [ObservableProperty] private CanvasPreset? _preset;

    partial void OnPresetChanged(CanvasPreset? value)
    {
        _chosen = true;
        if (value is not { IsCustom: false }) return;
        Show(() =>
        {
            DpiText = Number(value.Dpi);
            WidthText = InUnit(value.Width);
            HeightText = InUnit(value.Height);
        });
    }

    /// <summary>The unit the width and height are typed in; changing it shows the same size in the new unit.</summary>
    [ObservableProperty] private CanvasUnitChoice _unit;

    partial void OnUnitChanged(CanvasUnitChoice? oldValue, CanvasUnitChoice newValue)
    {
        if (oldValue is null || Pixels(WidthText, oldValue.Unit) is not { } width || Pixels(HeightText, oldValue.Unit) is not { } height) return;
        Show(() =>
        {
            WidthText = InUnit(width);
            HeightText = InUnit(height);
        });
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PixelWidth), nameof(IsValid), nameof(SizeNote))]
    private string _widthText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PixelHeight), nameof(IsValid), nameof(SizeNote))]
    private string _heightText = string.Empty;

    /// <summary>The resolution, in dots an inch: what millimetres and inches are counted in, and what the picture says it prints at.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Dpi), nameof(PixelWidth), nameof(PixelHeight), nameof(IsValid), nameof(SizeNote))]
    private string _dpiText = string.Empty;

    partial void OnWidthTextChanged(string value) => Typed();

    partial void OnHeightTextChanged(string value) => Typed();

    partial void OnDpiTextChanged(string value) => Typed();

    // A size typed by hand is the custom one, unless it is still the one chosen by name.
    private void Typed()
    {
        if (_showing || Preset is not { IsCustom: false } chosen) return;
        if (PixelWidth != chosen.Width || PixelHeight != chosen.Height || Dpi != chosen.Dpi) Preset = Custom;
    }

    private void Show(Action show)
    {
        _showing = true;
        try { show(); }
        finally { _showing = false; }
    }

    /// <summary>The resolution typed; null where it is not a number above 0.</summary>
    public double? Dpi => double.TryParse(DpiText, NumberStyles.Float, CultureInfo.CurrentCulture, out var dpi) && dpi > 0 && double.IsFinite(dpi) ? dpi : null;

    /// <summary>The width in pixels; null where what is typed is no size.</summary>
    public int? PixelWidth => Pixels(WidthText, Unit.Unit);

    /// <summary>The height in pixels; null where what is typed is no size.</summary>
    public int? PixelHeight => Pixels(HeightText, Unit.Unit);

    /// <summary>Whether the size is one a canvas can have — at least a pixel, and at most <see cref="LayeredPicture.MaxSide"/>, a
    /// side — at a resolution.</summary>
    public bool IsValid => Dpi is not null && PixelWidth is >= 1 and <= LayeredPicture.MaxSide && PixelHeight is >= 1 and <= LayeredPicture.MaxSide;

    /// <summary>The size in pixels, or what is wrong with it.</summary>
    public string SizeNote => Dpi is null ? Str.Get("Images.Canvas.NoDpi") : (PixelWidth, PixelHeight) switch
    {
        (null, _) or (_, null)                                              => Str.Get("Images.Canvas.NoSize"),
        ({ } w, { } h) when w > LayeredPicture.MaxSide || h > LayeredPicture.MaxSide => Str.Format("Images.Canvas.TooLargeFormat", LayeredPicture.MaxSide),
        ({ } w, { } h) when w < 1 || h < 1                                   => Str.Get("Images.Canvas.NoSize"),
        ({ } w, { } h)                                                       => Str.Format("Images.Canvas.PixelsFormat", w, h),
    };

    private int? Pixels(string text, CanvasUnit unit)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) || !double.IsFinite(value) || value <= 0) return null;
        double pixels = unit switch
        {
            CanvasUnit.Pixels      => value,
            CanvasUnit.Millimetres => Dpi is { } mmDpi ? value / MillimetresPerInch * mmDpi : double.NaN,
            CanvasUnit.Inches      => Dpi is { } inDpi ? value * inDpi : double.NaN,
            _                      => double.NaN,
        };
        return double.IsFinite(pixels) && pixels < int.MaxValue ? (int)Math.Round(pixels) : null;
    }

    private string InUnit(int pixels) => Unit.Unit switch
    {
        CanvasUnit.Millimetres when Dpi is { } mmDpi => Number(pixels / mmDpi * MillimetresPerInch),
        CanvasUnit.Inches when Dpi is { } inDpi      => Number(pixels / inDpi),
        _                                            => Number(pixels),
    };

    private static string Number(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>Turns the canvas a quarter: the width and height trade places.</summary>
    [RelayCommand]
    private void SwapOrientation()
    {
        var (width, height) = (WidthText, HeightText);
        WidthText = height;
        HeightText = width;
    }

    // The screen's size, where it is offered.
    private CanvasPreset? _screen;

    // Whether a size has been chosen — named, or typed — since the setup was made: until one is, the screen's is chosen once known.
    private bool _chosen;

    /// <summary>Offers the screen's size, first, as the screen shows it in pixels — known once the setup is on one — and chooses
    /// it where no size has been chosen yet: a canvas the size of the screen is what most want who have nothing else in mind.</summary>
    public void UseScreen(SKSizeI pixels)
    {
        if (pixels.Width < 1 || pixels.Height < 1) return;
        var screen = new CanvasPreset(Str.Get("Images.Canvas.Preset.Screen"), pixels.Width, pixels.Height, LayeredPicture.DefaultDpi);
        if (_screen is not { } offered)
        {
            bool chosen = _chosen;
            _screen = screen;
            Presets.Insert(0, screen);
            if (chosen) return;
            Preset = screen;
            _chosen = false;
            return;
        }
        if (offered == screen) return;
        bool following = ReferenceEquals(Preset, offered);
        _screen = screen;
        Presets[Presets.IndexOf(offered)] = screen;
        if (following) Preset = screen;
    }

    /// <summary>What fills the canvas behind its layers: shown, never a layer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TakesColour))]
    private CanvasBackgroundKind _background = CanvasBackgroundKind.Transparent;

    /// <summary>Whether the background is a colour of its own, which <see cref="BackgroundColour"/> says.</summary>
    public bool TakesColour => Background == CanvasBackgroundKind.Colour;

    /// <summary>The background's colour, where it is one.</summary>
    [ObservableProperty] private ColorSpec _backgroundColour = ColorSpec.Custom(Colors.Black);

    /// <summary>The background as the picture keeps it.</summary>
    public LayerBackground LayerBackground => Background switch
    {
        CanvasBackgroundKind.White  => LayerBackground.White,
        CanvasBackgroundKind.Colour => new LayerBackground(SpecColours.Argb(BackgroundColour)),
        _                           => LayerBackground.Transparent,
    };

    /// <summary>The blank canvas set up — one empty pixel layer named <paramref name="layerName"/> — or null while the size is no size.</summary>
    public LayeredPicture? Make(string layerName)
        => IsValid && Dpi is { } dpi ? LayeredPicture.Blank(PixelWidth!.Value, PixelHeight!.Value, LayerBackground, layerName, dpi) : null;
}
