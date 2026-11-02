using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Selection;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>
/// A picture layer's recipe as a layered file keeps it: each coverage its regions hold — a cut-out's, an erasure's — written
/// as a grey PNG named by its content, the recipe naming the file in its place, and read back the same way. What OpenRaster
/// keeps as files in its package and Photoshop in Nexaflow's own block.
/// </summary>
internal static class RecipeRegionFiles
{
    /// <summary>The files the coverages of a recipe's regions are kept in.</summary>
    public static IEnumerable<string> CoverageFiles(ImageRecipe recipe)
    {
        var files = new List<string>();
        MapRegions(recipe, step =>
        {
            if (step.File.Length > 0) files.Add(step.File);
            return step;
        });
        return files;
    }

    /// <summary>The recipe with every <see cref="RegionCoverage"/> its regions hold given its coverage from the file it names.</summary>
    public static ImageRecipe WithCoverages(ImageRecipe recipe, Func<string, TiledCoverage?> coverageOf)
        => MapRegions(recipe, step => step.Coverage is null && step.File.Length > 0 ? step with { Coverage = coverageOf(step.File) } : step);

    /// <summary>The recipe with each coverage its regions hold handed to <paramref name="keep"/> as a file — its name and its
    /// PNG — the step naming the file.</summary>
    public static ImageRecipe WithCoverageFiles(ImageRecipe recipe, Action<string, byte[]> keep) => MapRegions(recipe, step =>
    {
        if (step.Coverage is not { } coverage) return step;
        var grey = coverage.ToBytes();
        var file = $"data/nexaflow/region-{Convert.ToHexStringLower(SHA256.HashData(grey))[..16]}.png";
        keep(file, GreyPng(grey, coverage.Width, coverage.Height));
        return step with { File = file };
    });

    public static ImageRecipe MapRegions(ImageRecipe recipe, Func<RegionCoverage, RegionCoverage> map)
    {
        RecipeRegion? Mapped(RecipeRegion? region) => region is null ? null
            : region with { Steps = [.. region.Steps.Select(s => s is RegionCoverage coverage ? map(coverage) : s)] };
        return recipe with
        {
            CutOut = recipe.CutOut is { } cutOut ? cutOut with { Region = Mapped(cutOut.Region), } : null,
            Erasures = [.. recipe.Erasures.Select(e => e with { Region = Mapped(e.Region), Pick = e.Pick is { } pick ? pick with { Region = Mapped(pick.Region) } : null })],
        };
    }

    /// <summary>A coverage from the grey PNG it was kept as; null where it is no picture.</summary>
    public static TiledCoverage? CoverageOf(byte[] png)
    {
        using var decoded = SKBitmap.Decode(png);
        if (decoded is null) return null;
        using var grey = decoded.ColorType == SKColorType.Gray8 ? null : decoded.Copy(SKColorType.Gray8);
        var source = grey ?? decoded;
        var bytes = new byte[source.Width * source.Height];
        var origin = source.GetPixels();
        for (int y = 0; y < source.Height; y++) Marshal.Copy(origin + y * source.RowBytes, bytes, y * source.Width, source.Width);
        return TiledCoverage.FromBytes(bytes, source.Width, source.Height);
    }

    public static byte[] GreyPng(byte[] grey, int width, int height)
    {
        using var image = SKImage.FromPixelCopy(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque), grey)
                          ?? throw new InvalidOperationException("A coverage could not be made a picture.");
        using var data = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidOperationException("A coverage could not be written as PNG.");
        return data.ToArray();
    }
}
