using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown.Prose;

/// <summary>
/// The lines a table is ruled with: over and under every row and beside every column, and never through a cell written
/// to cover the square on the other side.
/// </summary>
[TestClass]
[CoversNode("markdown-table-spans")]
public class MarkdownTableRulesTests
{
    [TestMethod]
    public void ATableIsRuledOverAndUnderEveryRowAndBesideEveryColumn()
    {
        var (across, down, table) = Rules(MarkdownBuilder.Lay("| a | b |\n|---|---|\n| c | d |\n", StyleFormat.Dark, 480));

        var levels = across.GroupBy(rule => Math.Round(rule.Y, 1)).ToList();
        var edges = down.GroupBy(rule => Math.Round(rule.X, 1)).ToList();

        Assert.AreEqual(3, levels.Count, "over the head, between the rows, and under the last");
        Assert.AreEqual(3, edges.Count, "left, between the columns, and right");

        foreach (var level in levels) Assert.AreEqual(table.Width, level.Sum(rule => rule.Width), 0.5, $"the line at {level.Key} runs the whole way across");
        foreach (var edge in edges) Assert.AreEqual(table.Height, edge.Sum(rule => rule.Height), 0.5, $"the line at {edge.Key} runs the whole way down");
    }

    [TestMethod]
    public void NoLineIsDrawnThroughACellCoveringTwoColumns()
    {
        var laid = MarkdownBuilder.Lay("+-------+------+\n| Big          |\n+=======+======+\n| a     | b    |\n+-------+------+\n",
                                       StyleFormat.Dark, 480);
        var (_, down, table) = Rules(laid);

        var inner = down.Where(rule => rule.X > table.Left + 1 && rule.X < table.Right - 2).ToList();
        var big = laid.Root.SelfAndDescendants().First(piece => piece.Kind == MarkdownPieces.Cell).Bounds;

        Assert.IsTrue(inner.Count > 0, "the columns are ruled apart where they are two");
        Assert.IsFalse(inner.Any(rule => rule.Top < big.Bottom - 0.5), "but not through the cell covering both");
    }

    /// <summary>Every rule of the one table laid, where it is on the page, split into those across and those down, and where the table is.</summary>
    private static (List<Rect> Across, List<Rect> Down, Rect Table) Rules(Laid laid)
    {
        var rules = new List<Rect>();

        foreach (var piece in laid.Root.SelfAndDescendants())
        {
            var origin = piece.Offset;
            foreach (var up in piece.Ancestors()) origin += up.Offset;

            foreach (var mark in piece.Marks)
                if (mark is RuleMark rule) rules.Add(Rect.Offset(rule.Bounds, origin));
        }

        List<Rect> across = [.. rules.Where(rule => rule.Width > rule.Height)];
        List<Rect> down = [.. rules.Where(rule => rule.Height >= rule.Width)];

        // How wide from the lines across it and how tall from the lines down it: each kind of line is as thick as it is,
        // and counting that thickness in the other direction would make the table a line wider than its rules.
        var wide = across.Aggregate(Rect.Empty, (all, rule) => Rect.Union(all, rule));
        var tall = down.Aggregate(Rect.Empty, (all, rule) => Rect.Union(all, rule));

        Assert.IsFalse(wide.IsEmpty, "the table is ruled across");
        Assert.IsFalse(tall.IsEmpty, "the table is ruled down");

        return (across, down, new Rect(wide.Left, tall.Top, wide.Width, tall.Height));
    }
}
