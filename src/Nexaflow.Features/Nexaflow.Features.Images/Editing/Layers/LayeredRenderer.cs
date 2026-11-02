using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.Features.Images.Editing.Rendering;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Layers;

/// <summary>
/// A layered picture drawn at full size, apart from any preview — for a save, an export or a print: every picture layer
/// rendered at its full resolution first, then each layer by itself as a file holds it and the whole picture composited
/// through the same compositor the display uses, so what is written is what was shown.
/// </summary>
public sealed class LayeredRenderer : ILayeredPictureRenderer, IPictureLayerImages, IDisposable
{
    private readonly LayeredPicture _picture;
    private readonly MarkupPainter _markup;
    private readonly Dictionary<Guid, SKImage> _pictures;

    private LayeredRenderer(LayeredPicture picture, MarkupPainter markup, Dictionary<Guid, SKImage> pictures)
        => (_picture, _markup, _pictures) = (picture, markup, pictures);

    /// <summary>Renders every picture layer of <paramref name="picture"/> at full size, from what the picture tools made for
    /// each (<paramref name="made"/>).</summary>
    /// <exception cref="InvalidOperationException">A layer's recipe asks the picture tools for something not made yet.</exception>
    public static async Task<LayeredRenderer> PrepareAsync(LayeredPicture picture, Func<PictureLayer, MadeByTools> made, ImageEditingServices services,
                                                           CancellationToken ct = default)
    {
        var pictures = new Dictionary<Guid, SKImage>();
        try
        {
            foreach (var layer in picture.All.OfType<PictureLayer>())
                pictures[layer.Id] = await services.Exporter.RenderAsync(layer.Source.Path, layer.Recipe, made(layer), ct).ConfigureAwait(false);
        }
        catch
        {
            foreach (var image in pictures.Values) image.Dispose();
            throw;
        }
        return new LayeredRenderer(picture, services.Markup, pictures);
    }

    public (SKBitmap Pixels, SKPointI At)? RenderLayer(Layer layer)
    {
        switch (layer)
        {
            case PictureLayer picture when _pictures.TryGetValue(picture.Id, out var image):
            {
                var toCanvas = picture.Placement.ToMatrix();
                return Draw(_picture.Colour, toCanvas.MapRect(SKRect.Create(image.Width, image.Height)), canvas =>
                {
                    canvas.Concat(toCanvas);
                    using var paint = new SKPaint { IsAntialias = true };
                    canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
                });
            }
            case MarkupLayer { Elements.IsEmpty: false } markup:
            {
                var toCanvas = markup.Placement.ToMatrix();
                var bounds = toCanvas.MapRect(markup.Elements.Select(_markup.Bounds).Aggregate(SKRect.Union));
                return Draw(_picture.Colour, bounds, canvas =>
                {
                    canvas.Concat(toCanvas);
                    foreach (var element in markup.Elements) _markup.Paint(canvas, element);
                });
            }
            default:
                return null;
        }
    }

    /// <summary>Pixels covering <paramref name="bounds"/> on the canvas, drawn by <paramref name="draw"/> in canvas pixels, as deep
    /// as <paramref name="colour"/> holds them.</summary>
    private static (SKBitmap Pixels, SKPointI At)? Draw(PictureColour colour, SKRect bounds, Action<SKCanvas> draw)
    {
        var area = SKRectI.Ceiling(bounds);
        if (area.IsEmpty) return null;
        var bitmap = new SKBitmap(colour.SurfaceInfo(area.Width, area.Height));
        if (bitmap.GetPixels() == IntPtr.Zero)
        {
            bitmap.Dispose();
            throw new InvalidOperationException($"No memory for a {area.Width}×{area.Height} layer.");
        }
        bitmap.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Translate(-area.Left, -area.Top);
            draw(canvas);
        }
        return (bitmap, new SKPointI(area.Left, area.Top));
    }

    public SKImage Composite(double scale)
    {
        using var compositor = new LayerCompositor(_markup, cache: false);
        return compositor.Render(_picture, scale, this);
    }

    /// <summary>The whole picture composited at <paramref name="scale"/> as <paramref name="options"/> say; the caller owns it.</summary>
    public SKImage Composite(double scale, CompositeOptions options)
    {
        using var compositor = new LayerCompositor(_markup, cache: false);
        return compositor.Render(_picture, scale, this, options);
    }

    /// <summary>The whole picture composited at full size over nothing, as the pixels of one layer in its colour.</summary>
    public TiledPixels Flattened()
    {
        using var image = Composite(1, new CompositeOptions(LeaveOutBackground: true));
        using var bitmap = SKBitmap.FromImage(image);
        return TiledPixels.FromBitmap(bitmap, 0, 0, _picture.Colour.Tiles);
    }

    PictureLayerImage? IPictureLayerImages.ImageOf(PictureLayer layer, double scale)
        => _pictures.TryGetValue(layer.Id, out var image) ? new PictureLayerImage(image, layer.Placement.ToMatrix(), 0) : null;

    public void Dispose()
    {
        foreach (var image in _pictures.Values) image.Dispose();
        _pictures.Clear();
    }
}
