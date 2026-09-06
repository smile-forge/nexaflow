using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;

namespace Nexaflow.Tests.Visuals.Markdown.Music.Abc;

/// <summary>
/// The engraver, and the promise the layout tree rests on: every piece of the picture either names a real
/// part of the tune or names nothing at all.
///
/// <para>
/// The second half is the one worth stating. A staff line, a stem, a beam and a ledger line are how music
/// is drawn, not anything anybody typed — so they carry no part, are not ink, and cannot be selected or
/// stood beside. Pointing at one still means something: the shared queries resolve it to the nearest thing
/// above it that <em>was</em> written. Getting this wrong is invisible until a reader drags across a line
/// and copies out a stem.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("abc-layout")]
// InteractiveSelection owns one selection for the whole page — a process-wide static. Two elements
// selecting on different UI threads at once would each clear the other, so these run on their own.
[DoNotParallelize]
public class AbcBuilderTests
{
    private const string SpeedThePlough =
        "X:1\nT:Speed the Plough\nM:4/4\nC:Trad.\nK:G\n"
        + "|:GABc dedB|dedB dedB|c2ec B2dB|c2A2 A2BA|\n"
        + "GABc dedB|dedB dedB|c2ec B2dB|A2AG A2:|\n";

    [TestMethod]
    public void EveryPieceOfThePictureNamesARealPartOfTheTuneOrNothingAtAll() => UiThread.Run(() =>
    {
        foreach (var (what, abc) in AbcConstructs.Everything)
        {
            var layout = AbcLayout.Build(abc, 700, Brushes.Black, 1.0);
            var parts = layout.Reading.Root.SelfAndDescendants().ToHashSet();

            foreach (var node in layout.Root.SelfAndDescendants())
            {
                if (node.Part is null) continue;

                Assert.IsInstanceOfType<ContentPart>(node.Part, $"{what}: {node.Kind} names something else");
                Assert.IsTrue(parts.Contains((ContentPart)node.Part),
                    $"{what}: {node.Kind} names a part that is not in this reading");

                var at = node.Sits();
                Assert.IsTrue(at.Start >= 0 && at.End <= abc.Length,
                    $"{what}: {node.Kind} claims {at.Start}+{at.Length} of {abc.Length}");
            }
        }
    });

    [TestMethod]
    public void AndWhatNobodyWroteIsDrawnWithoutBeingSelectable() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build(SpeedThePlough, 700, Brushes.Black, 1.0);

        foreach (var node in layout.Root.SelfAndDescendants())
        {
            if (node.Kind is not ("staff-line" or "beam-bar" or "clef" or "key" or "meter")) continue;

            Assert.IsNull(node.Part, $"a {node.Kind} says it was written");
            Assert.IsFalse(node.IsInk, $"a {node.Kind} offers itself for selection");
        }

        // …and the tune is not simply empty, which is the way this test could pass for the wrong reason.
        Assert.IsTrue(layout.Root.Ink().Any(), "nothing selectable was engraved at all");
    });

    [TestMethod]
    public void EveryConstructEngravesToSomething() => UiThread.Run(() =>
    {
        foreach (var (what, abc) in AbcConstructs.Everything)
        {
            var layout = AbcLayout.Build(abc, 700, Brushes.Black, 1.0);

            if (!abc.Contains('|') && !abc.Contains("ABc")) continue;

            Assert.IsTrue(layout.Size.Width > 0 && layout.Size.Height > 0, $"{what}: engraved to nothing");
            Assert.IsTrue(layout.Root.Ink().Any(), $"{what}: nothing in it can be pointed at");
        }
    });

    [TestMethod]
    public void ANoteIsWhatAClickOnItsHeadMeans() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build("X:1\nK:C\nCDEF|\n", 400, Brushes.Black, 1.0);

        var notes = layout.Root.SelfAndDescendants().Where(n => n.Kind == "note").ToList();
        Assert.AreEqual(4, notes.Count);

        foreach (var note in notes)
        {
            var middle = new Point(note.Bounds.X + (note.Bounds.Width / 2), note.Bounds.Y + (note.Bounds.Height / 2));
            var hit = layout.Root.NodeAt(middle);

            Assert.AreSame(note, hit, "a click on a head means that note");
        }

        // And each one names exactly the letter that was typed.
        var written = notes.Select(n => layout.Abc.Substring(n.Sits().Start, n.Sits().Length)).ToList();
        CollectionAssert.AreEqual(new[] { "C", "D", "E", "F" }, written);
    });

    [TestMethod]
    public void AndABeamedRunIsWhatADragAcrossItMeans() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build("X:1\nL:1/8\nK:C\nABcd efga|\n", 500, Brushes.Black, 1.0);

        var beams = layout.Root.SelfAndDescendants().Where(n => n.Kind == "beam").ToList();
        Assert.AreEqual(2, beams.Count, "two runs of four");

        var notes = beams[0].SelfAndDescendants().Where(n => n.Kind == "note").ToList();
        Assert.AreEqual(4, notes.Count);

        var swept = ContentSelection.Between(layout.Root, notes[0], notes[^1]);

        Assert.AreEqual(1, swept.Ranges.Count, "the run is one stretch of source, not four");
        Assert.AreEqual("ABcd", layout.Abc.Substring(swept.Ranges[0].Start, swept.Ranges[0].Length));
    });

    [TestMethod]
    public void ABarLineCanBePointedAtBecauseSomebodyWroteIt() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build("X:1\nK:C\nCDE|FGA|]\n", 400, Brushes.Black, 1.0);

        var lines = layout.Root.SelfAndDescendants().Where(n => n.Kind == "barline").ToList();

        Assert.AreEqual(2, lines.Count);
        foreach (var line in lines)
        {
            Assert.IsNotNull(line.Part, "a bar line is written");
            Assert.IsTrue(line.IsInk, "…so it can be selected");
        }

        Assert.AreEqual("|", layout.Abc.Substring(lines[0].Sits().Start, lines[0].Sits().Length));
        Assert.AreEqual("|]", layout.Abc.Substring(lines[1].Sits().Start, lines[1].Sits().Length));
    });

    [TestMethod]
    public void ItPaintsWithoutFaulting() => UiThread.Run(() =>
    {
        // Rendering to a bitmap forces OnRender to run — measure and arrange alone would not — so this
        // exercises every draw path end to end: clef, key, meter, heads, stems, flags, beams, bar lines.
        var element = new AbcElement(SpeedThePlough, MarkdownPalette.Dark);
        element.Measure(new Size(700, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(0, 0), element.DesiredSize));

        Assert.IsTrue(element.DesiredSize.Width > 0 && element.DesiredSize.Height > 0);

        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(element.DesiredSize.Width)),
            Math.Max(1, (int)Math.Ceiling(element.DesiredSize.Height)),
            96, 96, PixelFormats.Pbgra32);

        bitmap.Render(element);
        Assert.IsTrue(bitmap.PixelWidth > 0);
    });

    [TestMethod]
    public void EveryLineButAShortLastOneSharesOneWidth() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build(SpeedThePlough, 600, Brushes.Black, 1.0);

        var systems = layout.Root.Children.Where(n => n.Kind == "system").ToList();
        Assert.IsTrue(systems.Count >= 2, "the tune should not fit on one line at this width");

        var rights = systems.Take(systems.Count - 1).Select(s => s.Bounds.Right).ToList();
        foreach (var right in rights)
            Assert.AreEqual(rights[0], right, 2.0, "every full system fills the same width");
    });
}
