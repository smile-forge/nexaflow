using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Latex;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Maths.Latex;

/// <summary>
/// Moving cells of a matrix: what each move writes, and — the point of writing cells rather than reprinting a body —
/// what it never writes at all.
///
/// <para>
/// The stretches a move names are the cells' own contents. So the spaces, the line breaks and the <c>&amp;</c> that a
/// writer laid out by hand are not in any answer: they are still the characters they typed, wherever the contents
/// moved. The columns will no longer line up, because what is written in them is a different width — but that is the
/// writer's spacing left alone rather than a body regenerated, which is what used to come back correct and
/// unrecognisable.
/// </para>
/// </summary>
[TestClass]
[CoversNode("latex-grid-move")]
public class TexMoveTests
{
    private const string Matrix = @"\begin{matrix} a & b & c \\ d & e & f \end{matrix}";
    private const string Square = @"\begin{matrix} a & b & c \\ d & e & f \\ g & h & i \end{matrix}";

    [TestMethod]
    public void AColumnCarriedOntoAnotherTakesItsPlaceAndTheRestShiftOver()
    {
        // The first column let go on the last: it goes in after that one, and the two it passed close up.
        Assert.AreEqual(@"\begin{matrix} b & c & a \\ e & f & d \end{matrix}",
                        Moved(Matrix, Column(Matrix, 0), Onto(Matrix, "c")));
    }

    [TestMethod]
    public void AndCarriedBackwardsItGoesInFrontOfWhatItWasLetGoOn()
    {
        Assert.AreEqual(@"\begin{matrix} c & a & b \\ f & d & e \end{matrix}",
                        Moved(Matrix, Column(Matrix, 2), Onto(Matrix, "a")));
    }

    [TestMethod]
    public void ARowCarriedOntoAnotherReordersTheRows()
    {
        Assert.AreEqual(@"\begin{matrix} d & e & f \\ a & b & c \end{matrix}",
                        Moved(Matrix, Row(Matrix, 0), Onto(Matrix, "d")));
    }

    [TestMethod]
    public void ABlockOfCellsMovesItsContentsAndLeavesTheOnesItCameFromEmpty()
    {
        // Four cells carried a row down and a column across. Where they land is written over; where they came from is
        // left empty, which is a hole in the parse and nothing at all in the source.
        Assert.AreEqual(@"\begin{matrix}  &  & c \\  & a & b \\ g & d & e \end{matrix}",
                        Moved(Square, Cells(Square, "a", "b", "d", "e"), Onto(Square, "e")));
    }

    [TestMethod]
    public void AndNoMoveOfThemEverWritesTheSpacingAWriterLaidOutByHand()
    {
        const string laid = "\\begin{matrix}\n  alpha & b     \\\\\n  c     & delta \\end{matrix}";

        Assert.AreEqual("\\begin{matrix}\n  b & alpha     \\\\\n  delta     & c \\end{matrix}",
                        Moved(laid, Column(laid, 0), Onto(laid, "b")),
                        "every newline, every indent and every run of spaces is the one that was typed");
    }

    [TestMethod]
    public void ACarryLetGoOnItselfIsNoMoveAtAll()
    {
        Assert.IsNull(Dropped(Matrix, Column(Matrix, 1), Onto(Matrix, "b")));
        Assert.IsNull(Dropped(Matrix, Row(Matrix, 0), Onto(Matrix, "b")), "and a row let go inside itself is the same");
    }

    [TestMethod]
    public void AndSoIsCarryingTheWholeTable()
    {
        Assert.IsNull(Dropped(Matrix, Cells(Matrix, "a", "b", "c", "d", "e", "f"), Onto(Matrix, "a")));
    }

    [TestMethod]
    public void WhatIsNotWholeCellsIsNoMoveOfCells()
    {
        // Characters carried are characters moving, which the engine does anyway — so the formula says nothing about
        // it and the engine's own answer stands.
        var inside = Onto(Matrix, "b");

        Assert.IsNull(Dropped(Matrix, [new EditRange(inside, 0)], Onto(Matrix, "c")), "no cell covered whole");
        Assert.IsNull(Dropped("a + b", [new EditRange(0, 1)], 4), "and a formula with no table in it at all");
    }

    [TestMethod]
    public void AnLOfCellsIsNoBlockToMove()
    {
        // Three cells that make no rectangle: there is nowhere for them to land as a shape, so the move is declined
        // rather than guessed at.
        Assert.IsNull(Dropped(Matrix, Cells(Matrix, "a", "b", "d"), Onto(Matrix, "f")));
    }

