using System.IO;
using System.Security.Cryptography;
using System.Text;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.Features.Images.Editing.Recipe;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Restoring;

/// <summary>
/// The photos the picture tools restored, kept on disk so a restoration is made once: a PNG for each photo, way of
/// restoring it and set of models, found again whenever the photo is shown restored that way — after a restart too. Past
/// <see cref="RestoredPictures(string, long)"/>'s budget the least recently used go first.
/// </summary>
/// <remarks>
/// A restoration is of the photo upright, as every recipe coordinate reads it, in its own colour space, 8 bits a channel.
/// It goes to the tools as raw RGBA and comes back the same way (<see cref="PictureRestoreRequest"/>), and is kept as PNG
/// with that colour space embedded, so it decodes as the same pixels.
/// </remarks>
public sealed class RestoredPictures(string folder, long budgetBytes)
{
    /// <summary>How much disk the restorations may take together before the oldest are let go.</summary>
    public const long DefaultBudget = 4L << 30;

    public static string DefaultFolder => KeptFiles.CacheFolder("restored");

    private readonly KeptFiles _kept = new(folder, budgetBytes, ".png");

    public RestoredPictures() : this(DefaultFolder, DefaultBudget) { }

    public string Folder => _kept.Folder;

    /// <summary>
    /// Where the restoration of <paramref name="photo"/> by <paramref name="restore"/> with the models
    /// <paramref name="modelsVersion"/> names is kept: by the photo's content, so the same photo moved keeps its
    /// restoration and a photo changed on disk is restored afresh. A strength over it is the same restoration.
    /// </summary>
    public string PathOf(string photo, RecipeRestore restore, string modelsVersion) => _kept.PathOf(photo, Made(restore, modelsVersion));

    /// <summary>The kept restoration, if it is here — marked as just used.</summary>
    public string? Find(string photo, RecipeRestore restore, string modelsVersion) => _kept.Find(photo, Made(restore, modelsVersion));

    /// <summary>What a restoration is filed by, beside the photo's content: a strength over it is the same restoration.</summary>
    private static string Made(RecipeRestore restore, string modelsVersion) => $"{restore.Scale}|{restore.Faces}|{modelsVersion}";

    /// <summary>
    /// Restores <paramref name="photo"/> as <paramref name="restore"/> asks — unless it is kept already — and keeps it:
    /// the photo decoded at full size and turned upright, through the tools, encoded as PNG beside the others.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tools could not restore it.</exception>
    public async Task<string> MakeAsync(ImageEditingServices services, string photo, RecipeRestore restore, IPictureTools tools,
                                        IProgress<double>? progress, CancellationToken ct)
    {
        var path = PathOf(photo, restore, tools.ModelsVersion);
        if (File.Exists(path))
        {
            KeptFiles.Touch(path);
            return path;
        }

        Directory.CreateDirectory(folder);
        var work = Directory.CreateTempSubdirectory("nexaflow-restoring-").FullName;
        try
        {
            string pixels = Path.Combine(work, "photo.rgba"), into = Path.Combine(work, "restored.rgba");
            var (width, height, space) = await Task.Run(() => WriteUpright(services, photo, pixels), ct);
            var restored = await tools.RestoreAsync(new PictureRestoreRequest(pixels, width, height, restore.Scale, restore.Faces, into), progress, ct);
            var kept = path + ".tmp";
            await Task.Run(() => Encode(into, restored.Width, restored.Height, space, kept), ct);
            _kept.Keep(kept, path);
            return path;
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { /* the temp folder's own clean-up has it */ }
        }
    }

    /// <summary>The photo at full size, upright, as raw unpremultiplied RGBA in its own colour space — what the tools read.</summary>
    private static (int Width, int Height, SKColorSpace Space) WriteUpright(ImageEditingServices services, string photo, string into)
    {
        var upright = services.ReadUpright(photo);
        File.WriteAllBytes(into, upright.Rgba);
        return (upright.Width, upright.Height, upright.Space);
    }

    /// <summary>The tools' raw RGBA kept as PNG, its colour space embedded; fast compression, since it is a cache.</summary>
    private static void Encode(string pixels, int width, int height, SKColorSpace space, string into)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul, space);
        using var image = SKImage.FromPixelCopy(info, File.ReadAllBytes(pixels))
                          ?? throw new InvalidOperationException("The restored picture could not be read back.");
        using var pixmap = image.PeekPixels();
        using var data = pixmap.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, zLibLevel: 2))
                         ?? throw new InvalidOperationException("The restored picture could not be encoded.");
        using var file = File.Create(into);
        data.SaveTo(file);
    }
}
