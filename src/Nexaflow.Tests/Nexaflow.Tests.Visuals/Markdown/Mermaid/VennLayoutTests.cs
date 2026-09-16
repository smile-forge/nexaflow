using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Circle = Nexaflow.Visuals.Text.Markdown.Mermaid.Venn.VennLayout.Circle;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Venn;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Where a Venn diagram's circles go: areas that are their sets' sizes, overlaps that are their unions' — the region three
/// sets share included — and how much a set of circles covers together, which is what all of that is measured by.
/// </summary>
[TestClass]
[CoversNode("venn")]
public class VennLayoutTests
{
    /// <summary>How much every one of the circles covers, counted a grid square at a time — slow, and not the same arithmetic.</summary>
    private static double Counted(IReadOnlyList<Circle> circles)
    {
        var box = circles.Select(circle => circle.Bounds).Aggregate(Rect.Intersect);
        if (box.IsEmpty) return 0;

        const int Across = 800;
        var cell = Math.Max(box.Width, box.Height) / Across;
        var inside = 0;

        for (var x = box.X + (cell / 2); x < box.Right; x += cell)
            for (var y = box.Y + (cell / 2); y < box.Bottom; y += cell)
                if (circles.All(circle => (new Point(x, y) - circle.Centre).Length <= circle.Radius)) inside++;

        return inside * cell * cell;
    }

    [TestMethod]
    public void WhatTwoCirclesShareIsTheirLens()
    {
        var (one, other) = (new Circle(new Point(0, 0), 2), new Circle(new Point(2.5, 0), 1.5));

        Assert.AreEqual(VennLayout.Lens(2, 1.5, 2.5), VennLayout.Shared([one, other]), 1e-9);
    }

    [TestMethod]
    public void WhatThreeCirclesShareIsWhatTheyAllCover()
    {
        foreach (var circles in new Circle[][]
                 {
                     [new(new Point(0, 0), 1), new(new Point(1, 0), 1), new(new Point(0.5, 0.85), 1)],
                     [new(new Point(0, 0), 2), new(new Point(1.8, 0.4), 1.4), new(new Point(0.6, 1.9), 1.1)],
                     [new(new Point(0, 0), 1), new(new Point(1.5, 0), 1), new(new Point(0.75, 1.2), 1), new(new Point(0.75, 0.3), 0.8)],
                 })
        {
            var counted = Counted(circles);
            Assert.AreEqual(counted, VennLayout.Shared(circles), counted * 0.01, $"{circles.Length} circles");
        }
    }

    [TestMethod]
    public void CirclesThatDoNotAllMeetShareNothing_AndOneInsideTheRestSharesItself()
    {
        Circle[] apart = [new(new Point(0, 0), 1), new(new Point(1.5, 0), 1), new(new Point(5, 0), 1)];
        Assert.AreEqual(0, VennLayout.Shared(apart));

        Circle[] nested = [new(new Point(0, 0), 3), new(new Point(0.5, 0), 3), new(new Point(0.2, 0.2), 0.5)];
        Assert.AreEqual(Math.PI * 0.25, VennLayout.Shared(nested), 1e-9);
    }

    [TestMethod]
    public void AUnionOfThreeSetsIsFittedToTheSizeWrittenForIt()
    {
        double[] areas = [10, 10, 10];
        static double Pairs(int one, int other) => 2.5;
        IReadOnlyList<int> all = [0, 1, 2];

        var pairsOnly = VennLayout.Place(areas, Pairs);

        foreach (var size in new[] { 0.3, 1.8 })
        {
            var fitted = VennLayout.Place(areas, Pairs, [(all, size)]);

            Assert.IsTrue(Math.Abs(VennLayout.Shared(fitted) - size) < Math.Abs(VennLayout.Shared(pairsOnly) - size),
                          $"fitted to {size}, the three share {VennLayout.Shared(fitted):0.00}; fitted to pairs alone, {VennLayout.Shared(pairsOnly):0.00}");
        }

        var little = VennLayout.Shared(VennLayout.Place(areas, Pairs, [(all, 0.3)]));
        var more = VennLayout.Shared(VennLayout.Place(areas, Pairs, [(all, 1.8)]));
        Assert.IsTrue(more > little, $"a bigger size written is a bigger region: {little:0.00} then {more:0.00}");
    }
}
