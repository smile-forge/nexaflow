using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Metadata;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Restoring;
using Nexaflow.IO.Common;
using SkiaSharp;
using System.IO;
using Nexaflow.Features.Images.Editing.CuttingOut;
using Nexaflow.Features.Images.Editing.Erasing;

namespace Nexaflow.Features.Images.Editing;

/// <summary>
/// The editing engine an Images registration shares across its tabs: codecs, metadata transfer, the adjustment
/// program, the markup painter, the recipe store and the exporter. Stateless apart from the store's pending writes
/// and the painter's typeface cache, so one instance serves every viewer at once.
/// </summary>
public sealed class ImageEditingServices : IDisposable
{
    public ImageEditingServices(IVirtualFileSystem vfs) : this(vfs, new ImageRecipeStore()) { }

    /// <param name="vfs">The runtime's file system the photos are read and saved through.</param>
    /// <param name="restored">Where restorations are kept; the app's cache when not given.</param>
    /// <param name="cutOuts">Where cut-out masks are kept; the app's cache when not given.</param>
    /// <param name="erased">Where erased photos are kept; the app's cache when not given.</param>
    public ImageEditingServices(IVirtualFileSystem vfs, ImageRecipeStore recipes, RestoredPictures? restored = null, CutOutMasks? cutOuts = null,
                                ErasedPictures? erased = null)
    {
        FileSystem = vfs;
        Restored   = restored ?? new RestoredPictures();
        CutOuts    = cutOuts ?? new CutOutMasks();
        Erased     = erased ?? new ErasedPictures();
        var jpeg = new JpegMetadata();
        var png  = new PngMetadata();
        var webp = new WebpMetadata();
        Metadata = new MetadataTransfer([jpeg, png, webp, new TiffMetadata()], [jpeg, png, webp]);

        Decoders    = new ImageDecoders([new SkiaImageDecoder(), new WicImageDecoder()]);
        Encoders    = new ImageEncoders([new SkiaImageEncoder(Metadata), new TiffImageEncoder(), new WicImageEncoder()]);
        Adjustments = new AdjustmentFilter();
        Markup      = new MarkupPainter();
        Recipes     = recipes;
        Exporter    = new ImageExporter(this);
    }

    /// <summary>The runtime's file system the photos are read and saved through.</summary>
    public IVirtualFileSystem FileSystem { get; }

    public MetadataTransfer Metadata { get; }
    public ImageDecoders Decoders { get; }
    public ImageEncoders Encoders { get; }
    public AdjustmentFilter Adjustments { get; }
    public MarkupPainter Markup { get; }
    public ImageRecipeStore Recipes { get; }
    public ImageExporter Exporter { get; }

    /// <summary>The restorations the picture tools made, kept on disk.</summary>
    public RestoredPictures Restored { get; }

    /// <summary>The masks photos are cut out of their backgrounds by, kept so each is picked once.</summary>
    public CutOutMasks CutOuts { get; }

    /// <summary>The photos as erased, kept so each step is done once.</summary>
    public ErasedPictures Erased { get; }

    /// <summary>True when <paramref name="path"/> is a file on this disk — one a sidecar can sit beside and a save can replace.</summary>
    [LocalDiskOnly("a photo is file-backed only as a file on this disk under its own path; a sidecar sits beside no other")]
    public static bool IsFileBacked(string path) => File.Exists(path);

    /// <summary>The picture's bytes, wherever it is: on this disk, inside an archive, at a location.</summary>
    public ValueTask<byte[]> ReadSourceAsync(string path, CancellationToken ct = default) => FileSystem.ReadAllBytesAsync(path, ct);

    /// <summary>Decodes the picture at <paramref name="path"/>, reduced to <paramref name="maxLongEdge"/> when given — off the
    /// caller's thread once its bytes are read.</summary>
    public async Task<DecodedImage> DecodeAsync(string path, int? maxLongEdge, CancellationToken ct = default)
        => Decoders.Decode(await ReadSourceAsync(path, ct).ConfigureAwait(false), maxLongEdge);

    /// <summary>The photo at its full size as it stands — its stored pixels turned as its orientation says — as interleaved
    /// 8-bit straight-alpha RGBA, with its colour space: what a picture tool is handed.</summary>
    public async Task<UprightPixels> ReadUprightAsync(string photo, CancellationToken ct = default)
    {
        using var decoded = await DecodeAsync(photo, maxLongEdge: null, ct).ConfigureAwait(false);
        int width = (int)Math.Round(decoded.SourceSize.Width), height = (int)Math.Round(decoded.SourceSize.Height);
        var space = decoded.ColorSpace;
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, space));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Concat(decoded.PixelsToSource);
            using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
            canvas.DrawImage(decoded.Pixels, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), paint);
        }
        using var unpremul = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul, space));
        using var image = SKImage.FromBitmap(bitmap);
        if (!image.ReadPixels(unpremul.Info, unpremul.GetPixels(), unpremul.RowBytes, 0, 0))
            throw new InvalidOperationException("The photo could not be read out for a picture tool.");
        return new UprightPixels(unpremul.Bytes, width, height, space);
    }

    /// <summary>A pipeline over <paramref name="source"/> — and its restoration, where the recipe restores it.</summary>
    public RenderPipeline Pipeline(DecodedImage source, bool cache = true, DecodedImage? restored = null, SKImage? cutOut = null)
        => new(source, Adjustments, Markup, cache, restored, cutOut);

    /// <summary>
    /// A picture the tools made of the photo — its restoration, or the photo as erased — kept at <paramref name="path"/>,
    /// reduced to <paramref name="maxLongEdge"/> when given, as the same picture at its size or larger: upright already,
    /// its pixels read against the photo's own <paramref name="sourceSize"/>.
    /// </summary>
    [LocalDiskOnly("a picture the tools made, kept in the app's cache")]
    public DecodedImage DecodeRestored(string path, SKSize sourceSize, int? maxLongEdge)
    {
        var decoded = Decoders.Decode(File.ReadAllBytes(path), maxLongEdge);
        return new DecodedImage(decoded.Pixels, SKEncodedOrigin.TopLeft,
                                new SKSizeI((int)Math.Round(sourceSize.Width), (int)Math.Round(sourceSize.Height)),
                                decoded.Container, isAnimated: false, isHighBitDepth: false);
    }

    public void Dispose()
    {
        Recipes.Dispose();
        Markup.Dispose();
        Adjustments.Dispose();
    }
}

/// <summary>A photo as it stands, at full size: interleaved 8-bit straight-alpha RGBA, row by row, and its colour space.</summary>
public sealed record UprightPixels(byte[] Rgba, int Width, int Height, SKColorSpace Space);
