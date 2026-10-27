using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// Source shown as written is a piece for each letter, standing where the type engine draws that letter. The engine is the
/// reference: the same line set as one run, asked where the letter is highlighted. Each line is set once and its letters read
/// off that setting, so these are the cases where reading off a setting could part company with asking the run: tabs, line
/// endings, ligatures, a face the letter falls back from, letters made of several characters, and text running the other way.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("layout-words-source-letters")]
public class SourceLettersTests
{
    private static readonly string[] Written =
    [
        "flowchart LR\n  A --> B ==> C\n  C != D >= E :: F",
        "a\tb\t\tc\n\tindented\nx\ty",
        "one\r\ntwo\r\n\r\nthree",
        "trailing   \n    \n\nend",
        "graph 图表 データ 한국어 end",
        "ok 😀 👍🏽 family 👨‍👩‍👧 done",
        "cafe\u0301 n\u0303 a\u0308\u0304 x",
        "label שלום עולם end\nnode مرحبا بالعالم end\nA 123 אבג 456 B",
        string.Concat(Enumerable.Repeat("word-> ", 43)),
    ];

    private static readonly (Typeface Face, double Size)[] Settings =
    [
        (new Typeface("Cascadia Code, Consolas"), 13),
        (new Typeface("Segoe UI"), 15),
    ];

    [TestMethod]
    public void EveryLetterStandsWhereTheTypeEngineDrawsIt() => UiThread.Run(() =>
    {
        foreach (var setting in Settings)
        foreach (var written in Written)
        {
            var tree = ContentPart.Of(ContentNode.Leaf("source", written));
            var medium = WpfMedium.OnThisThread;
            var laid = SourceShown.Lay(tree, [(tree, "Not read.")], medium, medium.Face(setting.Face), setting.Size, WpfStyles.Dark.Text,
                                               WpfStyles.Dark);

            var letters = laid.Root.SelfAndDescendants()
                .Where(piece => piece.Kind == LayoutText.SourceKind + "-letter")
                .ToDictionary(piece => piece.Part!.Start, piece => piece.Bounds.ToWpf());
            var drawn = Drawn(written, setting);

            CollectionAssert.AreEquivalent(drawn.Keys.ToList(), letters.Keys.ToList(),
                                           $"{setting.Face.FontFamily}: the letters with a place in {Show(written)}");

            foreach (var (at, box) in drawn)
            {
                var placed = letters[at];
                Assert.IsTrue(Close(box, placed),
                              $"{setting.Face.FontFamily}: letter {at} ('{Show(written[at].ToString())}') of {Show(written)} is drawn at {box} but stands at {placed}");
            }
        }
    });

    /// <summary>Where the type engine draws each letter: its line set as one run, a line's height below the lines above it.</summary>
    private static Dictionary<int, Rect> Drawn(string written, (Typeface Face, double Size) setting)
    {
        var boxes = new Dictionary<int, Rect>();
        var blank = Set(" ", setting).Height;
        var top = 0.0;

        for (var start = 0; start <= written.Length;)
        {
            var end = written.IndexOf('\n', start);
            if (end < 0) end = written.Length;

            var shown = end > start && written[end - 1] == '\r' ? end - 1 : end;
            var line = shown > start ? Set(written[start..shown], setting) : null;

            for (var at = start; at < shown; at++)
                if (line!.BuildHighlightGeometry(new Point(0, top), at - start, 1)?.Bounds is { IsEmpty: false, Width: > 0, Height: > 0 } box)
                    boxes[at] = box;

            top += line?.Height ?? blank;
            start = end + 1;
        }

        return boxes;
    }

    /// <summary>A line set by WPF's type engine as one run, in the face and size it is asked in.</summary>
    private static FormattedText Set(string text, (Typeface Face, double Size) setting) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, setting.Face, setting.Size, Brushes.Black, 1.0);

    private static bool Close(Rect a, Rect b) =>
        new[] { a.X - b.X, a.Y - b.Y, a.Width - b.Width, a.Height - b.Height }.All(d => System.Math.Abs(d) < 0.01);

    private static string Show(string text) => text.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
}
