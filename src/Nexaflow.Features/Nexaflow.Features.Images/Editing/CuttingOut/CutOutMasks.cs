using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.Features.Images.Editing.Recipe;
using SkiaSharp;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Images.Editing.CuttingOut;

/// <summary>
/// The masks photos are cut out of their backgrounds by, kept on disk so each is picked once: one for each photo,
/// cut-out and set of models, at the photo's full size as it stands — 255 over what is kept, 0 over what is cleared —
/// as an 8-bit grey PNG.
/// </summary>
[LocalDiskOnly("cut-out masks, kept in the app's cache; the photo itself is read through the runtime's file system")]
public sealed class CutOutMasks(string folder, long budgetBytes)
{
    /// <summary>How much disk the masks may take together before the oldest are let go.</summary>
    public const long DefaultBudget = 1L << 30;

    public static string DefaultFolder => KeptFiles.CacheFolder("cutouts");

    private readonly KeptFiles _kept = new(folder, budgetBytes, ".png");

    public CutOutMasks() : this(DefaultFolder, DefaultBudget) { }

    public string Folder => _kept.Folder;

    /// <summary>Where the mask cutting <paramref name="photo"/> out by <paramref name="cutOut"/> with the models
    /// <paramref name="modelsVersion"/> names is kept.</summary>
    public string PathOf(string photo, RecipeCutOut cutOut, string modelsVersion) => _kept.PathOf(photo, Made(cutOut, modelsVersion));

    /// <summary>The kept mask, if it is here — marked as just used.</summary>
    public string? Find(string photo, RecipeCutOut cutOut, string modelsVersion) => _kept.Find(photo, Made(cutOut, modelsVersion));

    /// <summary>
    /// Picks what <paramref name="cutOut"/> keeps of <paramref name="photo"/> — unless its mask is kept already — and keeps
    /// the mask: the photo read at full size as it stands, handed to picture selection, everything picked made one mask.
    /// How far it is goes to <paramref name="progress"/>, from 0 to 1, as each of those is done.
    /// </summary>
    /// <exception cref="InvalidOperationException">Picture selection could not pick it.</exception>
    public async Task<string> MakeAsync(ImageEditingServices services, string photo, RecipeCutOut cutOut, IPictureSelection selection,
                                        IProgress<double>? progress, CancellationToken ct)
    {
        var path = PathOf(photo, cutOut, selection.ModelsVersion);
        if (File.Exists(path))
        {
            KeptFiles.Touch(path);
            return path;
        }

        var upright = await Task.Run(() => services.ReadUprightAsync(photo, ct), ct);
        int width = upright.Width, height = upright.Height;
        progress?.Report(0.2);
        IReadOnlyList<byte[]> masks;
        await using (var session = await selection.OpenAsync(upright.Rgba, width, height, ct))
        {
            progress?.Report(0.7);
            masks = cutOut.Phrase is { } phrase
                ? [.. (await session.FindAsync(phrase, [], width, height, ct)).Select(f => f.Mask.Pixels)]
                : [(await session.SelectAsync([.. cutOut.Clicks.Select(c => new PicturePoint(c.X, c.Y, c.Include))],
                                              cutOut.Box is { } b ? new PictureBox(b.Left, b.Top, b.Right, b.Bottom) : null,
                                              width, height, ct)).Pixels];
        }

        progress?.Report(0.9);
        Directory.CreateDirectory(Folder);
        var written = path + ".tmp";
        await Task.Run(() => Encode(Union(masks, width * height), width, height, written), ct);
        _kept.Keep(written, path);
        return path;
    }

    /// <summary>The kept mask decoded as alpha — what a picture is drawn through to clear what it does not keep.</summary>
    public static SKImage Decode(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException($"{path} is not a mask.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        if (codec.GetPixels(info, out var grey) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            throw new InvalidDataException($"{path} could not be read as a mask.");
        return SKImage.FromPixelCopy(new SKImageInfo(info.Width, info.Height, SKColorType.Alpha8, SKAlphaType.Premul), grey)
               ?? throw new InvalidDataException($"{path} could not be made a mask.");
    }

    /// <summary>The kept mask as one byte a pixel, 255 over what it marks — <paramref name="width"/> × <paramref name="height"/>,
    /// the photo's size it was picked at.</summary>
    public static byte[] ReadGrey(string path, int width, int height)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException($"{path} is not a mask.");
        if (codec.Info.Width != width || codec.Info.Height != height)
            throw new InvalidDataException($"{path} is {codec.Info.Width}x{codec.Info.Height}, not the photo's {width}x{height}.");
        var info = new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
        if (codec.GetPixels(info, out var grey) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            throw new InvalidDataException($"{path} could not be read as a mask.");
        return grey;
    }

    private static string Made(RecipeCutOut cutOut, string modelsVersion) => $"cutout|{cutOut.Describe()}|{modelsVersion}";

    private static byte[] Union(IReadOnlyList<byte[]> masks, int size)
    {
        var union = new byte[size];
        foreach (var mask in masks)
            for (int i = 0; i < size; i++) union[i] |= mask[i];
        return union;
    }

    private static void Encode(byte[] grey, int width, int height, string into)
    {
        using var image = SKImage.FromPixelCopy(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque), grey)
                          ?? throw new InvalidOperationException("The mask could not be made a picture.");
        using var pixmap = image.PeekPixels();
        using var data = pixmap.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, zLibLevel: 6))
                         ?? throw new InvalidOperationException("The mask could not be encoded.");
        using var file = File.Create(into);
        data.SaveTo(file);
    }
}
