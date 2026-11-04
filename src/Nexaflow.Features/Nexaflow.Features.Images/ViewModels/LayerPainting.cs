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
    private readonly ImagesConfig _config;

    private PaintStroke? _stroke;

    // What the open stroke lands on, as it was when it began.
    private (LayeredSession Document, Guid Layer, bool Mask, TiledPixels? Pixels, TiledCoverage? Coverage)? _strokeOn;

    /// <param name="config">The Images settings, read as each stroke begins: the pen's pressure and the brush's spacing.</param>
    internal LayerPainting(ArtistViewModel viewer, ImageEditorViewModel editor, ImagesConfig config)
    {
        _viewer = viewer;
        _editor = editor;
        _config = config;
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
        Hardness = Hardness / 100, Opacity = Opacity / 100, Flow = Flow / 100, Spacing = Math.Clamp(_config.BrushSpacing, 1, 200) / 100.0,
        PressureSizes = _config.PenPressureSize, PressureOpacity = _config.PenPressureOpacity,
    };

    // What paint lands on now: the active layer's mask while it is edited, else the active layer where it holds pixels.
    private (LayeredSession Document, Layer Layer, bool Mask)? Surface()
    {
        if (_viewer.CurrentDocument is not { } document || document.Picture.Active is not { Locked: false } layer) return null;
        if (document.Picture.EditsMask && layer.Mask is not null) return (document, layer, true);
        return layer is PixelLayer ? (document, layer, false) : null;
    }

    // What paint lands on, made where there is none — the photo made a layered picture, and a new pixel layer added above the
    // active one where that holds no pixels or is locked — inside the step that begins here, so a layer made comes and goes with
    // the stroke or fill that made it. Null where there is no picture to paint.
    private (LayeredSession Document, Layer Layer, bool Mask)? BeginOnSurface()
    {
        if (Surface() is { } found)
        {
            found.Document.History.BeginGesture();
            return found;
        }
        if (_viewer.MakeLayered() is not { } document) return null;
        document.History.BeginGesture();
        _viewer.Layers.AddPixelLayerCommand.Execute(null);
        if (Surface() is { } made) return made;
        document.History.CancelGesture();
        return null;
    }

    /// <summary>The picture or its active layer changed: whether it holds light is read again.</summary>
    internal void Refresh()
    {
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

    /// <summary>A stroke begins at <paramref name="at"/> (canvas pixels): one undo step until it ends, with the layer it lands on
    /// where one had to be made. Gives whether it did — the brush or eraser chosen, and a picture to paint.</summary>
    public bool BeginStroke(SKPoint at, float pressure)
    {
        if (!PaintsWithTip || BeginOnSurface() is not { } surface) return false;
        var (document, layer, mask) = surface;
        var picture = document.Picture;
        _stroke = new PaintStroke(Brush, Tool == PaintTool.Eraser ? PaintKind.Eraser : PaintKind.Brush, picture.Width, picture.Height,
                                  picture.Selection.IsEmpty ? null : picture.Selection);
        _strokeOn = (document, layer.Id, mask, (layer as PixelLayer)?.Pixels, mask ? layer.Mask!.Coverage : null);
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
    /// one step, with the layer it lands on where one had to be made.</summary>
    public void FillAt(SKPoint at)
    {
        if (BeginOnSurface() is not { } surface) return;
        var (document, layer, mask) = surface;
        bool filled = false;
        try
        {
            filled = Flood(document, layer, mask, at);
        }
        finally
        {
            // Nothing reached — a click past the picture — takes back the layer made for it too.
            if (filled) document.History.EndGesture();
            else document.History.CancelGesture();
        }
    }

    // The fill at at, on layer or its mask: whether it reached anything.
    private bool Flood(LayeredSession document, Layer layer, bool mask, SKPoint at)
    {
        var picture = document.Picture;
        int x = (int)Math.Floor(at.X), y = (int)Math.Floor(at.Y), tolerance = (int)Math.Round(Tolerance);
        if (x < 0 || y < 0 || x >= picture.Width || y >= picture.Height) return false;
        var reach = mask
            ? FloodFill.Reach(layer.Mask!.Coverage, x, y, tolerance, Contiguous)
            : FloodFill.Reach(((PixelLayer)layer).Pixels, picture.Width, picture.Height, x, y, tolerance, Contiguous);
        if (!picture.Selection.IsEmpty) reach = reach.Combined(picture.Selection, RegionMode.Intersect);
        if (reach.IsEmpty) return false;
        document.Commit(p =>
        {
            var colour = InkIn(p);
            return p.WithLayer(layer.Id, l => mask && l.Mask is { } m
                ? l with { Mask = m with { Coverage = m.Coverage.LaidOver(reach, p.Colour.ShownGrey(colour)) } }
                : l is PixelLayer pixels ? pixels with { Pixels = pixels.Pixels.Filled(reach, colour, keepAlpha: pixels.AlphaLocked) } : l);
        });
        return true;
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
