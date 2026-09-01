using Nexaflow.Maths.Latex;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Maths.Latex;

/// <summary>
/// That the tree is a tree.
///
/// <para>
/// Reading a formula back exactly as it was written is necessary and not sufficient — a parser that
/// handed back one undigested leaf would manage it. These are the other half: the parts are there, they
/// are the parts the writer meant, and where each one falls in the printed source is worked out by the
/// tree rather than remembered from the parse.
/// </para>
/// </summary>
[TestClass]
[CoversNode("maths-latex")]
public class TexTreeTests
{
    [TestMethod]
    public void AFractionHasANumeratorAndADenominator()
    {
        var fraction = Only(@"\frac{a}{b}");

        Assert.AreEqual(TexKind.Command, fraction.Kind);
        Assert.AreEqual(@"\frac", fraction.Part(TexRole.Name)?.Text);
        Assert.AreEqual("{a}", fraction.Part(TexRole.Numerator)?.Print());
        Assert.AreEqual("{b}", fraction.Part(TexRole.Denominator)?.Print());
    }

    [TestMethod]
    public void AnUnbracedArgumentIsOneTokenAndNoMore()
    {
        // \frac12 is a half. TeX takes one token per argument when there are no braces, which is why
        // this has to be read the same way and not as "everything until something stops it".
        var fraction = Only(@"\frac12");

        Assert.AreEqual("1", fraction.Part(TexRole.Numerator)?.Print());
        Assert.AreEqual("2", fraction.Part(TexRole.Denominator)?.Print());
    }

    [TestMethod]
    public void AWholeCommandCanStandAsAnArgument()
    {
        // x^\alpha needs no braces, because a control word is one token.
        var script = Only(@"x^\alpha");

        Assert.AreEqual(TexKind.Script, script.Kind);
        Assert.AreEqual("x", script.Part(TexRole.Base)?.Print());
        Assert.AreEqual(@"\alpha", script.Part(TexRole.Superscript)?.Print());
    }

    [TestMethod]
    public void BothScriptsBelongToTheSameBase()
    {
        // Not a script wrapping a script: the x has two things attached to it, and each is a part of the
        // same one thing.
        var script = Only("x^2_i");

        Assert.AreEqual(TexKind.Script, script.Kind);
        Assert.AreEqual("x", script.Part(TexRole.Base)?.Print());
        Assert.AreEqual("2", script.Part(TexRole.Superscript)?.Print());
        Assert.AreEqual("i", script.Part(TexRole.Subscript)?.Print());
    }

    [TestMethod]
    public void APrimeBelongsToWhatItIsWrittenOn()
    {
        // `f''` is one thing, not three standing beside each other — one thing to select, to drag and to
        // delete. And the marks stay marks: naming them `superscript` would be saying what they draw as,
        // which is a reading and not a fact about what was written.
        var script = Only("f''");

        Assert.AreEqual(TexKind.Script, script.Kind);
        Assert.AreEqual("f", script.Part(TexRole.Base)?.Print());
        Assert.AreEqual(2, script.Children.Count(child => child.Role == TexRole.Mark));
        Assert.AreEqual("f''", script.Print());
    }

    [TestMethod]
    public void AScriptAfterAPrimeLandsOnWhatThePrimeIsOn()
    {
        // The reason the marks belong to the base rather than wrapping it. Written as a prime standing
        // between the x and the `_`, the subscript would otherwise be read as the prime's — and `x''_{i}`
        // would draw the i under a prime instead of under the x.
        var script = Only("x''_{i}");

        Assert.AreEqual(TexKind.Script, script.Kind);
        Assert.AreEqual("x", script.Part(TexRole.Base)?.Print());
        Assert.AreEqual(2, script.Children.Count(child => child.Role == TexRole.Mark));
        Assert.AreEqual("{i}", script.Part(TexRole.Subscript)?.Print());
    }

    [TestMethod]
    public void NothingIsWrittenOnATie()
    {
        // A script attaches to the atom before it, and a tie is a space written as a character. So the
        // `^{b}` of `a~^{b}` starts a base of its own and the tie stands where it was written.
        var run = TexParser.Parse("a~^{b}");

        Assert.AreEqual(3, run.Children.Count);
        Assert.AreEqual("~", run.Children[1].Print());
        Assert.AreEqual(TexKind.Script, run.Children[2].Kind);
        Assert.IsNull(run.Children[2].Part(TexRole.Base));
    }