    [TestMethod]
    public void ARaggedTableIsDeclinedRatherThanGuessedAt()
    {
        // A short row is squared off so that "the third column" means the same in every row, and the cell that adds
        // stands where a third cell would begin — with no & in front of it, because nobody wrote one. Writing there
        // would make 'd & e f' of the row, so the move is not made.
        const string ragged = @"\begin{matrix} a & b & c \\ d & e \end{matrix}";

        Assert.IsNull(Dropped(ragged, Column(ragged, 0), Onto(ragged, "c")));
    }

    [TestMethod]
    public void ABlockCarriedOutOfTheTableBecomesATableOfItsOwn()
    {
        const string beside = @"\begin{matrix} a & b \\ c & d \end{matrix} + z";

        Assert.AreEqual(@"\begin{matrix}  & b \\  & d \end{matrix} + z\begin{matrix} a \\ c \end{matrix}",
                        Moved(beside, Column(beside, 0), beside.Length),
                        "the same kind of table, at the size carried, and the cells it came from left empty");
    }

    [TestMethod]
    public void EveryMoveLeavesAFormulaThatStillReadsBackAndStillSaysWhereItCameFrom()
    {
        // The contract every answer is held to, the same one an edit is: what the engine writes is read again, so a
        // move must never write something the parser will not give back unchanged.
        foreach (var latex in new[] { Matrix, Square })
            foreach (var carried in Everything(latex))
                foreach (var cell in TexGrid.At(Read(latex), Onto(latex, "a"))!.Cells)
                {
                    if (TexMove.Dropped(Read(latex), carried, cell.Start) is not { } change) continue;

                    var made = Made(latex, change);

                    Assert.AreEqual(made, TexParser.Parse(made).Print(), made);
                    Assert.AreEqual(0, AstOracle.Faults(made, TexParser.Parse(made)).Count(), made);
                }
    }

    // ── What the fixtures mean ──────────────────────────────────────────────

    private static ContentPart Read(string latex) => ContentPart.Of(TexParser.Parse(latex));

    /// <summary>The table in a formula, positioned as a document would hold it.</summary>
    private static TexGrid Grid(string latex)
    {
        var environment = Read(latex).SelfAndDescendants().First(part => part.Kind == TexKinds.Environment);

        return TexGrid.Read(environment.Node, environment.Start)!;
    }

    /// <summary>The stretches a drag down a column picks out — one per cell, which is what a grid selection gives.</summary>
    private static IReadOnlyList<EditRange> Column(string latex, int column)
    {
        var grid = Grid(latex);

        return [.. Enumerable.Range(0, grid.RowCount).Select(row => Ranged(grid[row, column]))];
    }

    /// <summary>And across a row.</summary>
    private static IReadOnlyList<EditRange> Row(string latex, int row)
    {
        var grid = Grid(latex);

        return [.. Enumerable.Range(0, grid.ColumnCount).Select(column => Ranged(grid[row, column]))];
    }

    /// <summary>The cells holding each of <paramref name="said"/>, found by what is written in them.</summary>
    private static IReadOnlyList<EditRange> Cells(string latex, params string[] said)
    {
        var grid = Grid(latex);

        return [.. said.Select(what => Ranged(grid.Cells.First(cell => Written(latex, cell) == what)))];
    }

    /// <summary>Every carry worth trying on a formula: each whole column, each whole row, and each pair of cells.</summary>
    private static IEnumerable<IReadOnlyList<EditRange>> Everything(string latex)
    {
        var grid = Grid(latex);

        for (var column = 0; column < grid.ColumnCount; column++) yield return Column(latex, column);
        for (var row = 0; row < grid.RowCount; row++) yield return Row(latex, row);

        yield return [Ranged(grid[0, 0]), Ranged(grid[0, 1])];
        yield return [Ranged(grid[0, 0]), Ranged(grid[1, 0])];
    }

    /// <summary>Where the cell holding <paramref name="said"/> begins, which is where a drop on it lands.</summary>
    private static int Onto(string latex, string said) =>
        Grid(latex).Cells.First(cell => Written(latex, cell) == said).Start;

    private static string Written(string latex, TexCell cell) => latex.Substring(cell.Start, cell.Length);

    private static EditRange Ranged(TexCell cell) => new(cell.Start, cell.Length);

    private static ContentChange? Dropped(string latex, IReadOnlyList<EditRange> carried, int to) =>
        TexMove.Dropped(Read(latex), carried, to);

    /// <summary>The formula with every stretch a move names written over — what the engine does with one.</summary>
    private static string Moved(string latex, IReadOnlyList<EditRange> carried, int to)
    {
        var change = Dropped(latex, carried, to);

        Assert.IsNotNull(change, $"the move was declined: {latex}");
        return Made(latex, change);
    }

    private static string Made(string latex, ContentChange change)
    {
        foreach (var write in change.Writes.OrderByDescending(write => write.Start))
            latex = string.Concat(latex.AsSpan(0, write.Start), write.Text, latex.AsSpan(write.End));

        return latex;
    }
}
