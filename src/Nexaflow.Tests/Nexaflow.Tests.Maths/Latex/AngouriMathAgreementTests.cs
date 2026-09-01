using AngouriMath;
using Nexaflow.Maths.Latex;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Maths.Latex;

/// <summary>
/// The parse tree held against a second, independent reading — the algebra library's.
///
/// <para>
/// The typesetter is one oracle and a compromised one: its tree is lossy by design, and its spans are
/// the very thing being replaced, so where the two disagree it is often the one that is wrong. This is
/// the other direction entirely. AngouriMath does not read LaTeX at all — it <em>writes</em> it, from an
/// expression it built itself — so for every formula here the structure is not inferred from anything.
/// It is known, exactly, before a character of LaTeX exists.
/// </para>
/// <para>
/// Which makes these the sharpest tests in the suite: a fraction is a fraction because something that
/// has never heard of this parser decided to write one.
/// </para>
/// </summary>
[TestClass]
[CoversNode("maths-latex")]
public class AngouriMathAgreementTests
{
    private static readonly string[] Expressions =
    [
        "a / b",
        "(a + b) / (c - d)",
        "1 / (1 + 1 / x)",
        "1 / (2 + 1 / (3 + 1 / 4))",
        "x ^ 2",
        "x ^ y ^ z",
        "(a + b) ^ (c + d)",
        "sqrt(x)",
        "sqrt(x + 1) / sqrt(x - 1)",
        "sin(x) + cos(y)",
        "arctan(x / y)",
        "log(2, x)",
        "abs(x - y)",
        "a * b + c * d",
        "a / b / c",
        "(x + 1) / (x - 1) + (y + 1) / (y - 1)",
    ];

    [TestMethod]
    public void EveryExpressionHereIsOneItUnderstands()
    {
        // Said out loud, so that a change of syntax in the library shows up as itself rather than as
        // every test below quietly having nothing to read.
        foreach (var written in Expressions)
            Assert.IsNotNull(MathS.FromString(written), written);
    }

    [TestMethod]
    public void WhatItWritesReadsBackExactly()
    {
        // Machine-written LaTeX is a different shape from the corpus: deeply nested fractions, brackets
        // sized with \left and \right, and never a space where a human would have put one.
        foreach (var written in Expressions)
        {
            var latex = MathS.FromString(written).Latexize();
            Assert.AreEqual(latex, TexParser.Parse(latex).Print(), written);
        }
    }

    [TestMethod]
    public void EveryFractionItWroteIsAFractionWeRead()
    {
        // Counted in the characters the library wrote, and counted again in the tree those characters
        // were read into. Every \frac it wrote has to come back as a fraction with both of its parts —
        // one short would mean an argument this parser did not find.
        //
        // Against the written characters rather than against the expression tree, deliberately. How
        // many fractions an expression turns into is the printer's business and not a fact about the
        // expression: a division is written as one, and so is a rational that is not a whole number,
        // unless that rational is an exponent, in which case it is written as a root instead. Counting
        // the expression's divisions would mean re-deciding all of that here, and getting it wrong
        // would look exactly like a parser bug.
        foreach (var written in Expressions)
        {
            var latex = MathS.FromString(written).Latexize();

            Assert.AreEqual(Occurrences(latex, @"\frac"), Complete(latex, @"\frac", TexRole.Numerator, TexRole.Denominator),
                $"{written} → {latex}");

            Assert.AreEqual(Occurrences(latex, @"\sqrt"), Complete(latex, @"\sqrt", TexRole.Radicand),
                $"{written} → {latex}");
        }
    }