    [TestMethod]
    public void ASpaceBeforeAScriptBelongsToTheScript()
    {
        // `x ^2` is x-to-the-2 with a space in the middle of the writing of it. The space is inside the
        // script, because that is where it was written, and it is not between two separate things.
        var script = Only("x ^2");

        Assert.AreEqual(TexKind.Script, script.Kind);
        Assert.AreEqual("x ^2", script.Print());
    }

    [TestMethod]
    public void ARootsDegreeIsTheThingInBrackets()
    {
        var root = Only(@"\sqrt[3]{y+1}");

        Assert.AreEqual("[3]", root.Part(TexRole.Degree)?.Print());
        Assert.AreEqual("{y+1}", root.Part(TexRole.Radicand)?.Print());
    }

    [TestMethod]
    public void ARootWithoutADegreeStillHasItsRadicand()
    {
        var root = Only(@"\sqrt{x}");

        Assert.IsNull(root.Part(TexRole.Degree));
        Assert.AreEqual("{x}", root.Part(TexRole.Radicand)?.Print());
    }

    [TestMethod]
    public void ABracedGroupKeepsItsBraces()
    {
        // The braces are children of the group rather than something inferred from the characters
        // around it. That is what lets anything ask "did the writer brace this?" without going back to
        // the source to look.
        var group = Only("{a+b}");

        Assert.AreEqual(TexKind.Group, group.Kind);
        Assert.AreEqual("{", group.Part(TexRole.Open)?.Text);
        Assert.AreEqual("}", group.Part(TexRole.Close)?.Text);
        Assert.AreEqual(3, group.Parts(TexRole.Element).Count(), "a, +, b");
    }

    [TestMethod]
    public void AFenceHoldsItsOwnDelimiters()
    {
        var fence = Only(@"\left( a \right)");

        Assert.AreEqual(TexKind.Fence, fence.Kind);
        Assert.AreEqual(@"\left(", fence.Part(TexRole.Open)?.Print());
        Assert.AreEqual(@"\right)", fence.Part(TexRole.Close)?.Print());
    }

    [TestMethod]
    public void AMatrixIsRowsOfCells()
    {
        var matrix = Only(@"\begin{matrix} 1 & 2 & 3 \\ a & b & c \end{matrix}");

        Assert.AreEqual(TexKind.Environment, matrix.Kind);
        Assert.AreEqual("matrix", TexParser.NameOf(matrix.Part(TexRole.Begin)!));

        var rows = matrix.Parts(TexRole.Row).ToList();
        Assert.AreEqual(2, rows.Count);

        foreach (var row in rows)
            Assert.AreEqual(3, row.Parts(TexRole.Cell).Count(), $"in {row.Print()}");

        Assert.AreEqual(" 1 ", rows[0].Parts(TexRole.Cell).First().Print().TrimEnd('&'));
    }

    [TestMethod]
    public void ASeparatorSitsInTheTreeWhereItWasWritten()
    {
        // Which is the whole reason a column can be moved. A separator that only existed in the source
        // would have to be counted out of the characters every time, and reinserted by hand.
        var matrix = Only(@"\begin{matrix} 1 & 2 \\ a & b \end{matrix}");
        var rows = matrix.Parts(TexRole.Row).ToList();

        Assert.AreEqual("&", rows[0].Parts(TexRole.Cell).First().Part(TexRole.Separator)?.Text);
        Assert.IsNull(rows[0].Parts(TexRole.Cell).Last().Part(TexRole.Separator),
            "the last cell of a row ends with the row, not with an ampersand");
        Assert.AreEqual(@"\\", rows[0].Part(TexRole.Separator)?.Print());
        Assert.IsNull(rows[1].Part(TexRole.Separator), "and the last row ends with the environment");
    }

    [TestMethod]
    public void ATrailingLineBreakEndsTheLastRowRatherThanStartingAnother()
    {
        // A \\ finishes the line it is written on. Reading it as the start of the next one would give
        // every matrix written that way a blank row nobody typed.
        var matrix = Only(@"\begin{matrix} a & b \\ \end{matrix}");

        Assert.AreEqual(1, matrix.Parts(TexRole.Row).Count());
    }

