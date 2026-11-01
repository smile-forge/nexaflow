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
    private readonly ImageViewModel _viewer;
    private readonly ImageEditorViewModel _editor;

    private PaintStroke? _stroke;

    // What the open stroke lands on, as it was when it began.
    private (LayeredSession Document, Guid Layer, bool Mask, TiledPixels? Pixels, TiledCoverage? Coverage)? _strokeOn;

    internal LayerPainting(ImageViewModel viewer, ImageEditorViewModel editor)
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
    }

    private static byte Grey(SKColor colour) => (byte)Math.Round(0.2126 * colour.Red + 0.7152 * colour.Green + 0.0722 * colour.Blue);

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
        var colour = _editor.InkSkColour;
        document.Commit(p => p.WithLayer(id, l => mask && l.Mask is { } m
            ? l with { Mask = m with { Coverage = stroke.Drawn(coverage!, m.Coverage, area, Grey(colour)) } }
            : l is PixelLayer layer
                ? layer with { Pixels = stroke.Drawn(pixels!, layer.Pixels, area, colour, layer.AlphaLocked, p.Background.Colour) }
                : l));
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
        var colour = _editor.InkSkColour;
        document.Commit(p => p.WithLayer(layer.Id, l => mask && l.Mask is { } m
            ? l with { Mask = m with { Coverage = m.Coverage.LaidOver(reach, Grey(colour)) } }
            : l is PixelLayer pixels ? pixels with { Pixels = pixels.Pixels.Filled(reach, colour, keepAlpha: pixels.AlphaLocked) } : l));
    }

    // ── Eyedropper ───────────────────────────────────────────────────────────

    /// <summary>The colour at <paramref name="at"/> becomes the colour painted with: what shows, or the active layer's own.</summary>
    public void PickAt(SKPoint at)
    {
        if (Picked(at) is not { Alpha: > 0 } colour) return;
        Colour = ColorSpec.Custom(Color.FromArgb(255, colour.Red, colour.Green, colour.Blue));
    }

    private SKColor? Picked(SKPoint at)
    {
        int x = (int)Math.Floor(at.X), y = (int)Math.Floor(at.Y);
        if (!SampleMerged && _viewer.CurrentDocument is { } document && document.Picture.Active is { } layer)
        {
            var picture = document.Picture;
            if (x < 0 || y < 0 || x >= picture.Width || y >= picture.Height) return null;
            var spot = new SKRectI(x, y, x + 1, y + 1);
            if (picture.EditsMask && layer.Mask is { } mask)
            {
                byte value = mask.Coverage.Read(spot)[0];
                return new SKColor(value, value, value);
            }
            if (layer is PixelLayer pixels)
            {
                var rgba = pixels.Pixels.Read(spot);
                return Straight(rgba[0], rgba[1], rgba[2], rgba[3]);
            }
        }
        return Shown(at);
    }

    // What shows at a canvas point: the picture on screen, every layer together, at the zoom it is drawn at.
    private SKColor? Shown(SKPoint at)
    {
        if (_viewer.CurrentImage is not BitmapSource image || _viewer.Canvas is not { } canvas || canvas.Size.Width <= 0 || canvas.Size.Height <= 0) return null;
        var p = canvas.DocumentToCanvas.MapPoint(at);
        int x = (int)Math.Floor(p.X * image.PixelWidth / canvas.Size.Width), y = (int)Math.Floor(p.Y * image.PixelHeight / canvas.Size.Height);
        if (x < 0 || y < 0 || x >= image.PixelWidth || y >= image.PixelHeight) return null;
        BitmapSource bgra = image.Format == PixelFormats.Pbgra32 || image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        bgra.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return bgra.Format == PixelFormats.Pbgra32 ? Straight(pixel[2], pixel[1], pixel[0], pixel[3]) : new SKColor(pixel[2], pixel[1], pixel[0], pixel[3]);
    }

    private static SKColor Straight(byte r, byte g, byte b, byte a)
        => a == 0 ? SKColors.Transparent
         : new SKColor((byte)Math.Min(255, r * 255 / a), (byte)Math.Min(255, g * 255 / a), (byte)Math.Min(255, b * 255 / a), a);
}
