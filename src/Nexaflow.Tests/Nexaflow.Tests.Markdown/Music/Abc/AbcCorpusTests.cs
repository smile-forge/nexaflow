using System.Text;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Music.Abc;

/// <summary>
/// The same two invariants, over ten thousand tunes nobody here wrote.
///
/// <para>
/// The hand-written construct list is the record of what is supported; this is the only thing that can
/// speak for what nobody thought to write down. Real ABC in the wild is full of things a construct list
/// never reaches — a <c>%%</c> directive mid-tune, a field with no value, a bar line at the start of a
/// line, mojibake where a name should be — and every one of them is something an editor will be handed.
/// </para>
/// <para>
/// <strong>Read as bytes, decoded once, and never re-encoded.</strong> This corpus was built by
/// something that read UTF-8 as Latin-1, so a good third of it carries mojibake (<c>AntÃ­fona</c>). That
/// is not a defect in the fixture — it is exactly the input a claim of "the parser only ever copies" has
/// to survive, and a test that normalised it first would be testing a tune nobody has.
/// </para>
/// <para>
/// Opt-in, because it needs the corpus on disk. <c>NEXAFLOW_ABC_CORPUS</c> points at a folder; every
/// <c>.abc</c> beneath it is read. Unlike a picture sweep this needs no fonts, no desktop and no
/// rasteriser, so ten thousand tunes is seconds.
/// </para>
/// </summary>
[TestClass]
[CoversNode("abc-ast-roundtrip")]
public class AbcCorpusTests
{
    /// <summary>Where the corpus lives when nothing says otherwise.</summary>
    private const string Default = @"D:\Datasets\abcmusic\zenoob";

    [TestMethod]
    public void TenThousandRealTunesReadBackExactly()
    {
        if (Corpus() is not { } files) { Assert.Inconclusive(Missing); return; }

        var failures = new List<string>();
        var read = 0;

        foreach (var file in files)
        {
            var abc = Text(file);
            var printed = AbcParser.Parse(abc).Print();
            read++;

            if (printed == abc) continue;
            if (failures.Count < 10) failures.Add($"{Path.GetFileName(file)}: {Difference(abc, printed)}");
        }

        Assert.AreEqual(0, failures.Count,
            $"{failures.Count} of {read} tunes did not read back:\n  {string.Join("\n  ", failures)}");
        Assert.IsTrue(read > 0, "the corpus folder held no .abc files");
    }

    [TestMethod]
    public void AndTheParserOnlyEverCopiedThemToo()
    {
        if (Corpus() is not { } files) { Assert.Inconclusive(Missing); return; }

        var failures = new List<string>();

        foreach (var file in files)
        {
            var abc = Text(file);

            foreach (var place in AbcParser.Parse(abc).Placed())
            {
                if (!place.Node.IsLeaf) continue;
                if (place.End <= abc.Length && abc.AsSpan(place.Start, place.Node.Width).SequenceEqual(place.Node.Text))
                    continue;

                if (failures.Count < 10)
                    failures.Add($"{Path.GetFileName(file)}: {place.Node.Kind} at {place.Start} says "
                                 + $"{Show(place.Node.Text)}, source says "
                                 + $"{Show(place.End <= abc.Length ? abc.Substring(place.Start, place.Node.Width) : "<past the end>")}");
                break;
            }
        }

        Assert.AreEqual(0, failures.Count, $"invented text:\n  {string.Join("\n  ", failures)}");
    }

    [TestMethod]
    public void AndAlmostAllOfItIsActuallyRead()
    {
        // Holding what cannot be read is what stops the editor faulting; holding *most* of a corpus would
        // mean the reader had quietly given up. Counted rather than asserted per tune, because real ABC
        // does contain the odd character nothing can mean — the question is whether that is the exception.
        if (Corpus() is not { } files) { Assert.Inconclusive(Missing); return; }

        long characters = 0, held = 0;
        var worst = new List<string>();

        foreach (var file in files)
        {
            var abc = Text(file);
            characters += abc.Length;

            var loose = AbcParser.Parse(abc).SelfAndDescendants()
                .Where(node => node.Trouble is not null)
                .Sum(node => node.Text.Length);

            held += loose;
            if (loose > abc.Length / 20 && worst.Count < 10)
                worst.Add($"{Path.GetFileName(file)}: {loose} of {abc.Length}");
        }

        var share = characters == 0 ? 0 : (double)held / characters;
        Assert.IsTrue(share < 0.01,
            $"{share:P2} of the corpus was held rather than read ({held} of {characters} characters). "
            + $"Worst:\n  {string.Join("\n  ", worst)}");
    }

    [TestMethod]
    public void AndEveryStageLeavesAllTenThousandOfThemAlone()
    {
        // The rule the pipeline is built on, asked of real tunes rather than of the constructs somebody
        // thought to write down. The pipeline checks it itself in a debug build; this is the same check
        // with the corpus behind it, and it names the stage rather than leaving a print to differ.
        if (Corpus() is not { } files) { Assert.Inconclusive(Missing); return; }

        var failures = new List<string>();

        foreach (var file in files)
        {
            var abc = Text(file);
            var tree = AbcParser.Parse(abc);

            foreach (var stage in AbcPipeline.Of().Stages)
            {
                try
                {
                    tree = stage.Run(tree);
                }
                catch (Exception ex)
                {
                    if (failures.Count < 10) failures.Add($"{Path.GetFileName(file)}: {stage.Name} threw {ex.GetType().Name}: {ex.Message}");
                    break;
                }

                if (tree.Print() == abc) continue;

                if (failures.Count < 10) failures.Add($"{Path.GetFileName(file)}: {stage.Name} changed the source");
                break;
            }
        }

        Assert.AreEqual(0, failures.Count, string.Join("\n  ", failures));
    }

    // ── The corpus ──────────────────────────────────────────────────────────

    private const string Missing =
        "Point NEXAFLOW_ABC_CORPUS at a folder of .abc files to run this. "
        + "The invariants are also checked over the hand-written construct list, which always runs.";

    private static IReadOnlyList<string>? Corpus()
    {
        var root = Environment.GetEnvironmentVariable("NEXAFLOW_ABC_CORPUS");
        if (string.IsNullOrWhiteSpace(root)) root = Default;
        if (!Directory.Exists(root)) return null;

        var files = Directory.EnumerateFiles(root, "*.abc", SearchOption.AllDirectories).ToList();
        return files.Count == 0 ? null : files;
    }

    /// <summary>
    /// The file's characters, decoded once and left alone. UTF-8 without a BOM, which is what these are
    /// even where what they hold is somebody else's mis-decoding.
    /// </summary>
    private static string Text(string path) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(File.ReadAllBytes(path));

    private static string Difference(string expected, string actual)
    {
        var at = 0;
        while (at < expected.Length && at < actual.Length && expected[at] == actual[at]) at++;

        return $"first differs at {at}: expected {Show(Around(expected, at))}, got {Show(Around(actual, at))}";
    }

    private static string Around(string text, int at) =>
        text[Math.Max(0, at - 10)..Math.Min(text.Length, at + 10)];

    private static string Show(string text) =>
        "\"" + text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
}
