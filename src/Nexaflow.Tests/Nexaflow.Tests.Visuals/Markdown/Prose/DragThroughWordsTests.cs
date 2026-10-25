using System;
using System.Linq;
using System.Windows;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown.Prose;

/// <summary>
/// A drag through words picks out what lies between where it was pressed and where the pointer is, whatever it passes over on the
/// way — a formula, the end of a line, a word in bold. The words at either end are taken from the letter pointed at; a formula
/// the pointer is over is taken as far as the symbol it reached, since half a symbol cannot be read.
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("markdown-text")]
public class DragThroughWordsTests
{
    private const string Item =
        "- Im Fall $m = 0$ und $x_0 < x$ besagt der Satz, dass es ein $\\xi \\in ]x_0, x[$ gibt so, dass\n";

    private const string Paragraph =
        "Equations in which derivatives appear describe numerous laws of nature. The sought-after function plays, for "
        + "example, the role of a physical or chemical quantity that depends on time. **Bold words** and more words after them.\n";

    [TestMethod]
    public void ADragFromAWordStartsWhereItWasPressed_WhateverItReaches() => UiThread.Run(() =>
    {
        var element = Element(Item);
        var fall = Item.IndexOf("Fall", StringComparison.Ordinal);

        element.BeginPointerSelect(Before(element, fall));

        foreach (var (reached, inFormula) in new[] { ("m = 0", true), ("und", false), ("x_0 < x", true), ("besagt", false),
                                                     ("\\xi", true), ("gibt", false), ("so, dass", false) })
        {
            var at = Item.IndexOf(reached, StringComparison.Ordinal);
            var to = Within(element, at, reached.Length);

            element.ExtendPointerSelect(to);

            var chosen = element.Selection.Single();
            var said = Item.Substring(chosen.Start, chosen.Length);
            Assert.AreEqual(fall, chosen.Start, $"reaching '{reached}' chose '{said}', which does not start where the drag did");

            if (inFormula)
                Assert.IsTrue(chosen.Start + chosen.Length > at && chosen.Start + chosen.Length <= at + reached.Length + 1,
                    $"reaching into '{reached}' chose '{said}', not as far as the symbol reached");
            else
                Assert.AreEqual(element.Laid.OffsetAt(to), chosen.Start + chosen.Length, $"reaching '{reached}' chose '{said}'");
        }

        element.EndPointerSelect();
    });

    [TestMethod]
    public void ADragAcrossLinesAndMarks_IsTheLettersFromOneEndToTheOther() => UiThread.Run(() =>
    {
        foreach (var (from, to) in new[] { ("which", "depends"), ("sought", "Bold"), ("time", "more") })
        {
            var element = Element(Paragraph);
            var (start, end) = (Paragraph.IndexOf(from, StringComparison.Ordinal) + 2, Paragraph.IndexOf(to, StringComparison.Ordinal) + 2);

            Sweep(element, Before(element, start), Before(element, end));

            Assert.AreEqual((start, end - start), element.Selection.Single(),
                $"{from} to {to} chose '{Paragraph.Substring(element.SelectionStart, element.SelectionLength)}'");
        }
    });

    [TestMethod]
    public void ADragBackwards_EndsWhereItWasPressed() => UiThread.Run(() =>
    {
        var element = Element(Paragraph);
        var (start, end) = (Paragraph.IndexOf("which", StringComparison.Ordinal) + 2, Paragraph.IndexOf("depends", StringComparison.Ordinal) + 2);

        Sweep(element, Before(element, end), Before(element, start));

        Assert.AreEqual((start, end - start), element.Selection.Single());
    });

    [TestMethod]
    public void LettersChosenFromOneWordToAnother_AreThoseLetters() => UiThread.Run(() =>
    {
        // Not only a drag: whatever chooses a stretch of words — Shift and an arrow among them — keeps its letters.
        var element = Element(Item);
        var (start, end) = (Item.IndexOf("Fall", StringComparison.Ordinal) + 1, Item.IndexOf("besagt", StringComparison.Ordinal) + 3);

        element.Select(start, end - start);

        Assert.AreEqual((start, end - start), (element.SelectionStart, element.SelectionLength));
    });

    [TestMethod]
    public void ADragFromOneCellOfATableToAnother_IsStillTheColumnBetween() => UiThread.Run(() =>
    {
        const string table = "| alpha | beta |\n|---|---|\n| one | two |\n| three | four |\n";
        var element = Element(table);

        Sweep(element, Before(element, table.IndexOf("alpha", StringComparison.Ordinal) + 1),
                       Before(element, table.IndexOf("three", StringComparison.Ordinal) + 2));

        var chosen = element.Selection.Select(range => table.Substring(range.Start, range.Length)).ToList();
        var said = string.Join(" | ", chosen);

        foreach (var cell in new[] { "alpha", "one", "three" }) Assert.IsTrue(chosen.Any(text => text.Contains(cell)), $"the column holds {cell}: {said}");
        foreach (var cell in new[] { "beta", "two" }) Assert.IsFalse(chosen.Any(text => text.Contains(cell)), $"and not {cell}: {said}");
    });

    private static MarkdownElement Element(string source)
    {
        var element = new MarkdownElement(source, StyleFormat.Dark);

        element.Measure(new Size(360, double.PositiveInfinity));
        element.Arrange(new Rect(0, 0, 360, element.DesiredSize.Height));
        element.UpdateLayout();

        return element;
    }

    /// <summary>Just inside the left edge of the character at <paramref name="offset"/>: the place before it.</summary>
    private static Point Before(MarkdownElement element, int offset)
    {
        var drawn = element.Laid.Root.RangeRects(offset, 1)[0];
        return new Point(drawn.Left + 0.5, drawn.Y + (drawn.Height / 2));
    }

    /// <summary>A little past half way along what is drawn for the stretch at <paramref name="offset"/>.</summary>
    private static Point Within(MarkdownElement element, int offset, int length)
    {
        var drawn = element.Laid.Root.RangeRects(offset, length)[0];
        return new Point(drawn.X + (drawn.Width * 0.6), drawn.Y + (drawn.Height / 2));
    }

    /// <summary>A press, a drag and a release.</summary>
    private static void Sweep(MarkdownElement element, Point from, Point to)
    {
        element.BeginPointerSelect(from);
        element.ExtendPointerSelect(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        element.ExtendPointerSelect(to);
        element.EndPointerSelect();
    }
}
