using System.Collections.Immutable;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Imaging;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Erasing;

/// <summary>The erasing a recipe asks for (<see cref="ImageRecipe.Erasures"/>): its steps, in order — what
/// <see cref="ViewModels.PhotoEraser"/> is asked to make.</summary>
public sealed record EraseSteps(ImmutableArray<RecipeErase> Steps)
{
    public bool Equals(EraseSteps? other) => other is not null && Steps.AsSpan().SequenceEqual(other.Steps.AsSpan());

    public override int GetHashCode() => Steps.Length;
}

/// <summary>
/// The photos as erased, kept on disk so erasing is done once: a PNG for each photo, run of erasing steps and set of
/// models, found again whenever the photo is shown with them — after a restart too. Every run of first steps is kept as
/// it is made, so a step added makes only itself, and one undone shows what was there before at once. Past
/// <see cref="ErasedPictures(string, long)"/>'s budget the least recently used go first.
/// </summary>
/// <remarks>
/// An erased photo is the photo upright, as every recipe coordinate reads it, in its own colour space, 8 bits a channel,
/// kept as PNG with that colour space embedded, so it decodes as the same pixels.
/// </remarks>
[LocalDiskOnly("erased photos, kept in the app's cache and handed to picture erasing through the temp folder; the photo itself is read through the runtime's file system")]
public sealed class ErasedPictures(string folder, long budgetBytes)
{
    /// <summary>How much disk the erased photos may take together before the oldest are let go.</summary>
    public const long DefaultBudget = 4L << 30;

    public static string DefaultFolder => KeptFiles.CacheFolder("erased");

    private readonly KeptFiles _kept = new(folder, budgetBytes, ".png");

    public ErasedPictures() : this(DefaultFolder, DefaultBudget) { }

    public string Folder => _kept.Folder;

    /// <summary>Where <paramref name="photo"/> erased by <paramref name="steps"/> with the models
    /// <paramref name="modelsVersion"/> names is kept: by the photo's content, so the same photo moved keeps it.</summary>
    public string PathOf(string photo, EraseSteps steps, string modelsVersion) => _kept.PathOf(photo, Made(steps.Steps, steps.Steps.Length, modelsVersion));

    /// <summary>The kept erased photo, if it is here — marked as just used.</summary>
    public string? Find(string photo, EraseSteps steps, string modelsVersion) => _kept.Find(photo, Made(steps.Steps, steps.Steps.Length, modelsVersion));

    /// <summary>What the photo erased by its first <paramref name="count"/> steps is filed by, beside its content.</summary>
    private static string Made(ImmutableArray<RecipeErase> steps, int count, string modelsVersion)
        => $"erase|{string.Join("||", steps.Take(count).Select(s => s.Describe()))}|{modelsVersion}";

    /// <summary>
    /// Erases <paramref name="photo"/> as <paramref name="steps"/> asks — unless it is kept already — and keeps it: from the
    /// longest run of its first steps already kept, or the photo at full size upright, each step after that erasing what
    /// its mask covers (<see cref="EraseMasks"/>) from what the one before it left — by picture erasing, content-aware
    /// fill or the healing brush — and keeping what it made. How far it is goes to <paramref name="progress"/>, 0 to 1.
    /// </summary>
    /// <exception cref="InvalidOperationException">A tool a step needs is not here, or could not do it.</exception>
    public async Task<string> MakeAsync(ImageEditingServices services, string photo, EraseSteps steps, string modelsVersion, IPictureErasing? erasing,
                                        IPictureSelection? selection, IProgress<double>? progress, CancellationToken ct)
    {
        int count = steps.Steps.Length;
        var path = PathOf(photo, steps, modelsVersion);
        if (File.Exists(path))
        {
            KeptFiles.Touch(path);
            return path;
        }

        int done = count - 1;
        string? from = null;
        for (; done > 0; done--)
            if (_kept.Find(photo, Made(steps.Steps, done, modelsVersion)) is { } kept)
            {
                from = kept;
                break;
            }
        var picture = await Task.Run(() => services.ReadUprightAsync(from ?? photo, ct), ct);
        Directory.CreateDirectory(Folder);
        for (int i = done; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = steps.Steps[i];
            double start = (double)(i - done) / (count - done), share = 1.0 / (count - done);
            var part = progress is null ? null : new Within(f => progress.Report(start + share * f));
            var mask = await EraseMasks.MakeAsync(services, photo, step, selection, picture.Width, picture.Height, ct);
            part?.Report(0.1);
            var erased = step.Fill switch
            {
                EraseFill.Ai => await ByErasingAsync(erasing ?? throw new InvalidOperationException("Picture erasing is not here."), picture, mask, part, ct),
                EraseFill.ContentAware => await Task.Run(() => ContentAwareFill.Fill(picture.Rgba, picture.Width, picture.Height, mask, progress: part, ct: ct), ct),
                EraseFill.Heal => await Task.Run(() => HealingBrush.Heal(picture.Rgba, picture.Width, picture.Height, mask,
                                                                         (int)Math.Round(step.HealFrom?.X ?? 0), (int)Math.Round(step.HealFrom?.Y ?? 0), ct), ct),
                _ => throw new InvalidOperationException($"No way to fill by {step.Fill}."),
            };
            picture = picture with { Rgba = erased };
            var kept = _kept.PathOf(photo, Made(steps.Steps, i + 1, modelsVersion));
            var written = kept + ".tmp";
            await Task.Run(() => Encode(picture, written), ct);
            _kept.Keep(written, kept);
        }
        return path;
    }

    /// <summary>What picture erasing makes of <paramref name="picture"/> under <paramref name="mask"/>, handed over by file.</summary>
    private static async Task<byte[]> ByErasingAsync(IPictureErasing erasing, UprightPixels picture, byte[] mask, IProgress<double>? progress, CancellationToken ct)
    {
        var work = Directory.CreateTempSubdirectory("nexaflow-erasing-").FullName;
        try
        {
            string pixels = Path.Combine(work, "photo.rgba"), hole = Path.Combine(work, "mask.bytes"), into = Path.Combine(work, "erased.rgba");
            await File.WriteAllBytesAsync(pixels, picture.Rgba, ct);
            await File.WriteAllBytesAsync(hole, mask, ct);
            await erasing.EraseAsync(new PictureEraseRequest(pixels, hole, picture.Width, picture.Height, into), progress, ct);
            return await File.ReadAllBytesAsync(into, ct);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { /* the temp folder's own clean-up has it */ }
        }
    }

    /// <summary>The erased photo kept as PNG, its colour space embedded; fast compression, since it is a cache.</summary>
    private static void Encode(UprightPixels picture, string into)
    {
        var info = new SKImageInfo(picture.Width, picture.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul, picture.Space);
        using var image = SKImage.FromPixelCopy(info, picture.Rgba) ?? throw new InvalidOperationException("The erased photo could not be read back.");
        using var pixmap = image.PeekPixels();
        using var data = pixmap.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, zLibLevel: 2))
                         ?? throw new InvalidOperationException("The erased photo could not be encoded.");
        using var file = File.Create(into);
        data.SaveTo(file);
    }

    /// <summary>A step's progress, 0 to 1, reported as its share of the whole.</summary>
    private sealed class Within(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
