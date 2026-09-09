using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;

namespace Nexaflow.Tests.Visuals.Markdown.Music.Abc;

/// <summary>
/// The three gestures, drawn: a press, a drag along the notes, and a drag down from a note to a word.
///
/// <para>
/// Opt-in, and a picture rather than an assertion, because what is wrong with a selection is a thing you
/// see rather than a number you compare. The defect this was written for read perfectly in the ranges —
/// six notes, exactly the six dragged over — while the page showed a wash reaching from the beam down
/// through the lyric row, because a container named the same characters its contents did.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("a picture to look at, not a check")]
[DoNotParallelize]
public class AbcSelectionPicture
{
    private const string Tune =
        "X: 1\nT: the Auld Grey Cat\nM: C|\nL: 1/8\nK: EDorian\n"
        + "\"Em\"e2e2 E3F | \"D\"GFGA BABc |\n"
        + "w: one two three four five six sev-en eight\n";

    [TestMethod]
    public void ShowWhatEachGestureSelects() => UiThread.Run(() =>
    {
        var into = Environment.GetEnvironmentVariable("NEXAFLOW_ABC_COMPARE");
        if (string.IsNullOrWhiteSpace(into)) { Assert.Inconclusive("set NEXAFLOW_ABC_COMPARE"); return; }

        var shots = new[]
        {
            ("a press on one note", Shot("note", 2, "note", 2)),
            ("a drag along the notes", Shot("note", 0, "note", 5)),
            ("a drag from a note down to a word", Shot("note", 1, "syllable", 4)),
        };

        var width = (int)shots.Max(s => s.Item2.Width) + 20;
        var height = shots.Sum(s => (int)s.Item2.Height + 26) + 10;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            double y = 6;

            foreach (var (what, shot) in shots)
            {
                dc.DrawText(Label(what), new Point(10, y));
                y += 20;
                dc.DrawImage(shot, new Rect(10, y, shot.Width, shot.Height));
                y += shot.Height + 6;
            }
        }

        var page = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        page.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(page));
        using var stream = File.Create(Path.Combine(into, "selection.png"));
        encoder.Save(stream);
    });

    private static FormattedText Label(string text) =>
        new(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 12, Brushes.DimGray, 1.0);

    private static RenderTargetBitmap Shot(string fromKind, int fromAt, string toKind, int toAt)
    {
        var element = new AbcElement(Tune, MarkdownPalette.Light);
        element.Measure(new Size(760, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(0, 0), element.DesiredSize));

        element.BeginPointerSelect(Middle(Every(element, fromKind)[fromAt]));
        element.ExtendPointerSelect(Middle(Every(element, toKind)[toAt]));
        element.EndPointerSelect();

        // The selection asked for a repaint and nothing has painted: off a live window there is no render
        // pass, so the bitmap would come back showing the drawing made during Arrange — the tune with
        // nothing selected, which is a very convincing picture of a wash that does not work.
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

        var shot = new RenderTargetBitmap((int)element.DesiredSize.Width, (int)element.DesiredSize.Height,
                                          96, 96, PixelFormats.Pbgra32);
        shot.Render(element);
        return shot;
    }

    private static List<Piece> Every(AbcElement element, string kind) =>
        [.. element.Layout!.Root.SelfAndDescendants().Where(n => n.Kind == kind)];

    private static Point Middle(Piece node) =>
        new(node.Bounds.X + (node.Bounds.Width / 2), node.Bounds.Y + (node.Bounds.Height / 2));
}
