using Nexaflow.Markdown.Latex;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Maths.Latex;

/// <summary>
/// The promise the whole tree rests on: what was read prints back as it was written.
///
/// <para>
/// Both halves are here, and the second is the one that holds the first up. <c>Print(Parse(s)) == s</c>
/// says nothing was lost — but a parser that returned the input as one undigested lump would pass it,
/// and so would one that quietly repaired what it read. <see cref="TheParserOnlyEverCopies"/> is the
/// stricter claim: every leaf's characters are found in the source at the offset the tree puts them at,
/// so there is nowhere for an invented character to hide.
/// </para>
/// <para>
/// The prefixes are the point of the exercise rather than a flourish. Every prefix of a formula is
/// something somebody typed on the way to typing the formula, so a parser that handles the finished
/// article and not its prefixes is a parser an editor cannot use — and half the prefixes here are
/// unbalanced, which is what makes them worth asking about.
/// </para>
/// </summary>
[TestClass]
[CoversNode("maths-latex-roundtrip")]
public class TexRoundTripTests
{
    [TestMethod]
    public void EveryConstructReadsBackAsItWasWritten()
    {
        foreach (var (what, latex) in LatexConstructs.Everything)
        {
            var flat = LatexConstructs.Flatten(latex);
            Assert.AreEqual(flat, TexParser.Parse(flat).Print(), what);
        }
    }

    [TestMethod]
    public void IncludingTheLineBreaksAndIndentationItWasWrittenWith()
    {
        // The unflattened originals: newlines, runs of spaces, indentation that means nothing to TeX and
        // everything to whoever has to read the formula again tomorrow. Losing it would be invisible
        // until the first time an edit reprinted a formula somebody had laid out by hand.
        foreach (var (what, latex) in LatexConstructs.Everything)
            Assert.AreEqual(latex, TexParser.Parse(latex).Print(), what);
    }

    [TestMethod]
    public void EveryPrefixOfEveryConstructReadsBackToo()
    {
        foreach (var (what, latex) in LatexConstructs.Everything)
        {
            var flat = LatexConstructs.Flatten(latex);

            for (var length = 0; length <= flat.Length; length++)
            {
                var typed = flat[..length];
                Assert.AreEqual(typed, TexParser.Parse(typed).Print(),
                    $"{what}: after {length} character(s)");
            }
        }
    }

    [TestMethod]
    public void AndEverySuffix()
    {
        // What arrives when somebody pastes the back half of a formula — a run that starts inside a
        // group, or with the closing brace of something that was never opened here.
        foreach (var (what, latex) in LatexConstructs.Everything)
        {
            var flat = LatexConstructs.Flatten(latex);

            for (var start = 0; start <= flat.Length; start++)
            {
                var pasted = flat[start..];
                Assert.AreEqual(pasted, TexParser.Parse(pasted).Print(),
                    $"{what}: from character {start}");
            }
        }
    }

    [TestMethod]
    public void TheParserOnlyEverCopies()
    {
        foreach (var (what, latex) in LatexConstructs.Everything)
        {
            var flat = LatexConstructs.Flatten(latex);

            foreach (var place in TexParser.Parse(flat).Placed())
            {
                if (!place.Node.IsLeaf) continue;

                Assert.IsTrue(place.End <= flat.Length,
                    $"{what}: {place.Node.Kind} claims {place.Start}+{place.Node.Width} of {flat.Length}");

                Assert.AreEqual(flat.Substring(place.Start, place.Node.Width), place.Node.Text,
                    $"{what}: {place.Node.Kind} at {place.Start} is not what the source says");
            }
        }
    }

    [TestMethod]
    public void AndEveryPieceSaysWhereItWasReadFrom()
    {
        // The same claim, asked of the tree instead of worked out from it. Placed() walks the tree to find an
        // offset, so it agrees with the order the tree is in whether or not any piece knows its own place; this
        // asks each piece where it was read from and holds the answer against the source.
        foreach (var (what, latex) in LatexConstructs.Everything)
        {
            var flat = LatexConstructs.Flatten(latex);
            var faults = AstOracle.Faults(flat, TexParser.Parse(flat)).ToList();

            Assert.AreEqual(0, faults.Count, $"{what}\n{string.Join("\n", faults)}");
        }
    }

