using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Metadata;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.IO.Common;
using System.IO;

namespace Nexaflow.Features.Images.Editing;

/// <summary>
/// The editing engine an Images registration shares across its tabs: codecs, metadata transfer, the adjustment
/// program, the markup painter, the recipe store and the exporter. Stateless apart from the store's pending writes
/// and the painter's typeface cache, so one instance serves every viewer at once.
/// </summary>
public sealed class ImageEditingServices : IDisposable
{
    public ImageEditingServices() : this(new ImageRecipeStore()) { }

    public ImageEditingServices(ImageRecipeStore recipes)
    {
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

    public MetadataTransfer Metadata { get; }
    public ImageDecoders Decoders { get; }
    public ImageEncoders Encoders { get; }
    public AdjustmentFilter Adjustments { get; }
    public MarkupPainter Markup { get; }
    public ImageRecipeStore Recipes { get; }
    public ImageExporter Exporter { get; }

    /// <summary>True when <paramref name="path"/> is a file on disk — one a sidecar can sit beside and a save can replace.</summary>
    public static bool IsFileBacked(string path) => File.Exists(path);

    /// <summary>The picture's bytes, from disk or from inside an archive.</summary>
    public static byte[] ReadSource(string path)
    {
        if (File.Exists(path)) return File.ReadAllBytes(path);
        using var source = VirtualFileSystem.Instance.OpenRead(path);
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Decodes the picture at <paramref name="path"/>, reduced to <paramref name="maxLongEdge"/> when given.</summary>
    public DecodedImage Decode(string path, int? maxLongEdge) => Decoders.Decode(ReadSource(path), maxLongEdge);

    /// <summary>A preview pipeline over <paramref name="source"/>.</summary>
    public RenderPipeline Pipeline(DecodedImage source, bool cache = true) => new(source, Adjustments, Markup, cache);

    public void Dispose()
    {
        Recipes.Dispose();
        Markup.Dispose();
        Adjustments.Dispose();
    }
}
