using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Painting;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Common.Theming;
using SkiaSharp;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>The Paint tool's ways: lay colour down, take it away, pick it up, or flood an area with it.</summary>
public enum PaintTool
{
    Brush,
    Eraser,
    Eyedropper,
    Fill,
}

/// <summary>
/// The Paint tool: the round brush and the eraser stroked onto the active pixel layer — or, while its mask is edited, onto the
/// mask in greys — each stroke one undo step and only inside the selection; the eyedropper picking the colour of what shows or of
/// the active layer; and the fill flooding what is near the colour clicked. The colour is the one markup chooses.
/// </summary>
public sealed partial class LayerPainting : ObservableObject
{
    private readonly ArtistViewModel _viewer;
    private readonly ImageEditorViewModel _editor;

    private PaintStroke? _stroke;

    // What the open stroke lands on, as it was when it began.
    private (LayeredSession Document, Guid Layer, bool Mask, TiledPixels? Pixels, TiledCoverage? Coverage)? _strokeOn;

    internal LayerPainting(ArtistViewModel viewer, ImageEditorViewModel editor)
    {
        _viewer = viewer;
        _editor = editor;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ImageEditorViewModel.InkColour) or "") OnPropertyChanged(nameof(Colour));
        };
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrush), nameof(IsEraser), nameof(IsEyedropper), nameof(IsFill), nameof(PaintsWithTip))]
    private PaintTool _tool = PaintTool.Brush;

    public bool IsBrush      => Tool == PaintTool.Brush;
    public bool IsEraser     => Tool == PaintTool.Eraser;
    public bool IsEyedropper => Tool == PaintTool.Eyedropper;
    public bool IsFill       => Tool == PaintTool.Fill;

    /// <summary>The brush or the eraser: the tip's settings apply.</summary>
    public bool PaintsWithTip => Tool is PaintTool.Brush or PaintTool.Eraser;

    /// <summary>The tip's diameter, in canvas pixels.</summary>
    [ObservableProperty] private double _size = 24;

    /// <summary>How much of the tip is solid, in percent.</summary>
    [ObservableProperty] private double _hardness = 80;

    /// <summary>The most a stroke covers, in percent.</summary>
    [ObservableProperty] private double _opacity = 100;

    /// <summary>How much each dab adds, in percent.</summary>
    [ObservableProperty] private double _flow = 100;

    /// <summary>The distance between dabs, in percent of the size.</summary>
    [ObservableProperty] private double _spacing = 15;

    [ObservableProperty] private bool _pressureSizes = true;

    [ObservableProperty] private bool _pressureOpacity;

    /// <summary>How far from the colour clicked the fill still reaches, 0–255 on each channel.</summary>
    [ObservableProperty] private double _tolerance = 32;

    /// <summary>The fill reaches only what is joined to where it was clicked.</summary>
    [ObservableProperty] private bool _contiguous = true;

    /// <summary>The eyedropper picks what shows, every layer together; off, the active layer's own colour.</summary>
    [ObservableProperty] private bool _sampleMerged = true;

    /// <summary>Stops of light past the colour picked that a 32-bit picture is painted with — 0 paints it as picked, 1 twice as bright.</summary>
    [ObservableProperty] private double _intensity;

    /// <summary>The picture is 32-bit: its colour is light, and Intensity applies.</summary>
    public bool PaintsLight => _viewer.CurrentDocument?.Picture.Colour.IsLinear == true;

    /// <summary>The colour painted with: markup's, one choice for both.</summary>
    public ColorSpec Colour
    {
        get => _editor.InkColour;
        set => _editor.InkColour = value;
    }

    internal BrushSettings Brush => new()
    {
        Size = Math.Clamp(Size, BrushSettings.MinSize, BrushSettings.MaxSize),
        Hardness = Hardness / 100, Opacity = Opacity / 100, Flow = Flow / 100, Spacing = Math.Max(1, Spacing) / 100,
        PressureSizes = PressureSizes, PressureOpacity = PressureOpacity,
    };

    /// <summary>Makes the photo a layered picture with an empty layer to paint on, where it is not one.</summary>
    public IRelayCommand AddLayerCommand => _viewer.AddLayerCommand;

    // What paint lands on now: the active layer's mask while it is edited, else the active layer where it holds pixels.
    private (LayeredSession Document, Layer Layer, bool Mask)? Surface()
    {
        if (_viewer.CurrentDocument is not { } document || document.Picture.Active is not { Locked: false } layer) return null;
        if (document.Picture.EditsMask && layer.Mask is not null) return (document, layer, true);
        return layer is PixelLayer ? (document, layer, false) : null;
    }

    /// <summary>Whether there is something to paint on: a pixel layer, or a mask being edited, not locked.</summary>
    public bool CanPaint => Surface() is not null;

    /// <summary>Why nothing can be painted on, where nothing can.</summary>
    public string Notice => _viewer.CurrentDocument is null ? Str.Get("Images.Paint.NoLayers") : Str.Get("Images.Paint.NotPixels");

    /// <summary>The photo is no layered picture yet: Add layer is offered, to paint on.</summary>
    public bool OffersLayer => _viewer.CurrentDocument is null;

    /// <summary>The picture or its active layer changed: whether it can be painted on is read again.</summary>
    internal void Refresh()
    {
        OnPropertyChanged(nameof(CanPaint));
        OnPropertyChanged(nameof(Notice));
        OnPropertyChanged(nameof(OffersLayer));
        OnPropertyChanged(nameof(PaintsLight));
    }

    /// <summary>
    /// The colour painted with, in <paramref name="picture"/>'s values: the colour picked, made linear for a 32-bit picture and
    /// brightened there by <see cref="Intensity"/> — light past white, as a 32-bit picture holds it.
    /// </summary>
    internal SKColorF InkIn(LayeredPicture picture)
    {
        var picked = picture.Colour.FromSrgb(_editor.InkSkColour);
        if (!picture.Colour.IsLinear || Intensity == 0) return picked;
        float gain = (float)Math.Pow(2, Intensity);
        return new SKColorF(picked.Red * gain, picked.Green * gain, picked.Blue * gain, picked.Alpha);
    }

    // ── Strokes ──────────────────────────────────────────────────────────────

    /// <summary>A stroke begins at <paramref name="at"/> (canvas pixels): one undo step until it ends. Gives whether it did — the
    /// brush or eraser chosen, and something to paint on.</summary>
    public bool BeginStroke(SKPoint at, float pressure)
    {
        if (!PaintsWithTip || Surface() is not { } surface) return false;
        var (document, layer, mask) = surface;
        var picture = document.Picture;
        _stroke = new PaintStroke(Brush, Tool == PaintTool.Eraser ? PaintKind.Eraser : PaintKind.Brush, picture.Width, picture.Height,
                                  picture.Selection.IsEmpty ? null : picture.Selection);
        _strokeOn = (document, layer.Id, mask, (layer as PixelLayer)?.Pixels, mask ? layer.Mask!.Coverage : null);
        document.History.BeginGesture();
        ContinueStroke(at, pressure);
        return true;
    }

    /// <summary>The stroke reached <paramref name="at"/>: the dabs up to it are painted.</summary>
    public void ContinueStroke(SKPoint at, float pressure)
    {
        if (_stroke is not { } stroke || _strokeOn is not { } on) return;
        var (document, id, mask, pixels, coverage) = on;
        var area = stroke.To(at, pressure);
        if (area.IsEmpty) return;
        document.Commit(p =>
        {
            var colour = InkIn(p);
            return p.WithLayer(id, l => mask && l.Mask is { } m
                ? l with { Mask = m with { Coverage = stroke.Drawn(coverage!, m.Coverage, area, p.Colour.ShownGrey(colour)) } }
                : l is PixelLayer layer
                    ? layer with { Pixels = stroke.Drawn(pixels!, layer.Pixels, area, colour, layer.AlphaLocked, p.Colour.FromSrgb(p.Background.Colour)) }
                    : l);
        });
    }

    /// <summary>The stroke ended: it is one step.</summary>
    public void EndStroke()
    {
        _strokeOn?.Document.History.EndGesture();
        _stroke = null;
        _strokeOn = null;
    }

    /// <summary>The stroke was let go: what it painted goes.</summary>
    public void CancelStroke()
    {
        _strokeOn?.Document.History.CancelGesture();
        _stroke = null;
        _strokeOn = null;
    }

    // ── Fill ─────────────────────────────────────────────────────────────────

    /// <summary>Floods what is near the colour at <paramref name="at"/> with the colour — inside the selection, where there is one:
    /// one step.</summary>
    public void FillAt(SKPoint at)
    {
        if (Surface() is not { } surface) return;
        var (document, layer, mask) = surface;
        var picture = document.Picture;
        int x = (int)Math.Floor(at.X), y = (int)Math.Floor(at.Y), tolerance = (int)Math.Round(Tolerance);
        if (x < 0 || y < 0 || x >= picture.Width || y >= picture.Height) return;
        var reach = mask
            ? FloodFill.Reach(layer.Mask!.Coverage, x, y, tolerance, Contiguous)
            : FloodFill.Reach(((PixelLayer)layer).Pixels, picture.Width, picture.Height, x, y, tolerance, Contiguous);
        if (!picture.Selection.IsEmpty) reach = reach.Combined(picture.Selection, RegionMode.Intersect);
        if (reach.IsEmpty) return;
        document.Commit(p =>
        {
            var colour = InkIn(p);
            return p.WithLayer(layer.Id, l => mask && l.Mask is { } m
                ? l with { Mask = m with { Coverage = m.Coverage.LaidOver(reach, p.Colour.ShownGrey(colour)) } }
                : l is PixelLayer pixels ? pixels with { Pixels = pixels.Pixels.Filled(reach, colour, keepAlpha: pixels.AlphaLocked) } : l);
        });
    }

    // ── Eyedropper ───────────────────────────────────────────────────────────

    /// <summary>The colour at <paramref name="at"/> becomes the colour painted with: what shows, or the active layer's own. On a
    /// 32-bit picture light past white is picked as a colour and an <see cref="Intensity"/>.</summary>
    public void PickAt(SKPoint at)
    {
        if (_viewer.CurrentDocument is not { } document || Picked(document, at) is not { Alpha: > 0 } value) return;
        var colour = document.Picture.Colour;
        float brightest = Math.Max(value.Red, Math.Max(value.Green, value.Blue));
        double stops = colour.IsLinear && brightest > 1 ? Math.Log2(brightest) : 0;
        float scale = (float)Math.Pow(2, -stops);
        var shown = colour.ToSrgb(new SKColorF(value.Red * scale, value.Green * scale, value.Blue * scale, 1));
        static byte Byte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255), 0, 255);
        Colour = ColorSpec.Custom(Color.FromArgb(255, Byte(shown.Red), Byte(shown.Green), Byte(shown.Blue)));
        if (colour.IsLinear) Intensity = Math.Round(stops, 2);
    }

    /// <summary>The value at <paramref name="at"/>, in the picture's values, not premultiplied: what shows, every layer together,
    /// or the active layer's own — or its mask's grey while the mask is edited.</summary>
    private SKColorF? Picked(LayeredSession document, SKPoint at)
    {
        int x = (int)Math.Floor(at.X), y = (int)Math.Floor(at.Y);
        var picture = document.Picture;
        if (x < 0 || y < 0 || x >= picture.Width || y >= picture.Height) return null;
        if (!SampleMerged && picture.Active is { } layer)
        {
            var spot = new SKRectI(x, y, x + 1, y + 1);
            if (picture.EditsMask && layer.Mask is { } mask)
            {
                float value = mask.Coverage.ReadValues(spot)[0];
                return new SKColorF(value, value, value, 1);
            }
            if (layer is PixelLayer pixels)
            {
                var premul = pixels.Pixels.ReadPremul(spot);
                float a = premul[3];
                return a <= 0 ? new SKColorF(0, 0, 0, 0) : new SKColorF(premul[0] / a, premul[1] / a, premul[2] / a, a);
            }
        }
        return document.ShownAt(at);
    }
}
