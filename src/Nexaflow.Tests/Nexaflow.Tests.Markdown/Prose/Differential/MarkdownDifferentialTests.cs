using System.Diagnostics;
using System.Text;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Prose.Differential;

/// <summary>
/// The reading held to Markdig over every document the corpus has (<see cref="MarkdownCorpus"/>), with every place the two
/// part listed — so what the reading gets wrong is found, and a reader of our own is judged by the same measure from its
/// first commit.
///
/// <para>
/// <strong>The ledger is a ratchet.</strong> <c>markdown-reading-differences.txt</c> beside this holds every divergence
/// that is known, each under a line saying why. One it does not hold fails, and so does one it holds that has gone — it
/// only shrinks. A failure writes everything found beside the test binary, so the ledger is put right by reading what
/// changed rather than by guessing at it.
/// </para>
/// <para>
/// A document stopped part-way is held to whatever the whole of it is held to, and what it adds is its own: each block of
/// Samples and Pages is cut at its line ends, and what a cut parts on that the whole block does not is what is listed —
/// once per document, because the same half-written construct turns up at every cut after it.
/// </para>
/// </summary>
[TestClass]
[CoversNode("markdown-text")]
[CoversNode("reference-links")]
public class MarkdownDifferentialTests
{
    private const string LedgerName = "markdown-reading-differences.txt";

    /// <summary>How many places each block is cut at, at most.</summary>
    private const int CutsPerBlock = 6;

    private static readonly Lazy<List<MarkdownDivergence>> FoundLazy = new(Diverged);

    private static string LedgerFile => Path.Combine(
        TestSampleData.RepoRoot, "src", "Nexaflow.Tests", "Nexaflow.Tests.Markdown", "Prose", "Differential", LedgerName);

    // ── What it reads ───────────────────────────────────────────────────────

    [TestMethod]
    public void EveryDocument_ReadsBackAsItWasWritten_AndSaysWhereEveryPieceCameFrom()
    {
        var failures = new List<string>();

        foreach (var input in MarkdownCorpus.Whole.Concat(MarkdownCorpus.Whole.Where(Cuttable).SelectMany(Cuts)))
        {
            var read = MarkdownReadings.Today(input.Source);

            if (!read.Prints(input.Source)) failures.Add($"{input.Id}: does not print as what it was read from");
            else if (AstOracle.Faults(input.Source, read).FirstOrDefault() is { } fault) failures.Add($"{input.Id}: {fault}");
        }

        Assert.AreEqual(0, failures.Count, $"{failures.Count} document(s):\n  " + string.Join("\n  ", failures.Take(20)));
    }

    // ── Where it parts from Markdig ─────────────────────────────────────────

    [TestMethod]
    public void TheReading_AgreesWithMarkdig_ButWhereTheLedgerSays()
    {
        var found = FoundLazy.Value;
        var unknown = Except(found.Select(divergence => divergence.Entry), Ledger()).ToHashSet(StringComparer.Ordinal);

        if (unknown.Count == 0) return;

        var shown = found.Where(divergence => unknown.Contains(divergence.Entry)).ToList();

        Assert.Fail($"{shown.Count} divergence(s) from Markdig that {LedgerName} does not hold — fix the reading, or add each "
                  + $"under a line saying why. Everything found is in {Written(found)}.\n\n{Summary(shown)}\n  "
                  + string.Join("\n  ", shown.Take(40).Select(divergence => divergence.Shown)));
    }

    [TestMethod]
    public void TheLedger_HoldsNothingThatNoLongerDiverges()
    {
        var found = FoundLazy.Value;
        var gone = Except(Ledger(), found.Select(divergence => divergence.Entry)).ToList();

        Assert.AreEqual(0, gone.Count,
            $"{LedgerName} holds {gone.Count} divergence(s) that are not there any more — delete these lines:\n  "
          + string.Join("\n  ", gone.Take(40)));
    }

    // ── A reading a stretch at a time ───────────────────────────────────────

