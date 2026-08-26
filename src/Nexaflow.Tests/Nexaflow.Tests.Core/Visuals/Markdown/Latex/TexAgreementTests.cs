using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Maths.Latex;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using WpfMath.Parsers;
using XamlMath;

namespace Nexaflow.Tests.Core.Visuals.Markdown.Latex;

/// <summary>
/// The parse tree held against the typesetter's own reading of the same formula — the one place both
/// readings are in scope at once, and the check that says the new one is right rather than merely
/// lossless.
///
/// <para>
/// Reading a formula back exactly as it was written says nothing about whether the parts are the parts
/// the writer meant; a table that gave <c>\frac</c> one argument would round-trip a quarter of a million
/// formulas and be wrong about every fraction in them. The typesetter already knows the answer for
/// every construct it can set, so it is the oracle — with one direction of disagreement allowed, and
/// deliberately so.
/// </para>
/// <para>
/// <strong>The parse tree may see more; it may never see less.</strong> More, because the typesetter's
/// tree is a typesetting tree: a fraction inside <c>\displaystyle</c> is wrapped in a style atom that
/// names no parts at all, so its numerator is not in that tree to be found. Less would mean a construct
/// this parser does not know about, which is the regression worth catching.
/// </para>
/// <para>
/// Needs an STA thread for the typesetter's brushes. It opens no window and takes no focus.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("maths-latex-roles")]
public class TexAgreementTests
{
    /// <summary>
    /// Roles both readings name the same way, and where the typesetter is entitled to nothing the parse
    /// tree does not also have.
    /// </summary>
    private static readonly string[] Comparable =
        ["numerator", "denominator", "degree", "radicand", "superscript", "subscript"];

    // over / under / cell are left out, and each for a reason of its own:
    //
    //   over    — the typesetter expands predefined symbols before building atoms, so \doteq arrives as
    //             a dot set over an equals sign and counts as one. In the source it is one symbol, and
    //             one symbol is what the parse tree has.
    //   cell    — a ragged matrix is padded out to a rectangle while it is being read, so a row written
    //             with two cells beside rows of four contributes four. The parse tree holds what was
    //             written; making it rectangular is a question for whoever is moving a column.
    //   under   — the same as over, from the same expansion.
    //   base    — not the same idea in the two readings at all. The typesetter names "base" on every
    //             internal wrapper it builds, so a formula of nine fractions has bases in its tree and
    //             none in this one, where a fraction has a numerator and a denominator and that is all.

    [TestMethod]
    public void TheParseTreeSeesEverythingTheTypesetterSees() => UiThread.Run(() =>
    {
        foreach (var (what, latex) in LatexConstructs.Everything)
        {
            var flat = LatexConstructs.Flatten(latex);
            Complain(what, flat);
        }
    });

    [TestMethod]
    public void AndSeesIntoAStyledGroupWhereTheTypesetterCannot() => UiThread.Run(() =>
    {
        // The reason the editor cannot currently do anything with a fraction someone wrote inside
        // \displaystyle: it is wrapped in a style atom, style atoms name no parts, and the walk stops
        // there. Nothing below it has a role, so it has no numerator to select, drag or copy as one.
        const string latex = @"{\displaystyle \frac{a}{b}}";

        Assert.AreEqual(0, Theirs(latex).GetValueOrDefault("numerator"),
            "the typesetter's tree cannot see into a styled group");

        Assert.AreEqual(1, Mine(latex).GetValueOrDefault("numerator"),
            "and the parse tree can");
    });

    [TestMethod]
    public void ARealCorpusAgreesTheSameWay()
    {
        // Opt-in. NEXAFLOW_LATEX_CORPUS_STRIDE samples every Nth line; the whole file is minutes rather
        // than the hours the layout sweep takes, because nothing here is drawn.
        var corpus = Environment.GetEnvironmentVariable("NEXAFLOW_LATEX_CORPUS");
        if (string.IsNullOrWhiteSpace(corpus) || !File.Exists(corpus))
            Assert.Inconclusive($"set NEXAFLOW_LATEX_CORPUS to a file of formulas (got: {corpus ?? "nothing"})");

        var stride = int.TryParse(Environment.GetEnvironmentVariable("NEXAFLOW_LATEX_CORPUS_STRIDE"), out var s)
            ? Math.Max(s, 1)
            : 1;

        var line = 0;
        var seen = 0;
        var faults = 0;
        var first = new List<string>();

        UiThread.Run(() =>
        {
            foreach (var raw in File.ReadLines(corpus))
            {
                if (line++ % stride != 0) continue;

                var latex = raw.Trim();
                if (latex.Length == 0) continue;

                seen++;
                if (Shortfall(latex) is not { } shortfall) continue;

                faults++;
                if (first.Count < 20) first.Add($"line {line}: {shortfall}\n     {latex}");
            }
        });

        Assert.IsTrue(seen > 1000, $"only {seen} formula(s) in {corpus} — is that the right file?");
        Assert.AreEqual(0, faults, $"of {seen} formulas read:\n" + string.Join("\n", first));
    }

    private static void Complain(string what, string latex)
    {
        if (Shortfall(latex) is { } shortfall) Assert.Fail($"{what}: {shortfall}\n{latex}");
    }

    /// <summary>Which role the parse tree came up short on, or nothing.</summary>
    private static string? Shortfall(string latex)
    {
        var theirs = Theirs(latex);

        // A formula the typesetter will not read is the corpus's business rather than ours: there is no
        // reading to be held against.
        if (theirs is null) return null;

        var mine = Mine(latex);

        foreach (var role in Comparable)
        {
            var found = mine.GetValueOrDefault(role);
            var expected = theirs.GetValueOrDefault(role);

            if (found < expected)
                return $"{expected} {role}(s) in the typesetter's reading, {found} in the parse tree";
        }

        return null;
    }

    private static Dictionary<string, int>? Theirs(string latex)
    {
        try
        {
            // The parser that throws rather than the one that recovers: a recovered formula is a reading
            // of what could be salvaged, which is not an oracle for anything.
            var formula = WpfTeXFormulaParser.Instance.Parse(latex);
            if (formula.Root is not { } root) return null;

            return Tally(Parts(root).SelectMany(node => node.Slots).Select(slot => slot.Role));
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, int> Mine(string latex) =>
        Tally(TexParser.Parse(latex).SelfAndDescendants().Select(node => node.Role));

    private static IEnumerable<IFormulaNode> Parts(IFormulaNode node)
    {
        yield return node;

        foreach (var slot in node.Slots)
            foreach (var inner in Parts(slot.Node))
                yield return inner;
    }

    private static Dictionary<string, int> Tally(IEnumerable<string> roles)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var role in roles) counts[role] = counts.GetValueOrDefault(role) + 1;
        return counts;
    }
}
