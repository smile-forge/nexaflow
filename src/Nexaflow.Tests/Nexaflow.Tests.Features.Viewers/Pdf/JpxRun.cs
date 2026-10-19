using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// What the JPEG 2000 measurements share: the folder a run writes to, the corpus's JPEG 2000 images — every one, or a
/// sample of each size — and a log, beside the file the run will write, of how far it has got.
/// </summary>
/// <remarks>
/// Opt-in: <c>NEXAFLOW_JPX_BENCH</c> names the folder; <c>NEXAFLOW_JPX_CORPUS</c> the folders of PDFs, separated
/// as a PATH is (PdfPig's own test documents when it names none); <c>NEXAFLOW_JPX_PER_SIZE</c> how many images of
/// each size a sampled run measures.
/// </remarks>
internal sealed class JpxRun : IDisposable
{
    public const string Folder = "NEXAFLOW_JPX_BENCH";
    public const string Library = "NEXAFLOW_OPENJP2";
    private const string Corpus = "NEXAFLOW_JPX_CORPUS";
    private const string PerSize = "NEXAFLOW_JPX_PER_SIZE";

    /// <summary>How many images of each size a sampled run measures, unless <c>NEXAFLOW_JPX_PER_SIZE</c> says otherwise; 0 is all.</summary>
    private const int DefaultPerSize = 6;

    /// <summary>The sizes images are chosen and totalled by.</summary>
    public static readonly (string Name, double Below)[] Sizes =
        [("under 0.1 MP", 0.1), ("0.1-1 MP", 1), ("1-4 MP", 4), ("4 MP and over", double.MaxValue)];

    private static readonly JsonSerializerOptions Written = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _folder;
    private readonly string _kind;
    private readonly string _stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    private readonly StreamWriter _progress;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, object> _corpus;
    private IReadOnlyList<JpxStream>? _streams;

    private JpxRun(string folder, string kind, bool everyImage)
    {
        _folder = folder;
        _kind = kind;
        Directory.CreateDirectory(folder);
        _progress = new StreamWriter(Path.Combine(folder, $"jpx-{kind}-{_stamp}.log")) { AutoFlush = true };

        string[] folders = Environment.GetEnvironmentVariable(Corpus) is { Length: > 0 } named
            ? named.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [Path.Combine(TestSampleData.RepoRoot, "external", "PdfPig", "src", "UglyToad.PdfPig.Tests")];

        var pdfs = folders.SelectMany(folder => Directory.EnumerateFiles(folder, "*.pdf", SearchOption.AllDirectories))
            .Order(StringComparer.OrdinalIgnoreCase).ToList();
        var (found, unread) = JpxCorpus.Scan(pdfs);
        var perSize = everyImage ? 0 : int.TryParse(Environment.GetEnvironmentVariable(PerSize), out var asked) ? asked : DefaultPerSize;
        Entries = Sampled(found, perSize);
        Megapixels = Entries.Sum(entry => entry.DeclaredMegapixels);
        Log($"{found.Count} images in {pdfs.Count} PDFs ({unread.Count} unread); measuring {Entries.Count}, {Megapixels:N0} MP");

        _corpus = new Dictionary<string, object>
        {
            ["folders"] = folders,
            ["pdfs"] = pdfs.Count,
            ["unread"] = unread,
            ["images"] = found.Count,
            ["perSize"] = perSize,
            ["sampled"] = Entries.Count,
        };
    }

    /// <summary>The images this run measures, where each one is.</summary>
    public IReadOnlyList<JpxEntry> Entries { get; }

    /// <summary>What the image dictionaries declare the measured images to hold, in all.</summary>
    public double Megapixels { get; }

    /// <summary>The measured images' bytes, read on first use — a run over every image visits them instead.</summary>
    public IReadOnlyList<JpxStream> Streams => _streams ??= JpxCorpus.Load(Entries);