    [TestMethod]
    public void AStretchedReading_SaysWhatTheWholeReadingSays()
    {
        var failures = new List<string>();

        foreach (var input in MarkdownCorpus.Whole.Where(Cuttable))
        {
            var whole = MarkdownShape.Of(MarkdownParser.Parsing()(input.Source).Tree, input.Source);

            foreach (var length in new[] { 256, 4096 })
            {
                var chunks = new ContentChunks(input.Source, MarkdownParser.Language);
                var parse = MarkdownParser.Windowed();

                while (chunks.Next(length) is { } next)
                    chunks.Add(ContentChunk.Read(parse, input.Source, next.From, next.SoftMaxLength, next.Resume));

                var stretched = MarkdownShape.Of(chunks.Tree(), input.Source);

                foreach (var construct in MarkdownReadings.Unmatched(whole, stretched).Take(3))
                    failures.Add($"{input.Id} @{length}: whole-only {construct.Named(input.Source)} at {construct.Where(input.Source)}");

                foreach (var construct in MarkdownReadings.Unmatched(stretched, whole).Take(3))
                    failures.Add($"{input.Id} @{length}: stretched-only {construct.Named(input.Source)} at {construct.Where(input.Source)}");
            }
        }

        Assert.AreEqual(0, failures.Count, $"{failures.Count} difference(s):\n  " + string.Join("\n  ", failures.Take(40)));
    }

    // ── How long it takes ───────────────────────────────────────────────────

    /// <summary>
    /// What a reading costs, document by document: Markdig's, the app's, and a keystroke's in the middle of the longest
    /// document. Reported, not asserted — it is the baseline a reader of our own is held to — in
    /// <c>markdown-reading-costs.tsv</c> beside the test binary. Opt-in, by <c>NEXAFLOW_MARKDOWN_TIMING</c>, because a time
    /// is only worth reading off a quiet machine.
    /// </summary>
    [TestMethod]
    public void WhatAReadingCosts()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NEXAFLOW_MARKDOWN_TIMING")))
        {
            Assert.Inconclusive("Set NEXAFLOW_MARKDOWN_TIMING to measure.");
            return;
        }

        var documents = MarkdownCorpus.Whole.Where(Cuttable).ToList();
        foreach (var input in documents) { MarkdownReadings.Markdig(input.Source); MarkdownReadings.Today(input.Source); }

        var report = new StringBuilder();
        report.AppendLine("document\tchars\tmarkdig ms\tmarkdig KB\ttoday ms\ttoday KB");

        var totals = new double[4];

        foreach (var input in documents)
        {
            var (markdig, markdigBytes) = Measured(() => MarkdownReadings.Markdig(input.Source));
            var (today, todayBytes) = Measured(() => MarkdownReadings.Today(input.Source));

            totals[0] += markdig; totals[1] += markdigBytes; totals[2] += today; totals[3] += todayBytes;
            report.AppendLine($"{input.Id}\t{input.Source.Length}\t{markdig:F2}\t{markdigBytes / 1024:F0}\t{today:F2}\t{todayBytes / 1024:F0}");
        }

        report.AppendLine($"all\t{documents.Sum(input => input.Source.Length)}\t{totals[0]:F1}\t{totals[1] / 1024:F0}\t{totals[2]:F1}\t{totals[3] / 1024:F0}");

        var longest = documents.MaxBy(input => input.Source.Length)!;
        var parse = MarkdownParser.Parsing();
        parse(longest.Source);

        var middle = longest.Source.LastIndexOf(' ', longest.Source.Length / 2) + 1;
        var typed = longest.Source.Insert(middle, "x");
        var (keystroke, keystrokeBytes) = Measured(() => parse(typed));

        report.AppendLine($"keystroke in {longest.Id}\t{keystroke:F2} ms\t{keystrokeBytes / 1024:F0} KB");

