using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>One formula from the corpus, and the reference rendering that goes with it.</summary>
/// <param name="Index">Line number in the dataset, 0-based - the only stable name an entry has.</param>
/// <param name="Formula">The LaTeX, as the dataset tokenised it (one space between every token).</param>
/// <param name="ImageName">File name of the reference PNG, or null where the dataset lists none.</param>
internal sealed record CorpusEntry(int Index, string Formula, string? ImageName)
{
    public string Id => this.ImageName is { } name
        ? Path.GetFileNameWithoutExtension(name)
        : this.Index.ToString("D7");
}

/// <summary>
/// The im2latex-style datasets: a file of formulas and a file of image names, one per line, in step
/// with each other, beside a folder of the images themselves.
/// </summary>
internal sealed class Corpus
{
    private const string FormulasFile = "final_png_formulas.txt";
    private const string ImageNamesFile = "corresponding_png_images.txt";
    private const string ImagesFolder = "generated_png_images";

    private Corpus(string root, IReadOnlyList<CorpusEntry> entries)
    {
        this.Root = root;
        this.Entries = entries;
    }

    public string Root { get; }

    public IReadOnlyList<CorpusEntry> Entries { get; }

    public string ImagePath(CorpusEntry entry) =>
        Path.Combine(this.Root, ImagesFolder, entry.ImageName ?? throw new InvalidOperationException(
            $"Entry {entry.Index} has no reference image."));

    public static Corpus Load(string root, int limit = 0, int skip = 0)
    {
        var formulasPath = Path.Combine(root, FormulasFile);
        var namesPath = Path.Combine(root, ImageNamesFile);
        if (!File.Exists(formulasPath))
            throw new FileNotFoundException($"No {FormulasFile} under {root}.", formulasPath);

        // The two files are parallel, but nothing enforces it, so read them together and say so if
        // they part company rather than silently pairing a formula with someone else's image.
        var names = File.Exists(namesPath) ? File.ReadAllLines(namesPath) : [];
        var entries = new List<CorpusEntry>();
        var index = 0;
        foreach (var line in File.ReadLines(formulasPath))
        {
            var formula = line.Trim();
            if (formula.Length > 0 && index >= skip)
            {
                var name = index < names.Length ? names[index].Trim() : null;
                entries.Add(new CorpusEntry(index, formula, string.IsNullOrEmpty(name) ? null : name));
                if (limit > 0 && entries.Count >= limit)
                {
                    index++;
                    break;
                }
            }

            index++;
        }

        if (names.Length > 0 && names.Length != CountLines(formulasPath) && limit == 0 && skip == 0)
            Console.Error.WriteLine(
                $"  note: {names.Length} image names against {CountLines(formulasPath)} formulas - " +
                "the shorter file wins where they disagree.");

        return new Corpus(root, entries);
    }

    private static int CountLines(string path)
    {
        var count = 0;
        foreach (var _ in File.ReadLines(path)) count++;
        return count;
    }
}
