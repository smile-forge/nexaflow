using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Selection;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Layers;

/// <summary>A picture layer as rendered for a composite: its picture, the matrix from its pixels to canvas pixels, and what
/// changes whenever it is rendered differently.</summary>
public sealed record PictureLayerImage(SKImage Image, SKMatrix ToCanvas, long Version);

/// <summary>Renders picture layers for a composite — through each one's own render pipeline, so its passes stay cached.</summary>
public interface IPictureLayerImages
{
    /// <summary>The layer rendered for <paramref name="scale"/> output pixels per canvas pixel — valid until the composite is
    /// done — or null while it has no pixels yet, when it is left out.</summary>
    PictureLayerImage? ImageOf(PictureLayer layer, double scale);
}

/// <summary>How a composite is drawn; the default draws the layers over the background.</summary>
/// <param name="LeaveOutBackground">Draw the layers over nothing, without the canvas's background.</param>
/// <param name="MaskAlone">Instead of the layers, show this layer's mask alone, in greys.</param>
public readonly record struct CompositeOptions(bool LeaveOutBackground = false, Guid? MaskAlone = null);

/// <summary>
/// Stacks a layered picture's layers into one picture, for the display at the zoom's scale and for a save at full size:
/// each layer with its opacity and blend, groups isolated on a surface of their own or passed through onto what lies beneath
/// them, masks and clipping to the layers beneath — the same drawing either way, so what is shown is what is written.
/// </summary>
/// <remarks>
/// <para>A picture layer is rendered through its own render pipeline as a photo is, so an Adjust drag redraws its tone pass
/// and the composite. Pixels are drawn from their tiles, each tile made a Skia image once; annotations are drawn as vectors.</para>
/// <para>The display's composite is kept between renders: where only a pixel layer's tiles changed — a fill, a delete — just
/// the rectangle they cover is drawn again. Every composite is drawn in horizontal bands across the cores, each band its own
/// rectangle.</para>
/// <para>A masked layer is drawn onto a surface of its own, kept only where its mask covers, then composited with its opacity
/// and blend. A layer that clips to those beneath is kept only where they are, its blend governing the colour. A
/// pass-through group with a mask, or clipping, is drawn as an isolated one: a mask needs a surface to apply to.</para>
/// Not thread-safe: one render at a time.
/// </remarks>
public sealed class LayerCompositor(MarkupPainter markup, bool cache = true) : IDisposable
{
    /// <summary>Bands thinner than this cost more to set up than they save.</summary>
    private const int MinBandRows = 64;

    private SKBitmap? _composite;
    private LayeredPicture? _drawn;
    private double _drawnScale;
    private CompositeOptions _drawnOptions;
    private Dictionary<Guid, long> _drawnVersions = [];

    /// <summary>
    /// The picture composited at <paramref name="scale"/> output pixels per canvas pixel. Kept between renders — valid until the
    /// next render or disposal, so copy it before handing it to another thread — unless the compositor keeps nothing, when the
    /// caller owns it.
    /// </summary>
    public SKImage Render(LayeredPicture picture, double scale, IPictureLayerImages pictures, CompositeOptions options = default)
    {
        var size = new SKSizeI(Math.Max(1, (int)Math.Round(picture.Width * scale)), Math.Max(1, (int)Math.Round(picture.Height * scale)));
        var images = new Dictionary<Guid, PictureLayerImage>();
        foreach (var layer in picture.All)
            if (layer is PictureLayer { Visible: true } placed && pictures.ImageOf(placed, scale) is { } image) images[layer.Id] = image;
        var versions = images.ToDictionary(p => p.Key, p => p.Value.Version);

        if (!cache)
        {
            var once = NewBitmap(picture.Colour, size);
            Draw(once, picture, scale, images, options, new SKRectI(0, 0, size.Width, size.Height));
            return ImageDecoders.Seal(once);
        }

        SKRectI? dirty = _composite is { } kept && kept.Width == size.Width && kept.Height == size.Height && _drawn is { } before
                         && _drawnScale == scale && _drawnOptions == options && SameVersions(versions)
            ? DirtyBetween(before, picture, scale, size)
            : null;
        if (dirty is null)
        {
            _composite?.Dispose();
            _composite = NewBitmap(picture.Colour, size);
            dirty = new SKRectI(0, 0, size.Width, size.Height);
        }
        if (!dirty.Value.IsEmpty) Draw(_composite!, picture, scale, images, options, dirty.Value);
        (_drawn, _drawnScale, _drawnOptions, _drawnVersions) = (picture, scale, options, versions);
        return SKImage.FromPixels(_composite!.PeekPixels());
    }

    private bool SameVersions(Dictionary<Guid, long> versions)
        => versions.Count == _drawnVersions.Count && versions.All(v => _drawnVersions.TryGetValue(v.Key, out var was) && was == v.Value);