    /// <summary>The run, or nothing — and the test inconclusive — when no folder is set to write it to.</summary>
    public static JpxRun? Open(string kind, bool everyImage = false)
    {
        var folder = Environment.GetEnvironmentVariable(Folder);
        if (string.IsNullOrWhiteSpace(folder)) { Assert.Inconclusive($"set {Folder} to a folder to write the run to"); return null; }
        return new JpxRun(folder, kind, everyImage);
    }

    /// <summary>The libopenjp2 build to measure against, or nothing — and the test failed — when none is named.</summary>
    public static string? NativeLibrary()
    {
        var library = Environment.GetEnvironmentVariable(Library);
        if (string.IsNullOrWhiteSpace(library) || !File.Exists(library)) { Assert.Fail($"set {Library} to the openjp2.dll to measure against"); return null; }
        return library;
    }

    public TimeSpan Elapsed => _clock.Elapsed;

    public void Log(string line) => _progress.WriteLine($"{_clock.Elapsed:hh\\:mm\\:ss} {line}");

    /// <summary>
    /// An image kept to look at after the run, in a folder of its own beside the run's file, named for where it came
    /// from. Returns the file's path.
    /// </summary>
    public string Keep(JpxStream stream)
    {
        var kept = Path.Combine(_folder, $"jpx-{_kind}-{_stamp}");
        Directory.CreateDirectory(kept);
        var file = Path.Combine(kept, string.Concat(stream.Name.Split(Path.GetInvalidFileNameChars())) + (stream.IsRawCodestream ? ".j2k" : ".jp2"));
        File.WriteAllBytes(file, stream.Data);
        return file;
    }

    /// <summary>What every run records about itself, for the measurement to add to.</summary>
    public Dictionary<string, object> Described() => new()
    {
        ["label"] = Environment.GetEnvironmentVariable("NEXAFLOW_BENCH_LABEL") ?? "",
        ["commit"] = Environment.GetEnvironmentVariable("NEXAFLOW_BENCH_COMMIT") ?? "",
#if DEBUG
        ["configuration"] = "Debug",
#else
        ["configuration"] = "Release",
#endif
        ["machine"] = Environment.MachineName,
        ["runtime"] = RuntimeInformation.FrameworkDescription,
        ["when"] = DateTimeOffset.Now.ToString("o"),
        ["corpus"] = _corpus,
    };

    public void Write(Dictionary<string, object> run)
    {
        var file = Path.Combine(_folder, $"jpx-{_kind}-{_stamp}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(run, Written));
        Log($"wrote {file}");
        Console.WriteLine(file);
    }

    public void Dispose() => _progress.Dispose();

    /// <summary>Which of <see cref="Sizes"/> an image of this many megapixels falls in.</summary>
    public static int SizeOf(double megapixels)
    {
        var at = 0;
        while (megapixels >= Sizes[at].Below) at++;
        return at;
    }

    /// <summary>
    /// <paramref name="perSize"/> images of each size, by the size their dictionaries declare, spread evenly from the
    /// smallest file to the largest so how hard an image was compressed varies too — or every image, at 0. The same
    /// corpus always yields the same sample, so runs compare.
    /// </summary>
    private static List<JpxEntry> Sampled(IReadOnlyList<JpxEntry> entries, int perSize)
    {
        if (perSize <= 0) return [.. entries];

        var sampled = new List<JpxEntry>();
        foreach (var group in entries.GroupBy(entry => SizeOf(entry.DeclaredMegapixels)).OrderBy(g => g.Key))
        {
            var ordered = group.OrderBy(entry => entry.Bytes).ThenBy(entry => entry.Name, StringComparer.Ordinal).ToList();
            if (ordered.Count <= perSize) { sampled.AddRange(ordered); continue; }

            var step = (ordered.Count - 1) / (double)Math.Max(1, perSize - 1);
            for (var pick = 0; pick < perSize; pick++) sampled.Add(ordered[(int)Math.Round(pick * step)]);
        }

        return sampled;
    }
}