    [TestMethod]
    public void AndItSaysSoForEveryPrefixAndEverySuffixToo()
    {
        // Where the half-written formulas are, and so where a piece that does not know its own place would
        // first be noticed: a brace closing nothing, a command whose argument has not been typed.
        foreach (var (what, latex) in LatexConstructs.Everything)
        {
            var flat = LatexConstructs.Flatten(latex);

            for (var length = 0; length <= flat.Length; length++)
            {
                var typed = flat[..length];
                var faults = AstOracle.Faults(typed, TexParser.Parse(typed)).ToList();

                Assert.AreEqual(0, faults.Count, $"{what}: after {length} character(s)\n{string.Join("\n", faults)}");
            }

            for (var from = 0; from < flat.Length; from++)
            {
                var tail = flat[from..];
                var faults = AstOracle.Faults(tail, TexParser.Parse(tail)).ToList();

                Assert.AreEqual(0, faults.Count, $"{what}: from {from}\n{string.Join("\n", faults)}");
            }
        }
    }

    [TestMethod]
    public void AndAReversedTreeStillPrintsWhatWasWritten()
    {
        // The sharpest form of it, and the one no round trip can make: with the parts of every piece in the
        // opposite order the source only comes back if each piece knows where it was read from rather than
        // relying on where it happens to sit.
        foreach (var (what, latex) in LatexConstructs.Everything)
            Assert.AreEqual(latex, AstOracle.Reversed(TexParser.Parse(latex)).Print(), what);
    }

    [TestMethod]
    public void AWholeDocumentOfRealFormulasReadsBack()
    {
        // Opt-in, and the only thing that can speak for constructs nobody thought to write down. Point
        // NEXAFLOW_LATEX_CORPUS at a file of one formula per line — the same corpus the layout sweep
        // uses. Unlike that sweep this needs no fonts, no desktop and no rasteriser, so a quarter of a
        // million formulas is seconds rather than hours.
        var corpus = Environment.GetEnvironmentVariable("NEXAFLOW_LATEX_CORPUS");
        if (string.IsNullOrWhiteSpace(corpus) || !File.Exists(corpus))
            Assert.Inconclusive($"set NEXAFLOW_LATEX_CORPUS to a file of formulas (got: {corpus ?? "nothing"})");

        var seen = 0;
        var faults = 0;
        var first = new List<string>();

        foreach (var raw in File.ReadLines(corpus))
        {
            var latex = raw.Trim();
            if (latex.Length == 0) continue;

            seen++;
            var root = TexParser.Parse(latex);

            if (Complaint(root, latex) is not { } complaint) continue;

            faults++;
            if (first.Count < 20) first.Add($"line {seen}: {latex}\n     {complaint}");
        }

        // Said out loud, because "passed" over a corpus that turned out to be empty looks exactly like
        // "passed" over a quarter of a million formulas.
        Assert.IsTrue(seen > 1000, $"only {seen} formula(s) in {corpus} — is that the right file?");

        // Counted in full and reported in part: a run that reports "20 faults" whenever there are at
        // least twenty says nothing about whether a change made it better or worse.
        Assert.AreEqual(0, faults, $"of {seen} formulas read:\n" + string.Join("\n", first));
    }

    /// <summary>What is wrong with this reading of <paramref name="latex"/>, or nothing.</summary>
    /// <remarks>
    /// Both invariants, because the sweep is the only place a construct nobody remembered gets asked
    /// about at all, and asking it only the weaker of the two would be a waste of the corpus.
    /// </remarks>
    private static string? Complaint(ContentNode root, string latex)
    {
        var printed = root.Print();
        if (printed != latex) return $"read back as: {printed}";

        foreach (var place in root.Placed())
        {
            if (!place.Node.IsLeaf) continue;
            if (place.End > latex.Length) return $"{place.Node.Kind} claims {place.Start}+{place.Node.Width}";

            if (latex.Substring(place.Start, place.Node.Width) != place.Node.Text)
                return $"{place.Node.Kind} at {place.Start} holds \"{place.Node.Text}\", "
                       + $"the source has \"{latex.Substring(place.Start, place.Node.Width)}\"";
        }

        return null;
    }
}