    /// <summary>
    /// The rectangle of the output that differs between <paramref name="before"/> and <paramref name="after"/>: empty where the
    /// layers are the same, the tiles a pixel layer changed where that is all that changed, else null — draw it all again.
    /// </summary>
    public static SKRectI? DirtyBetween(LayeredPicture before, LayeredPicture after, double scale, SKSizeI size)
    {
        if (before.Width != after.Width || before.Height != after.Height || before.Background != after.Background || before.Colour != after.Colour) return null;
        if (before.Layers == after.Layers) return SKRectI.Empty;
        var dirty = SKRectI.Empty;
        return Compare(before.Layers, after.Layers, ref dirty) ? Scaled(dirty, scale, size) : null;
    }

    private static bool Compare(System.Collections.Immutable.ImmutableArray<Layer> before, System.Collections.Immutable.ImmutableArray<Layer> after, ref SKRectI dirty)
    {
        if (before.Length != after.Length) return false;
        for (int i = 0; i < before.Length; i++)
        {
            Layer a = before[i], b = after[i];
            if (ReferenceEquals(a, b)) continue;
            if (a.Id != b.Id) return false;
            switch (a, b)
            {
                case (PixelLayer pa, PixelLayer pb) when (pa with { Pixels = pb.Pixels }) == pb:
                    foreach (var rect in pa.Pixels.ChangedTileRects(pb.Pixels)) dirty = Union(dirty, rect);
                    break;
                case (GroupLayer ga, GroupLayer gb) when (ga with { Children = gb.Children }) == gb:
                    if (!Compare(ga.Children, gb.Children, ref dirty)) return false;
                    break;
                default:
                    return false;
            }
        }
        return true;
    }

    private static SKRectI Union(SKRectI a, SKRectI b) => a.IsEmpty ? b : b.IsEmpty ? a : SKRectI.Union(a, b);

    /// <summary>A rectangle of canvas pixels as the output pixels it touches, a pixel more round for the resampling.</summary>
    private static SKRectI Scaled(SKRectI canvas, double scale, SKSizeI size)
    {
        if (canvas.IsEmpty) return SKRectI.Empty;
        var rect = new SKRectI((int)Math.Floor(canvas.Left * scale) - 1, (int)Math.Floor(canvas.Top * scale) - 1,
                               (int)Math.Ceiling(canvas.Right * scale) + 1, (int)Math.Ceiling(canvas.Bottom * scale) + 1);
        return SKRectI.Intersect(rect, new SKRectI(0, 0, size.Width, size.Height));
    }

    // ── Drawing ─────────────────────────────────────────────────────────────

    /// <summary>The rectangle <paramref name="area"/> of <paramref name="into"/> drawn again, in bands across the cores.</summary>
    private void Draw(SKBitmap into, LayeredPicture picture, double scale, IReadOnlyDictionary<Guid, PictureLayerImage> images,
                      CompositeOptions options, SKRectI area)
    {
        int bands = Math.Clamp(area.Height / MinBandRows, 1, Environment.ProcessorCount);
        int rows = (area.Height + bands - 1) / bands;
        var origin = into.GetPixels();
        int rowBytes = into.RowBytes;
        Parallel.For(0, bands, band =>
        {
            int top = area.Top + band * rows, height = Math.Min(rows, area.Bottom - top);
            if (height <= 0) return;
            var rect = new SKRectI(area.Left, top, area.Right, top + height);
            using var slice = new SKBitmap();
            slice.InstallPixels(into.Info.WithSize(rect.Width, rect.Height), origin + top * rowBytes + rect.Left * into.BytesPerPixel, rowBytes);
            DrawBand(slice, picture, scale, images, options, rect);
        });
    }

    /// <summary>One band: the background, then every layer bottom first — or the mask asked for alone, white over black.</summary>
    private void DrawBand(SKBitmap slice, LayeredPicture picture, double scale, IReadOnlyDictionary<Guid, PictureLayerImage> images,
                          CompositeOptions options, SKRectI rect)
    {
        using var canvas = new SKCanvas(slice);
        var target = new Target(canvas, () =>
        {
            canvas.Flush();
            return SKImage.FromPixelCopy(slice.Info, slice.GetPixels(), slice.RowBytes)
                   ?? throw new InvalidOperationException("What lies beneath a layer could not be read.");
        });
        var band = new Band(rect, scale, picture, images);
        if (options.MaskAlone is { } id && picture.Find(id)?.Mask is { } mask)
        {
            canvas.Clear(SKColors.Black);
            using var shown = NewSurface(picture.Colour, rect);
            shown.Canvas.Clear(SKColors.White);
            band.ApplyCoverage(shown.Canvas, mask.Coverage, SKBlendMode.DstIn);
            using var white = shown.Snapshot();
            canvas.DrawImage(white, 0, 0);
            return;
        }
        canvas.Clear(options.LeaveOutBackground ? SKColors.Transparent : picture.Background.Colour);
        foreach (var layer in picture.Layers) Composite(target, layer, band, 1);
    }