        // Beside the test binary, because a passing test's output is not shown.
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "markdown-reading-costs.tsv"), report.ToString());
    }

    /// <summary>
    /// What one reading costs: the fastest of many — a time is noise, a best time is the work — and what each allocates, which
    /// is the same every time.
    /// </summary>
    private static (double Milliseconds, double Bytes) Measured(Action read)
    {
        const int Least = 7;
        const double Enough = 60;

        var fastest = double.MaxValue;
        var bytes = 0L;
        var spent = 0.0;

        for (var round = 0; round < Least || spent < Enough; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();

            read();

            var took = clock.Elapsed.TotalMilliseconds;
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            fastest = Math.Min(fastest, took);
            spent += took;

            if (round > 1000) break;
        }

        return (fastest, bytes);
    }

    // ── Gathering ───────────────────────────────────────────────────────────

    /// <summary>Whether a set's documents are written as documents, and so are worth stopping part-way.</summary>
    private static bool Cuttable(MarkdownInput input) => input.Set is MarkdownCorpus.Samples or MarkdownCorpus.Pages;

    private static IEnumerable<MarkdownInput> Cuts(MarkdownInput document) =>
        MarkdownCorpus.Blocks(document).SelectMany(block => MarkdownCorpus.Cut(block, CutsPerBlock));

    /// <summary>Every divergence in the corpus: each whole document's, and each half-written block's own, once per document.</summary>
    private static List<MarkdownDivergence> Diverged()
    {
        var found = new List<MarkdownDivergence>();

        foreach (var input in MarkdownCorpus.Whole)
        {
            found.AddRange(MarkdownReadings.Parted(input));

            if (!Cuttable(input)) continue;

            var named = $"{input.Id}~cut";
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var block in MarkdownCorpus.Blocks(input))
            {
                var whole = MarkdownReadings.Parted(block).Select(divergence => (divergence.Side, divergence.Construct)).ToHashSet();

                foreach (var cut in MarkdownCorpus.Cut(block, CutsPerBlock))
                    foreach (var divergence in MarkdownReadings.Parted(cut))
                    {
                        if (whole.Contains((divergence.Side, divergence.Construct))) continue;

                        var own = divergence with { Input = named };
                        if (seen.Add(own.Entry)) found.Add(own);
                    }
            }
        }

        return found;
    }

    /// <summary>What the ledger holds, a line for each time a divergence is held.</summary>
    private static List<string> Ledger() =>
        File.Exists(LedgerFile)
            ? [.. File.ReadAllLines(LedgerFile).Where(line => line.Length > 0 && !line.StartsWith('#'))]
            : [];

    /// <summary>What <paramref name="these"/> hold that <paramref name="those"/> do not, counting each line as often as it is held.</summary>
    private static IEnumerable<string> Except(IEnumerable<string> these, IEnumerable<string> those)
    {
        var left = those.GroupBy(line => line, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (var line in these)
        {
            if (left.TryGetValue(line, out var count) && count > 0) left[line] = count - 1;
            else yield return line;
        }
    }

    /// <summary>How many divergences there are of each kind, on each side, in each set.</summary>
    private static string Summary(IEnumerable<MarkdownDivergence> divergences) => string.Join("\n", divergences
        .GroupBy(divergence => (Set: SetOf(divergence.Input), divergence.Side, divergence.Construct.Kind))
        .OrderBy(group => group.Key.Set, StringComparer.Ordinal).ThenByDescending(group => group.Count())
        .Select(group => $"{group.Count(),6}  {group.Key.Set,-12} {group.Key.Side,-12} {group.Key.Kind}"));

    private static string SetOf(string input) =>
        input.EndsWith("~cut", StringComparison.Ordinal) ? MarkdownCorpus.Cuts
        : MarkdownCorpus.Whole.FirstOrDefault(whole => whole.Id == input)?.Set ?? "?";

    /// <summary>Everything found, as the ledger would hold it and again with where each is, written beside the test binary.</summary>
    private static string Written(IEnumerable<MarkdownDivergence> found)
    {
        var path = Path.Combine(AppContext.BaseDirectory, Path.ChangeExtension(LedgerName, ".found.txt"));
        File.WriteAllLines(path, found.Select(divergence => divergence.Entry));
        File.WriteAllLines(Path.ChangeExtension(path, ".where.txt"), found.Select(divergence => divergence.Shown));
        return path;
    }
}
