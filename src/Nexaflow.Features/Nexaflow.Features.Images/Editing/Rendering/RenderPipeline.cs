using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Recipe;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Rendering;

/// <summary>What to render, and how big.</summary>
/// <param name="Frame">The finished picture, or the uncropped picture the crop frame is dragged over.</param>
/// <param name="Scale">Canvas pixels per pixel of the frame's natural size (1 = the saved size).</param>
/// <param name="Adjustments">Apply tone and colour; off for a before/after look.</param>
/// <param name="Markup">Paint the annotations.</param>
public readonly record struct RenderRequest(RenderFrame Frame, double Scale, bool Adjustments = true, bool Markup = true)
{
    public static RenderRequest Saved => new(RenderFrame.Output, 1);
}

/// <summary>
/// Renders a recipe over one decoded picture, for a preview and for a save alike: the same three passes in the same
/// order, so what is shown is what is written.
/// </summary>
/// <remarks>
/// <para>The passes, each kept until what it depends on changes:</para>
/// <list type="number">
/// <item><b>geometry</b> — the decoded pixels drawn once through the whole matrix (orientation, straighten, crop,
/// turns, flips, scale), so the picture is resampled exactly once;</item>
/// <item><b>tone</b> — the adjustment filter over that, per pixel;</item>
/// <item><b>markup</b> — the annotations, through the same matrix, over a copy of the toned picture.</item>
/// </list>
/// A slider drag redoes only the tone pass, and a pen stroke only the markup pass — which is what keeps both live.
/// Not thread-safe: one render at a time, which the presenter guarantees.
/// </remarks>
public sealed class RenderPipeline : IDisposable
{
    private readonly DecodedImage _source;
    private readonly AdjustmentFilter _adjust;
    private readonly MarkupPainter _markup;
    private readonly bool _cache;

    private GeometryKey? _geometryKey;
    private SKImage? _geometryLayer;
    private RecipeAdjustments? _toneKey;
    private SKImage? _toneLayer;
    private SKImage? _output;

    /// <param name="cache">Keep the passes between renders — for a preview. A one-off save passes false and holds
    /// no more than two full-size layers at a time.</param>
    public RenderPipeline(DecodedImage source, AdjustmentFilter adjust, MarkupPainter markup, bool cache = true)
    {
        _source = source;
        _adjust = adjust;
        _markup = markup;
        _cache  = cache;
    }

    public DecodedImage Source => _source;

    /// <summary>
    /// The picture. It belongs to the pipeline and stays valid until the next render or disposal — copy it out
    /// before handing it to another thread.
    /// </summary>
    public SKImage Render(ImageRecipe recipe, RenderRequest request)
    {
        var sourceToCanvas = CropGeometry.SourceToCanvas(recipe, _source.SourceSize, request.Frame, request.Scale, out var canvas);

        var geometryKey = new GeometryKey(recipe.Geometry, recipe.Resize, request.Frame, canvas);
        if (_geometryLayer is null || _geometryKey != geometryKey)
        {
            DropTone();
            DropGeometry();
            _geometryLayer = DrawGeometry(sourceToCanvas, canvas);
            _geometryKey   = geometryKey;
        }

        var adjustments = request.Adjustments ? recipe.Adjustments : RecipeAdjustments.Identity;
        if (_toneLayer is null || _toneKey != adjustments)
        {
            DropTone();
            _toneLayer = DrawTone(_geometryLayer, adjustments);
            _toneKey   = adjustments;
            if (!_cache && !ReferenceEquals(_toneLayer, _geometryLayer)) DropGeometry();
        }

        DropOutput();
        if (!request.Markup || recipe.Markup.IsDefaultOrEmpty) return _toneLayer;

        _output = DrawMarkup(_toneLayer, recipe, sourceToCanvas);
        if (!_cache) DropTone();
        return _output;
    }

    private SKImage DrawGeometry(SKMatrix sourceToCanvas, SKSizeI canvas)
    {
        var pixelsToCanvas = _source.PixelsToSource.PostConcat(sourceToCanvas);
        using var surface = NewSurface(canvas);
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        c.SetMatrix(pixelsToCanvas);
        using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Src };
        c.DrawImage(_source.Pixels, 0, 0, SamplingFor(pixelsToCanvas), paint);
        return surface.Snapshot();
    }

    private SKImage DrawTone(SKImage geometry, RecipeAdjustments adjustments)
    {
        using var filter = _adjust.Create(adjustments);
        if (filter is null) return geometry;

        using var surface = NewSurface(new SKSizeI(geometry.Width, geometry.Height));
        surface.Canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { ColorFilter = filter, BlendMode = SKBlendMode.Src };
        surface.Canvas.DrawImage(geometry, 0, 0, paint);
        return surface.Snapshot();
    }

    private SKImage DrawMarkup(SKImage toned, ImageRecipe recipe, SKMatrix sourceToCanvas)
    {
        using var surface = NewSurface(new SKSizeI(toned.Width, toned.Height));
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        using (var copy = new SKPaint { BlendMode = SKBlendMode.Src }) c.DrawImage(toned, 0, 0, copy);
        c.SetMatrix(sourceToCanvas);
        foreach (var element in recipe.Markup) _markup.Paint(c, element);
        return surface.Snapshot();
    }

    /// <summary>
    /// Cubic resampling, except where the picture is shrunk to well under half — there mipmaps average the detail a
    /// cubic kernel would alias.
    /// </summary>
    private static SKSamplingOptions SamplingFor(SKMatrix pixelsToCanvas) =>
        CropGeometry.SourcePixelsPerCanvasPixel(pixelsToCanvas) > 2
            ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            : new SKSamplingOptions(SKCubicResampler.Mitchell);

    private SKSurface NewSurface(SKSizeI size)
    {
        var colorType = _source.IsHighBitDepth ? SKColorType.RgbaF16 : SKImageInfo.PlatformColorType;
        var info = new SKImageInfo(size.Width, size.Height, colorType, SKAlphaType.Premul, _source.ColorSpace);
        return SKSurface.Create(info) ?? throw new InvalidOperationException($"No memory for a {size.Width}×{size.Height} picture.");
    }

    private void DropGeometry()
    {
        if (!ReferenceEquals(_geometryLayer, _toneLayer)) _geometryLayer?.Dispose();
        _geometryLayer = null;
        _geometryKey   = null;
    }

    private void DropTone()
    {
        if (!ReferenceEquals(_toneLayer, _geometryLayer)) _toneLayer?.Dispose();
        _toneLayer = null;
        _toneKey   = null;
    }

    private void DropOutput()
    {
        _output?.Dispose();
        _output = null;
    }

    public void Dispose()
    {
        DropOutput();
        DropTone();
        DropGeometry();
    }

    private sealed record GeometryKey(RecipeGeometry Geometry, RecipeResize? Resize, RenderFrame Frame, SKSizeI Canvas);
}