    [TestMethod]
    public void AnArraysColumnSpecIsNotOneOfItsCells()
    {
        // \begin{array}{cc} — the {cc} says how the columns are set, and reading it as content would
        // put it in the first cell.
        var array = Only(@"\begin{array}{cc} a & b \end{array}");

        Assert.AreEqual("{cc}", array.Part(TexRole.Option)?.Print());
        Assert.AreEqual(2, array.Parts(TexRole.Row).First().Parts(TexRole.Cell).Count());
    }

    [TestMethod]
    public void AnUnknownCommandTakesNothingWithIt()
    {
        // The right default. Nothing is lost — the group after it is still a group, still readable,
        // still printable — the tree is only flatter than it would be if the table knew the command.
        var root = TexParser.Parse(@"\notacommand{x}");

        Assert.AreEqual(2, root.Children.Count);
        Assert.AreEqual(TexKind.Command, root.Children[0].Kind);
        Assert.AreEqual(TexKind.Group, root.Children[1].Kind);
    }

    [TestMethod]
    public void WhereEveryPartFallsIsWorkedOutRatherThanRemembered()
    {
        // No node stores an offset. Asking where one starts walks the widths, so a tree that has been
        // edited cannot hand back a position from before the edit — there are none to go stale.
        foreach (var (what, latex) in LatexConstructs.Everything)
        {
            var flat = LatexConstructs.Flatten(latex);
            var root = TexParser.Parse(flat);

            foreach (var place in root.Placed())
                Assert.AreEqual(flat.Substring(place.Start, place.Node.Width), place.Node.Print(),
                    $"{what}: {place.Node.Kind} at {place.Start}");
        }
    }

    [TestMethod]
    public void APartMovedSomewhereElseIsTheSamePart()
    {
        // Immutability earning its keep: re-rooting a subtree reuses it rather than copying it, which is
        // what will let a matrix be rewritten without reprinting the cells nobody touched.
        var fraction = Only(@"\frac{a}{b}");
        var numerator = fraction.Part(TexRole.Numerator)!;

        var moved = numerator.As(TexRole.Denominator);

        Assert.AreEqual("{a}", moved.Print());
        Assert.AreEqual(TexRole.Denominator, moved.Role);
        Assert.AreSame(numerator.Children[0], moved.Children[0], "its parts were not rebuilt");
    }

    [TestMethod]
    public void TheCorpusIsReadAsStructureAndNotAsText()
    {
        // The antidote to a round-trip that passes for the wrong reason. A parser that had quietly
        // degenerated into "hold everything verbatim" would read a quarter of a million real formulas
        // back perfectly and fail this on the first one that contains a fraction.
        var corpus = Environment.GetEnvironmentVariable("NEXAFLOW_LATEX_CORPUS");
        if (string.IsNullOrWhiteSpace(corpus) || !File.Exists(corpus))
            Assert.Inconclusive($"set NEXAFLOW_LATEX_CORPUS to a file of formulas (got: {corpus ?? "nothing"})");

        var seen = 0;
        var missed = new List<string>();

        foreach (var raw in File.ReadLines(corpus))
        {
            var latex = raw.Trim();
            // \frac, not \frac{ — the corpus is tokenised, so a fraction is written `\frac { a } { b }`
            // with a space before every brace. Which is itself worth having read: a command and its
            // argument are routinely written apart, and the space between them belongs to the command.
            if (!latex.Contains(@"\frac", StringComparison.Ordinal)) continue;

            seen++;
            var whole = TexParser.Parse(latex).SelfAndDescendants()
                .Any(node => node.Kind == TexKind.Command
                             && node.Part(TexRole.Name)?.Text == @"\frac"
                             && node.Part(TexRole.Numerator) is not null
                             && node.Part(TexRole.Denominator) is not null);

            if (!whole && missed.Count < 10) missed.Add(latex);
        }

        Assert.IsTrue(seen > 100, $"only {seen} formula(s) with a fraction in {corpus}");
        Assert.AreEqual(0, missed.Count,
            $"of {seen} formulas written with a fraction:\n" + string.Join("\n", missed));
    }

    /// <summary>The one thing the formula is made of, ignoring the space around it.</summary>
    private static TexNode Only(string latex)
    {
        var content = TexParser.Parse(latex).Children
            .Where(child => child.Kind is not (TexKind.Space or TexKind.Comment))
            .ToList();

        Assert.AreEqual(1, content.Count, $"expected one thing, got: {string.Join(", ", content)}");
        return content[0];
    }
}
