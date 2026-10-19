using System.Diagnostics;
using System.Text;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Prose.Corpus;

/// <summary>
/// The reading over every document the corpus has (<see cref="MarkdownCorpus"/>) — the specifications' examples, the samples,
/// the pages, and every block of the samples and pages stopped part-way: each read back as it was written with every piece
/// saying where it came from, and read a stretch at a time as it reads whole.
///
/// <para>
/// What the reading makes of the specifications is held to what they say through the HTML it is written out as
/// (<c>MarkdownHtmlSpecTests</c>, in <c>Tests.Visuals</c>, where a document can be laid out).
/// </para>
/// </summary>
[TestClass]
[CoversNode("markdown-text")]
public class MarkdownCorpusTests
{
    /// <summary>How many places each block is cut at, at most.</summary>
    private const int CutsPerBlock = 6;

    [TestMethod]
    [CoversNode("reference-links")]
    public void EveryDocument_ReadsBackAsItWasWritten_AndSaysWhereEveryPieceCameFrom()
    {
        var failures = new List<string>();

        foreach (var input in MarkdownCorpus.Whole.Concat(MarkdownCorpus.Whole.Where(Cuttable).SelectMany(Cuts)))
        {
            var read = MarkdownParser.Worked(input.Source);

            if (!read.Prints(input.Source)) failures.Add($"{input.Id}: does not print as what it was read from");
            else if (AstOracle.Faults(input.Source, read).FirstOrDefault() is { } fault) failures.Add($"{input.Id}: {fault}");
        }

        Assert.AreEqual(0, failures.Count, $"{failures.Count} document(s):\n  " + string.Join("\n  ", failures.Take(20)));
    }

    [TestMethod]
    [CoversNode("reference-links")]
    public void NothingIsShownAsWrittenForWantOfAReading()
    {
        var failures = new List<string>();

        foreach (var input in MarkdownCorpus.Whole)
            foreach (var construct in MarkdownShape.Of(MarkdownParser.Worked(input.Source), input.Source))
                if (construct.Kind is MarkdownShape.ShownAsWritten or MarkdownShape.Unread)
                    failures.Add($"{input.Id}: {construct.Named(input.Source)} at {construct.Where(input.Source)}");

        Assert.AreEqual(0, failures.Count, $"{failures.Count} stretch(es) the reading could not read:\n  " + string.Join("\n  ", failures.Take(40)));
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

                foreach (var construct in MarkdownShape.Unmatched(whole, stretched).Take(3))
                    failures.Add($"{input.Id} @{length}: whole-only {construct.Named(input.Source)} at {construct.Where(input.Source)}");

                foreach (var construct in MarkdownShape.Unmatched(stretched, whole).Take(3))
                    failures.Add($"{input.Id} @{length}: stretched-only {construct.Named(input.Source)} at {construct.Where(input.Source)}");
            }
        }

        Assert.AreEqual(0, failures.Count, $"{failures.Count} difference(s):\n  " + string.Join("\n  ", failures.Take(40)));
    }

    // ── How long it takes ───────────────────────────────────────────────────

    /// <summary>
    /// What a reading costs, document by document, and a keystroke's in the middle of the longest document. Reported, not
    /// asserted, in <c>markdown-reading-costs.tsv</c> beside the test binary. Opt-in, by <c>NEXAFLOW_MARKDOWN_TIMING</c>,
    /// because a time is only worth reading off a quiet machine.
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
        foreach (var input in documents) MarkdownParser.Worked(input.Source);

        var report = new StringBuilder();
        report.AppendLine("document\tchars\tms\tKB");

        var (time, size) = (0.0, 0.0);

        foreach (var input in documents)
        {
            var (took, bytes) = Measured(() => MarkdownParser.Worked(input.Source));

            (time, size) = (time + took, size + bytes);
            report.AppendLine($"{input.Id}\t{input.Source.Length}\t{took:F2}\t{bytes / 1024:F0}");
        }

        report.AppendLine($"all\t{documents.Sum(input => input.Source.Length)}\t{time:F1}\t{size / 1024:F0}");

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
}
