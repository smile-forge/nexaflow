using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Recipe;
using SkiaSharp;
using Nexaflow.Features.Images.Editing.SceneDepth;

namespace Nexaflow.Features.Images.Editing.Rendering;

/// <summary>What to render, and how big.</summary>
/// <param name="DepthDetail">The share of the frame's pixels the scene depth pass is drawn at, along a side: <see cref="PreviewDepthDetail"/>
/// while a slider or a drag is moving, so lens blur and haze keep up with it; 1 for every frame that stays.</param>
public readonly record struct RenderRequest(RenderFrame Frame, double Scale, bool Adjustments = true, bool Markup = true, double DepthDetail = 1)
{
    public static RenderRequest Saved => new(RenderFrame.Output, 1);

/// <summary>The scene depth pass's detail while something moves: half along each side, so a sixteenth of the blur's work.</summary>
    public const double PreviewDepthDetail = 0.5;

/// <summary>The detail asked for, read as 1 where it says nothing usable.</summary>
    public double DepthDetailAsked => DepthDetail is > 0 and < 1 ? DepthDetail : 1;
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
/// <item><b>scene depth</b> — where the recipe asks for them, the haze and lens blur drawn from the picture's scene depth
/// over that (<see cref="SceneDepthPass"/>), before tone as air and a lens come before the camera's processing;</item>
/// <item><b>tone</b> — the adjustment filter over that, per pixel;</item>
/// <item><b>markup</b> — the annotations, through the same matrix, over a copy of the toned picture.</item>
/// </list>
/// A slider drag redoes only the tone pass, a focus moved or an aperture dragged only the scene depth pass and the tone pass
/// after it, and a pen stroke only the markup pass — which is what keeps each live.
/// Not thread-safe: one render at a time, which the presenter guarantees.
/// </remarks>
public sealed class RenderPipeline : IDisposable
{
    private readonly DecodedImage _source;
    private readonly DecodedImage? _restored;
    private readonly SKImage? _cutOut;
    private readonly SceneDepthPlane? _sceneDepth;

    private readonly SKImage? _subject;
    private (float R, float G, float B)? _distance;
    private readonly AdjustmentFilter _adjust;
    private readonly MarkupPainter _markup;
    private readonly bool _cache;

    private GeometryKey? _geometryKey;
    private SKImage? _geometryLayer;
    private SceneDepthKey? _depthKey;
    private double _depthDetail = 1;
    private SKImage? _depthLayer;
    private RecipeAdjustments? _toneKey;
    private SKImage? _toneLayer;
    private SKImage? _output;

    /// <param name="cache">Keep the passes between renders — for a preview. A one-off save passes false and holds
    /// no more than two full-size layers at a time.</param>
    /// <param name="restored">The picture as the picture tools restored it, for a recipe that restores it: the same picture,
    /// upright and larger, laid over <paramref name="source"/> at the restoration's strength. Without it, a recipe that
    /// restores the picture shows it unrestored, at the restored size.</param>
    /// <param name="cutOut">The mask the recipe's cut-out keeps the picture by — alpha over the upright source, 255 kept and
    /// 0 cleared — for a recipe that cuts it out. Without it, such a recipe shows the whole picture. The caller owns it.</param>
    /// <param name="sceneDepth">The upright source's scene depth, for a recipe that draws from it — a lens blur, a haze. Without
    /// it, such a recipe shows the picture sharp and clear.</param>
    /// <param name="subject">The outline of what the recipe's lens blur is focused on — alpha over the upright source, as a
    /// cut-out's mask is — for a lens blur with a subject: the depth is set right at its edge. Without it, the blur is drawn from
    /// the depth as it is. The caller owns it.</param>
    public RenderPipeline(DecodedImage source, AdjustmentFilter adjust, MarkupPainter markup, bool cache = true, DecodedImage? restored = null,
                          SKImage? cutOut = null, SceneDepthPlane? sceneDepth = null, SKImage? subject = null)
    {
        _source     = source;
        _restored   = restored;
        _cutOut     = cutOut;
        _sceneDepth = sceneDepth;
        _subject    = subject;
        _adjust   = adjust;
        _markup   = markup;
        _cache    = cache;
    }

    public DecodedImage Source => _source;

    /// <summary>
    /// The picture. It belongs to the pipeline and stays valid until the next render or disposal — copy it out
    /// before handing it to another thread.
    /// </summary>
    public SKImage Render(ImageRecipe recipe, RenderRequest request)
    {
        var sourceToCanvas = CropGeometry.SourceToCanvas(recipe, _source.SourceSize, request.Frame, request.Scale, out var canvas);

        var geometryKey = new GeometryKey(recipe.Geometry, recipe.Resize, recipe.Restore, recipe.CutOut, request.Frame, canvas);
        if (_geometryLayer is null || _geometryKey != geometryKey)
        {
            DropTone();
            DropDepth();
            DropGeometry();
            _geometryLayer = DrawGeometry(sourceToCanvas, canvas, recipe.Restore, recipe.CutOut);
            _geometryKey   = geometryKey;
        }

        // The scene depth pass, where the recipe asks for it and the depth is here; otherwise tone works on the geometry pass.
        // Drawn at the detail asked for when it has to be drawn again, and kept at any detail as fine — so a pass drawn whole
        // stays while something else moves, and one drawn to keep up with a slider is drawn whole once it stops.
        var depthKey = _sceneDepth is not null && recipe.AsksSceneDepth ? new SceneDepthKey(recipe.LensBlur, recipe.DepthHaze) : null;
        double detail = request.DepthDetailAsked;
        if (depthKey is null && _depthLayer is not null)
        {
            DropTone();
            DropDepth();
        }
        else if (depthKey is not null && (_depthLayer is null || _depthKey != depthKey || _depthDetail < detail))
        {
            DropTone();
            DropDepth();
            _depthLayer  = DrawDepth(_geometryLayer!, recipe, sourceToCanvas, detail);
            _depthKey    = depthKey;
            _depthDetail = detail;
            if (!_cache) DropGeometry();
        }
        var beneathTone = _depthLayer ?? _geometryLayer!;

        var adjustments = request.Adjustments ? recipe.Adjustments : RecipeAdjustments.Identity;
        if (_toneLayer is null || _toneKey != adjustments)
        {
            DropTone();
            _toneLayer = DrawTone(beneathTone, adjustments);
            _toneKey   = adjustments;
            if (!_cache && !ReferenceEquals(_toneLayer, beneathTone))
            {
                DropDepth();
                DropGeometry();
            }
        }

        DropOutput();
        if (!request.Markup || recipe.Markup.IsDefaultOrEmpty) return _toneLayer;

        _output = DrawMarkup(_toneLayer, recipe, sourceToCanvas);
        if (!_cache) DropTone();
        return _output;
    }

    /// <summary>
    /// The picture drawn through the whole matrix — and, where the recipe restores it and the restored picture is here, the
    /// restored picture through its own matrix over it, at the restoration's strength: all restored, the picture beneath is
    /// not drawn at all. Where the recipe cuts it out and its mask is here, what the mask does not keep is cleared.
    /// </summary>
    private SKImage DrawGeometry(SKMatrix sourceToCanvas, SKSizeI canvas, RecipeRestore? restore, RecipeCutOut? cutOut)
    {
        var over = restore is not null ? _restored : null;
        var mask = cutOut is not null ? _cutOut : null;
        var pixelsToCanvas = _source.PixelsToSource.PostConcat(sourceToCanvas);
        var restoredToCanvas = over?.PixelsToSource.PostConcat(sourceToCanvas);
        bool beneath = over is null || restore!.Strength < 1;
        return DrawBanded(canvas, c =>
        {
            if (beneath) Draw(c, _source.Pixels, pixelsToCanvas, SKBlendMode.Src, 255);
            if (over is not null)
                Draw(c, over.Pixels, restoredToCanvas!.Value, beneath ? SKBlendMode.SrcOver : SKBlendMode.Src, (byte)Math.Round(Math.Clamp(restore!.Strength, 0, 1) * 255));
            if (mask is not null)
                Draw(c, mask, sourceToCanvas, SKBlendMode.DstIn, 255);
        });
    }

    private static void Draw(SKCanvas canvas, SKImage pixels, SKMatrix toCanvas, SKBlendMode blend, byte alpha)
    {
        canvas.Save();
        canvas.Concat(toCanvas);
        using var paint = new SKPaint { IsAntialias = true, BlendMode = blend, Color = SKColors.Black.WithAlpha(alpha) };
        canvas.DrawImage(pixels, 0, 0, SamplingFor(toCanvas), paint);
        canvas.Restore();
    }

    /// <summary>The recipe's haze and lens blur over the geometry pass's picture, from the source's scene depth; the colour of the
    /// source's distance, for a haze with none of its own, read once.</summary>
    private SKImage DrawDepth(SKImage geometry, ImageRecipe recipe, SKMatrix sourceToCanvas, double detail)
    {
        var photo = new SKSizeI((int)Math.Round(_source.SourceSize.Width), (int)Math.Round(_source.SourceSize.Height));
        if (recipe.DepthHaze is { Colour: null }) _distance ??= SceneDepthPass.FarColour(_source, _sceneDepth!);
        var distance = _distance ?? (0.5f, 0.5f, 0.5f);
        var subject = recipe.LensBlur?.Subject is not null ? _subject : null;
        var size = new SKSizeI(geometry.Width, geometry.Height);
        if (detail >= 1) return SceneDepthPass.Draw(geometry, recipe, _sceneDepth!, sourceToCanvas, photo, distance, NewInfo(size), subject);

        // Fewer pixels, the frame's own drawn back over it: the pass where the canvas lies scaled to them.
        var reduced = new SKSizeI(Math.Max(1, (int)Math.Round(size.Width * detail)), Math.Max(1, (int)Math.Round(size.Height * detail)));
        var toReduced = SKMatrix.CreateScale(reduced.Width / (float)size.Width, reduced.Height / (float)size.Height);
        using var smaller = Resampled(geometry, reduced);
        using var drawn = SceneDepthPass.Draw(smaller, recipe, _sceneDepth!, sourceToCanvas.PostConcat(toReduced), photo, distance, NewInfo(reduced), subject);
        return Resampled(drawn, size);
    }

    // image drawn to size, smoothly either way.
    private SKImage Resampled(SKImage image, SKSizeI size)
    {
        using var surface = NewSurface(size);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        surface.Canvas.DrawImage(image, SKRect.Create(size.Width, size.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
        return surface.Snapshot();
    }

    private SKImage DrawTone(SKImage geometry, RecipeAdjustments adjustments)
    {
        using var filter = _adjust.Create(adjustments);
        if (filter is null) return geometry;

        return DrawBanded(new SKSizeI(geometry.Width, geometry.Height), c =>
        {
            using var paint = new SKPaint { ColorFilter = filter, BlendMode = SKBlendMode.Src };
            c.DrawImage(geometry, 0, 0, SKSamplingOptions.Default, paint);
        });
    }

    private SKImage DrawMarkup(SKImage toned, ImageRecipe recipe, SKMatrix sourceToCanvas)
    {
        using var surface = NewSurface(new SKSizeI(toned.Width, toned.Height));
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        using (var copy = new SKPaint { BlendMode = SKBlendMode.Src }) c.DrawImage(toned, 0, 0, SKSamplingOptions.Default, copy);
        c.SetMatrix(sourceToCanvas);
        foreach (var element in recipe.Markup) _markup.Paint(c, element);
        return surface.Snapshot();
    }

    /// <summary>
    /// One pass drawn across the cores: the canvas is cut into horizontal bands, each drawn by <paramref name="draw"/>
    /// on a canvas of its own over its own rows of one shared buffer — so the bands never touch, and every pixel is
    /// computed exactly as a single canvas would compute it. The resampling and the adjustment program are per-pixel
    /// work Skia runs on one core, so this is what keeps a slider live on a large preview and a save short.
    /// </summary>
    private SKImage DrawBanded(SKSizeI size, Action<SKCanvas> draw)
    {
        var bitmap = new SKBitmap(NewInfo(size));
        if (bitmap.GetPixels() == IntPtr.Zero) throw new InvalidOperationException($"No memory for a {size.Width}×{size.Height} picture.");

        int bands = Math.Clamp(size.Height / MinBandRows, 1, Environment.ProcessorCount);
        int rows = (size.Height + bands - 1) / bands;
        var origin = bitmap.GetPixels();
        int rowBytes = bitmap.RowBytes;

        Parallel.For(0, bands, band =>
        {
            int top = band * rows, height = Math.Min(rows, size.Height - top);
            if (height <= 0) return;
            using var slice = new SKBitmap();
            slice.InstallPixels(bitmap.Info.WithSize(size.Width, height), origin + top * rowBytes, rowBytes);
            using var canvas = new SKCanvas(slice);
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(0, -top);
            draw(canvas);
        });

        return ImageDecoders.Seal(bitmap);
    }

    /// <summary>Bands thinner than this cost more to set up than they save.</summary>
    private const int MinBandRows = 64;

    /// <summary>
    /// Cubic resampling, except where the picture is shrunk to well under half — there mipmaps average the detail a
    /// cubic kernel would alias.
    /// </summary>
    private static SKSamplingOptions SamplingFor(SKMatrix pixelsToCanvas) =>
        CropGeometry.SourcePixelsPerCanvasPixel(pixelsToCanvas) > 2
            ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            : new SKSamplingOptions(SKCubicResampler.Mitchell);

    private SKSurface NewSurface(SKSizeI size) =>
        SKSurface.Create(NewInfo(size)) ?? throw new InvalidOperationException($"No memory for a {size.Width}×{size.Height} picture.");

    /// <summary>The working format: 8-bit premultiplied for an 8-bit source, half-float for a deeper one, in its colour space.</summary>
    private SKImageInfo NewInfo(SKSizeI size) => new(size.Width, size.Height,
        _source.IsHighBitDepth ? SKColorType.RgbaF16 : SKImageInfo.PlatformColorType, SKAlphaType.Premul, _source.ColorSpace);

    private void DropGeometry()
    {
        if (!ReferenceEquals(_geometryLayer, _toneLayer) && !ReferenceEquals(_geometryLayer, _depthLayer)) _geometryLayer?.Dispose();
        _geometryLayer = null;
        _geometryKey   = null;
    }

    private void DropTone()
    {
        if (!ReferenceEquals(_toneLayer, _geometryLayer) && !ReferenceEquals(_toneLayer, _depthLayer)) _toneLayer?.Dispose();
        _toneLayer = null;
        _toneKey   = null;
    }

    private void DropDepth()
    {
        if (!ReferenceEquals(_depthLayer, _toneLayer)) _depthLayer?.Dispose();
        _depthLayer = null;
        _depthKey   = null;
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
        DropDepth();
        DropGeometry();
    }

    private sealed record GeometryKey(RecipeGeometry Geometry, RecipeResize? Resize, RecipeRestore? Restore, RecipeCutOut? CutOut, RenderFrame Frame, SKSizeI Canvas);

    private sealed record SceneDepthKey(RecipeLensBlur? LensBlur, RecipeDepthHaze? Haze);
}