    /// <summary>A surface being drawn onto, and how to read what it holds so far.</summary>
    private sealed record Target(SKCanvas Canvas, Func<SKImage> Snapshot);

    /// <summary>
    /// <paramref name="layer"/> composited onto <paramref name="target"/>, a surface over the band's rectangle: its opacity
    /// times <paramref name="carried"/> — a pass-through group's — its blend, its mask, and its clip to what the target holds.
    /// </summary>
    private void Composite(Target target, Layer layer, Band band, double carried)
    {
        if (!layer.Visible || layer is UndrawableLayer) return;
        double opacity = Math.Clamp(layer.Opacity * carried, 0, 1);
        if (opacity <= 0) return;
        var mask = layer.Mask is { Enabled: true } m ? m : null;

        if (layer is GroupLayer { Isolated: false } passing && mask is null && !layer.ClipsToBelow)
        {
            foreach (var child in passing.Children) Composite(target, child, band, opacity);
            return;
        }

        using var paint = new SKPaint { BlendMode = layer.Blend.ToSkia(), ColorF = new SKColorF(0, 0, 0, (float)opacity) };
        bool ownSurface = mask is not null || layer.ClipsToBelow || layer is GroupLayer;
        if (!ownSurface)
        {
            DrawContent(target.Canvas, layer, band, paint);
            return;
        }

        using var surface = NewSurface(band.Picture.Colour, band.Rect);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var inner = new Target(canvas, surface.Snapshot);
        if (layer is GroupLayer group)
            foreach (var child in group.Children) Composite(inner, child, band, 1);
        else
            DrawContent(canvas, layer, band, null);
        if (mask is not null) band.ApplyCoverage(canvas, mask.Coverage, SKBlendMode.DstIn);
        if (layer.ClipsToBelow)
        {
            // Kept only where what lies beneath it in its group is: the target as it stands, as alpha.
            using var beneath = target.Snapshot();
            using var keep = new SKPaint { BlendMode = SKBlendMode.DstIn };
            canvas.ResetMatrix();
            canvas.DrawImage(beneath, 0, 0, keep);
        }
        using var drawn = surface.Snapshot();
        target.Canvas.ResetMatrix();
        target.Canvas.DrawImage(drawn, 0, 0, paint);
    }

    /// <summary>What a pixel, markup or picture layer shows, drawn onto <paramref name="canvas"/> in canvas pixels.</summary>
    private void DrawContent(SKCanvas canvas, Layer layer, Band band, SKPaint? paint)
    {
        canvas.Save();
        canvas.SetMatrix(band.CanvasToBand);
        var clip = band.CanvasClip;
        switch (layer)
        {
            case PixelLayer pixels:
                pixels.Pixels.DrawTo(canvas, clip, band.Sampling, paint);
                break;
            case MarkupLayer marks when !marks.Elements.IsEmpty:
                if (paint is not null) canvas.SaveLayer(paint);
                canvas.Concat(marks.Placement.ToMatrix());
                foreach (var element in marks.Elements) markup.Paint(canvas, element);
                if (paint is not null) canvas.Restore();
                break;
            case PictureLayer when band.Images.TryGetValue(layer.Id, out var image):
                canvas.Concat(image.ToCanvas);
                using (var draw = paint is null ? new SKPaint() : paint.Clone())
                {
                    draw.IsAntialias = true;
                    canvas.DrawImage(image.Image, 0, 0, SamplingFor(image.ToCanvas.PostConcat(band.CanvasToBand)), draw);
                }
                break;
        }
        canvas.Restore();
    }

    private static SKSamplingOptions SamplingFor(SKMatrix toOutput) =>
        CropGeometry.SourcePixelsPerCanvasPixel(toOutput) > 2
            ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            : new SKSamplingOptions(SKCubicResampler.Mitchell);

    /// <summary>A surface over <paramref name="rect"/> as deep as <paramref name="colour"/>, in its space.</summary>
    private static SKSurface NewSurface(PictureColour colour, SKRectI rect)
        => SKSurface.Create(colour.SurfaceInfo(rect.Width, rect.Height))
           ?? throw new InvalidOperationException($"No memory for a {rect.Width}×{rect.Height} layer.");

    private static SKBitmap NewBitmap(PictureColour colour, SKSizeI size)
    {
        var bitmap = new SKBitmap(colour.SurfaceInfo(size.Width, size.Height));
        if (bitmap.GetPixels() == IntPtr.Zero) throw new InvalidOperationException($"No memory for a {size.Width}×{size.Height} picture.");
        bitmap.Erase(SKColors.Transparent);
        return bitmap;
    }