    [TestMethod]
    public void AndSoDoesEverythingItCanBeMadeToWrite()
    {
        // The same two questions over a few hundred expressions grown by nesting, rather than over the
        // dozen or so anybody thought to write down. Machine-written LaTeX nests far deeper than a human
        // writes by hand, which is exactly where an argument goes missing.
        var seen = 0;
        var fractions = 0;
        var roots = 0;

        foreach (var entity in Grown())
        {
            var latex = entity.Latexize();
            seen++;

            Assert.AreEqual(latex, TexParser.Parse(latex).Print(), "did not read back");

            var wroteFractions = Occurrences(latex, @"\frac");
            var wroteRoots = Occurrences(latex, @"\sqrt");
            fractions += wroteFractions;
            roots += wroteRoots;

            Assert.AreEqual(wroteFractions, Complete(latex, @"\frac", TexRole.Numerator, TexRole.Denominator),
                $"fractions in {latex}");

            Assert.AreEqual(wroteRoots, Complete(latex, @"\sqrt", TexRole.Radicand),
                $"roots in {latex}");
        }

        // What was actually asked, said out loud. Two counts that are both zero agree with each other,
        // and a sweep that quietly stopped generating anything would read as a pass.
        Assert.IsTrue(seen > 200, $"only {seen} expression(s) were grown");
        Assert.IsTrue(fractions > 100, $"only {fractions} fraction(s) in any of them");
        Assert.IsTrue(roots > 50, $"only {roots} root(s) in any of them");
    }

    /// <summary>Expressions built by nesting a handful of leaves through a handful of operations.</summary>
    private static IEnumerable<Entity> Grown()
    {
        IReadOnlyList<Entity> level =
            [MathS.Var("x"), MathS.Var("y"), MathS.FromString("2"), MathS.FromString("a + 1")];

        for (var depth = 0; depth < 3; depth++)
        {
            var next = new List<Entity>();

            foreach (var left in level)
                foreach (var right in level)
                {
                    next.Add(left / right);
                    next.Add(MathS.Pow(left, right));
                    next.Add(MathS.Sqrt(left) + right);
                    next.Add(MathS.Sin(left) * right);
                }

            foreach (var entity in next) yield return entity;

            // Only a few forward, or the next round is the square of this one.
            level = [.. next.Take(6)];
        }
    }

    [TestMethod]
    public void AMatrixItWroteIsATableOfTheSameShape()
    {
        var matrix = MathS.Matrix(new Entity[,]
        {
            { MathS.FromString("1"), MathS.FromString("x + 1"), MathS.Var("a") },
            { MathS.FromString("y / 2"), MathS.Var("b"), MathS.FromString("z ^ 2") },
        });

        var latex = matrix.Latexize();
        var grid = TexGrid.In(TexParser.Parse(latex)).Single();

        Assert.AreEqual(matrix.RowCount, grid.RowCount, latex);
        Assert.AreEqual(matrix.ColumnCount, grid.ColumnCount, latex);
    }

    [TestMethod]
    public void AndHoldsWhatItPutInEveryCell()
    {
        // Cell by cell, against the very string the library wrote for that cell. A span a character out
        // at either end shows up here as a cell holding the wrong thing, which is what happened to every
        // matrix rewrite the editor did until the cells came from this tree.
        var matrix = MathS.Matrix(new Entity[,]
        {
            { MathS.Var("alpha"), MathS.FromString("x / y") },
            { MathS.FromString("sqrt(z)"), MathS.FromString("w ^ 2") },
        });

        var latex = matrix.Latexize();
        var grid = TexGrid.In(TexParser.Parse(latex)).Single();

        for (var row = 0; row < matrix.RowCount; row++)
            for (var column = 0; column < matrix.ColumnCount; column++)
            {
                var cell = grid[row, column];
                Assert.AreEqual(matrix[row, column].Latexize(), latex.Substring(cell.Start, cell.Length),
                    $"cell {row},{column} of {latex}");
            }
    }

    /// <summary>How many times this command is written in the source, as a command and not as letters.</summary>
    private static int Occurrences(string latex, string command)
    {
        var found = 0;

        for (var at = latex.IndexOf(command, StringComparison.Ordinal); at >= 0;
             at = latex.IndexOf(command, at + 1, StringComparison.Ordinal))
        {
            var after = at + command.Length;
            if (after >= latex.Length || !char.IsLetter(latex[after])) found++;
        }

        return found;
    }

    /// <summary>How many of them the tree read as that command with every one of these parts.</summary>
    private static int Complete(string latex, string command, params string[] roles) =>
        TexParser.Parse(latex).SelfAndDescendants().Count(
            node => node.Kind == TexKind.Command
                    && node.Part(TexRole.Name)?.Text == command
                    && roles.All(role => node.Part(role) is not null));
}
