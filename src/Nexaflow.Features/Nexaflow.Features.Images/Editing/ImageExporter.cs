using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.CuttingOut;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.Features.Images.Editing.Metadata;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using System.IO;

namespace Nexaflow.Features.Images.Editing;

/// <summary>What saving a picture can and cannot do, worked out before the user is asked.</summary>
/// <param name="CanSaveInPlace">The original can be overwritten: its format can be written and it is one still frame.</param>
/// <param name="KeepsMetadata">The target format can carry EXIF and XMP.</param>
/// <param name="KeepsTransparency">The target format can hold transparency — what a removed background leaves.</param>
public readonly record struct SaveCapability(bool CanSaveInPlace, bool KeepsMetadata, bool KeepsTransparency);

/// <summary>
/// Renders a recipe at full resolution and encodes it: the bytes a save writes. The full-size picture is decoded,
/// rendered through the same pipeline as the preview, and encoded with the source's metadata — patched to describe
/// the saved picture, or stripped to its colour profile.
/// </summary>
public sealed class ImageExporter(ImageEditingServices services)
{
    /// <summary>The formats a copy can be saved as, in the order they are offered.</summary>
    public static IReadOnlyList<ImageContainer> CopyFormats { get; } =
        [ImageContainer.Jpeg, ImageContainer.Png, ImageContainer.Webp, ImageContainer.Tiff, ImageContainer.Bmp, ImageContainer.Gif];

    public SaveCapability Capability(ImageContainer source, bool isAnimated, ImageContainer target) => new(
        CanSaveInPlace: services.Encoders.CanEncode(source) && !isAnimated,
        KeepsMetadata: services.Encoders.CarriesMetadata(target),
        KeepsTransparency: services.Encoders.CarriesTransparency(target));

    /// <summary>The finished file for <paramref name="recipe"/> applied to the picture at <paramref name="path"/>.</summary>
    /// <param name="made">What the picture tools made for the recipe: the restoration, the cut-out's mask.</param>
    /// <exception cref="InvalidOperationException">The recipe asks the picture tools for something not made yet.</exception>
    public byte[] Export(string path, ImageRecipe recipe, ImageContainer target, CancellationToken ct = default, MadeByTools? made = null)
    {
    made ??= MadeByTools.None;
    if (!made.Serves(recipe))
        throw new InvalidOperationException("What the picture tools make for the picture is not made yet.");
    var file = ImageEditingServices.ReadSource(path);
    ct.ThrowIfCancellationRequested();

    using var full = services.Decoders.Decode(file, maxLongEdge: null);
    ct.ThrowIfCancellationRequested();

    using var restoredFull = recipe.Restore is not null ? services.DecodeRestored(made.Restored!, full.SourceSize, maxLongEdge: null) : null;
    using var mask = recipe.CutOut is not null ? CutOutMasks.Decode(made.CutOut!) : null;
    using var pipeline = services.Pipeline(full, cache: false, restoredFull, mask);
        var picture = pipeline.Render(recipe, RenderRequest.Saved);
        ct.ThrowIfCancellationRequested();

        var source = services.Metadata.Read(file, full.Container);
        var blocks = recipe.Export.StripMetadata
            ? source.Stripped()
            : source.ForSavedPicture(picture.Width, picture.Height);

        var options = new EncodeOptions(recipe.Export.JpegQuality,
                                        Lossless: target == ImageContainer.Webp && ImageContainers.IsLosslessWebp(file));
        return services.Encoders.Encode(picture, target, options, blocks);
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> over <paramref name="path"/> without ever leaving it half-written: a swap file
    /// beside it, then one replace. The original's attributes and creation time carry over.
    /// </summary>
    public static void ReplaceFile(string path, byte[] bytes)
    {
        var swap = path + ".nexsave.tmp";
        File.WriteAllBytes(swap, bytes);
        try
        {
            File.Replace(swap, path, null);
        }
        catch
        {
            File.Delete(swap);
            throw;
        }
    }
}