    public void Dispose()
    {
        _composite?.Dispose();
        _composite = null;
        _drawn = null;
    }

    /// <summary>One band being drawn: its rectangle of output pixels, the scale, and the layers' pictures.</summary>
    private sealed class Band(SKRectI rect, double scale, LayeredPicture picture, IReadOnlyDictionary<Guid, PictureLayerImage> images)
    {
        public SKRectI Rect { get; } = rect;
        public LayeredPicture Picture { get; } = picture;
        public IReadOnlyDictionary<Guid, PictureLayerImage> Images { get; } = images;

        /// <summary>Canvas pixels to the band's own pixels.</summary>
        public SKMatrix CanvasToBand { get; } = SKMatrix.CreateScale((float)scale, (float)scale).PostConcat(SKMatrix.CreateTranslation(-rect.Left, -rect.Top));

        /// <summary>The band, in canvas pixels.</summary>
        public SKRect CanvasClip { get; } = new((float)(rect.Left / scale), (float)(rect.Top / scale), (float)(rect.Right / scale), (float)(rect.Bottom / scale));

        public SKSamplingOptions Sampling { get; } = scale < 0.5
            ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            : scale > 1 ? new SKSamplingOptions(SKFilterMode.Nearest) : new SKSamplingOptions(SKFilterMode.Linear);

        /// <summary>Coverage over the canvas drawn onto <paramref name="canvas"/> as alpha with <paramref name="blend"/> — only what
        /// lies under the band read from it; past the canvas, uncovered.</summary>
        public void ApplyCoverage(SKCanvas canvas, TiledCoverage coverage, SKBlendMode blend)
        {
            var area = SKRectI.Intersect(SKRectI.Round(CanvasClip with
            {
                Left = MathF.Floor(CanvasClip.Left) - 1, Top = MathF.Floor(CanvasClip.Top) - 1,
                Right = MathF.Ceiling(CanvasClip.Right) + 1, Bottom = MathF.Ceiling(CanvasClip.Bottom) + 1,
            }), new SKRectI(0, 0, picture.Width, picture.Height));
            canvas.Save();
            canvas.ResetMatrix();
            if (area.IsEmpty)
            {
                using var none = new SKPaint { BlendMode = blend, Color = SKColors.Transparent };
                canvas.DrawPaint(none);
                canvas.Restore();
                return;
            }
            using var alpha = CoverageImage(coverage, area);
            // Past the coverage's area — past the canvas — nothing is covered.
            using (var outside = new SKPaint { BlendMode = blend, Color = SKColors.Transparent })
            {
                canvas.Save();
                canvas.SetMatrix(CanvasToBand);
                canvas.ClipRect(SKRect.Create(area.Left, area.Top, area.Width, area.Height), SKClipOperation.Difference);
                canvas.DrawPaint(outside);
                canvas.Restore();
            }
            canvas.SetMatrix(CanvasToBand);
            using var paint = new SKPaint { BlendMode = blend };
            canvas.DrawImage(alpha, area.Left, area.Top, new SKSamplingOptions(SKFilterMode.Linear), paint);
            canvas.Restore();
        }

        /// <summary>The coverage of <paramref name="area"/> as an image whose alpha it is, as deep as it is held: a byte, 16 bits, or
        /// the alpha of a float picture — Skia's half-float alpha holds fewer values than a 32-bit mask does.</summary>
        private static SKImage CoverageImage(TiledCoverage coverage, SKRectI area)
        {
            SKImage? image;
            switch (coverage.Depth)
            {
                case ChannelDepth.U8:
                    image = SKImage.FromPixelCopy(new SKImageInfo(area.Width, area.Height, SKColorType.Alpha8, SKAlphaType.Premul), coverage.Read(area));
                    break;
                case ChannelDepth.U16:
                {
                    var values = coverage.ReadValues(area);
                    var samples = new ushort[values.Length];
                    for (int i = 0; i < values.Length; i++) samples[i] = (ushort)Math.Clamp((int)MathF.Round(values[i] * 65535), 0, 65535);
                    image = SKImage.FromPixelCopy(new SKImageInfo(area.Width, area.Height, SKColorType.Alpha16, SKAlphaType.Premul),
                                                  System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples.AsSpan()));
                    break;
                }
                default:
                {
                    var values = coverage.ReadValues(area);
                    var premul = new float[values.Length * 4];
                    for (int i = 0; i < values.Length; i++) premul[i * 4 + 3] = values[i];
                    image = SKImage.FromPixelCopy(new SKImageInfo(area.Width, area.Height, SKColorType.RgbaF32, SKAlphaType.Premul),
                                                  System.Runtime.InteropServices.MemoryMarshal.AsBytes(premul.AsSpan()));
                    break;
                }
            }
            return image ?? throw new InvalidOperationException("A mask could not be made an image.");
        }
    }
}
