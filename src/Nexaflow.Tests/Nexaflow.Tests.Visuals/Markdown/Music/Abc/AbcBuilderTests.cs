using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
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

                // Either a part of this reading, or - for a grouping the notation declares no node for,
                // which is what a section is - a stretch whose two ends are both ends of parts that are.
                // The second is what stops a span being a licence to name any two numbers: it has to
                // begin where something written begins and finish where something written finishes.
                if (node.Part is ContentPart part)
                {
                    Assert.IsTrue(parts.Contains(part),
                        $"{what}: {node.Kind} names a part that is not in this reading");
                }
                else
                {
                    Assert.IsInstanceOfType<SourceSpan>(node.Part, $"{what}: {node.Kind} names something else");
                    Assert.IsTrue(parts.Any(p => p.Start == node.Part.Start),
                        $"{what}: {node.Kind} spans from {node.Part.Start}, where nothing was written");
                    Assert.IsTrue(parts.Any(p => p.End() == node.Part.End()),
                        $"{what}: {node.Kind} spans to {node.Part.End()}, where nothing was written");
                }

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

    // ── What is drawn beside the notes ──────────────────────────────────────

    [TestMethod]
    public void ATieAndASlurAreCurvesThatNobodyTyped() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build("X:1\nL:1/8\nK:C\nA-A (BcdB)|\n", 500, Brushes.Black, 1.0);

        var ties = layout.Root.SelfAndDescendants().Where(n => n.Kind == "tie").ToList();
        var slurs = layout.Root.SelfAndDescendants().Where(n => n.Kind == "slur").ToList();

        Assert.AreEqual(1, ties.Count);
        Assert.AreEqual(1, slurs.Count);

        // Neither carries a part. The `-` and the `(` that asked for them are marks on the notes either
        // side; the arc between is how those are drawn. A curve that named a stretch of source would offer
        // itself as a caret stop spanning both notes — and being shallower than either, it would win.
        foreach (var curve in ties.Concat(slurs))
        {
            Assert.IsNull(curve.Part, $"a {curve.Kind} says it was written");
            Assert.IsFalse(curve.IsInk, $"a {curve.Kind} offers itself for selection");
            Assert.IsTrue(curve.Bounds.Width > 0 && curve.Bounds.Height > 0, $"a {curve.Kind} drew nothing");
        }
    });

    [TestMethod]
    public void AndASlurAcrossASystemBreakIsDrawnAtBothEnds() => UiThread.Run(() =>
    {
        // Narrow enough that the tune cannot sit on one line, with a slur running over the break.
        var layout = AbcLayout.Build(
            "X:1\nL:1/8\nK:C\n(ABcd ABcd|ABcd ABcd|ABcd ABcd|ABcd ABcd)|\n", 300, Brushes.Black, 1.0);

        var systems = layout.Root.Children.Count(n => n.Kind == "system");
        var pieces = layout.Root.SelfAndDescendants().Count(n => n.Kind == "slur");

        Assert.IsTrue(systems >= 2, "the tune should not fit on one line at this width");
        Assert.AreEqual(2, pieces, "out to the right margin, and in from the left of the next");
    });

    [TestMethod]
    public void ADecorationIsDrawnWhereItsKindBelongs() => UiThread.Run(() =>
    {
        var plain = AbcLayout.Build("X:1\nK:C\nA|\n", 400, Brushes.Black, 1.0);
        var marked = AbcLayout.Build("X:1\nK:C\n.HA|\n", 400, Brushes.Black, 1.0);

        // A staccato hugs the head and a fermata stacks clear of the staff, but both are marks on the same
        // piece — so what says they landed is that the piece drew more than a bare note does.
        Assert.IsTrue(Marks(Note(marked)) > Marks(Note(plain)), "neither mark was drawn");

        // …and the tune got taller to make room for the one that lives outside the staff.
        Assert.IsTrue(marked.Size.Height > plain.Size.Height, "no room was reserved above the staff");
    });

    [TestMethod]
    public void GraceNotesAreDrawnOnTheNoteTheyBelongTo() => UiThread.Run(() =>
    {
        var plain = AbcLayout.Build("X:1\nK:C\nA|\n", 400, Brushes.Black, 1.0);
        var graced = AbcLayout.Build("X:1\nK:C\n{gAG}A|\n", 400, Brushes.Black, 1.0);

        var note = Note(graced);

        Assert.IsTrue(Marks(note) > Marks(Note(plain)), "the grace notes were not drawn");
        Assert.AreEqual("A", graced.Abc.Substring(note.Sits().Start, note.Sits().Length),
            "and they hang off the note they precede, so selecting it takes them with it");
    });

    [TestMethod]
    public void ARepeatBracketRunsFromItsNumberToWhereTheRepeatEnds() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build(
            "X:1\nL:1/8\nK:G\n|:GABc dedB|1 dedB dedB:|2 c2ec B2dB|]\n", 700, Brushes.Black, 1.0);

        var brackets = layout.Root.SelfAndDescendants().Where(n => n.Kind == "volta").ToList();

        Assert.AreEqual(2, brackets.Count, "a first-time bracket and a second-time one");

        // Each names the number somebody wrote, which is what makes it something a reader can point at.
        var written = brackets.Select(b => layout.Abc.Substring(b.Sits().Start, b.Sits().Length)).ToList();
        CollectionAssert.AreEqual(new[] { "1", "2" }, written);

        Assert.IsTrue(brackets[0].Bounds.Right <= brackets[1].Bounds.Left + 1,
            "the first bracket stops where the second begins");
    });

    [TestMethod]
    public void APlacedAnnotationGoesWhereItsQuotesSaid() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build("""
            X:1
            K:C
            "^over"A "_under"B|

            """.ReplaceLineEndings("\n").Replace("            ", ""), 500, Brushes.Black, 1.0);

        var staff = layout.Root.SelfAndDescendants().First(n => n.Kind == "staff-line").Bounds;
        var notes = layout.Root.SelfAndDescendants().Where(n => n.Kind == "note").ToList();

        Assert.AreEqual(2, notes.Count);
        Assert.IsTrue(notes[0].Bounds.Top < staff.Top, "the text above reaches over the staff");
        Assert.IsTrue(notes[1].Bounds.Bottom > staff.Bottom, "and the text below reaches under it");
    });

    // ── Parts that sound together ───────────────────────────────────────────

    private const string PartSong =
        "X:1\nM:4/4\nL:1/8\nK:C\nV:1 clef=treble name=!Soprano!\nCDEF GABc|cBAG FEDC|\n"
      + "V:2 clef=bass name=!Bass!\nC,D,E,F, G,A,B,C|CB,A,G, F,E,D,C,|\n";

    [TestMethod]
    public void TwoVoicesAreBracketedIntoOneSystem() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build(PartSong.Replace('!', '"'), 700, Brushes.Black, 1.0);

        var systems = layout.Root.Children.Where(n => n.Kind == "system").ToList();
        var brackets = layout.Root.Children.Where(n => n.Kind == "bracket").ToList();

        Assert.AreEqual(2, systems.Count, "one staff per voice");
        Assert.AreEqual(1, brackets.Count, "and one bracket joining them");

        // It reaches from the top staff to the bottom one, which is the whole of what it says. Staff line
        // to staff line, not the full height of the ink: a beam reaching above the top staff is not
        // something the bracket is joining.
        var top = Staff(systems[0]);
        var bottom = Staff(systems[1]);

        Assert.AreEqual(top.Top, brackets[0].Bounds.Top, 1.0, "it starts at the top staff");
        Assert.AreEqual(bottom.Bottom, brackets[0].Bounds.Bottom, 1.0, "and finishes at the bottom one");

        static Rect Staff(ILayoutNode system)
        {
            var lines = system.SelfAndDescendants().Where(n => n.Kind == "staff-line").ToList();
            return new Rect(lines[0].Bounds.TopLeft, lines[^1].Bounds.BottomRight);
        }
    });

    [TestMethod]
    public void AndTheirBarsLineUp() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build(PartSong.Replace('!', '"'), 700, Brushes.Black, 1.0);

        var systems = layout.Root.Children.Where(n => n.Kind == "system").ToList();
        var lines = systems
            .Select(s => s.SelfAndDescendants().Where(n => n.Kind == "barline").Select(n => n.Bounds.X).ToList())
            .ToList();

        Assert.AreEqual(lines[0].Count, lines[1].Count, "the voices are barred the same way");
        for (var at = 0; at < lines[0].Count; at++)
            Assert.AreEqual(lines[0][at], lines[1][at], 1.0, $"bar line {at} is not above its partner");
    });

    [TestMethod]
    public void AndEachTakesTheClefAndNameItsVoiceAskedFor() => UiThread.Run(() =>
    {
        // Both are written on the V: line, which is in the header — before any music. Reading them under
        // the guard that stops the header's meter being printed twice is how the first voice lost both.
        var layout = AbcLayout.Build(PartSong.Replace('!', '"'), 700, Brushes.Black, 1.0);

        Assert.AreEqual(2, layout.Root.SelfAndDescendants().Count(n => n.Kind == "voice"),
            "both voices are named at the left");

        // The bass part is written low. In the treble clef it would hang far below the staff on ledger
        // lines; in the clef it asked for it sits on it.
        var systems = layout.Root.Children.Where(n => n.Kind == "system").ToList();
        var bass = systems[1].SelfAndDescendants().Where(n => n.Kind == "note").ToList();
        var staff = systems[1].SelfAndDescendants().Where(n => n.Kind == "staff-line").ToList();

        Assert.IsTrue(bass.Count > 0);
        Assert.IsTrue(bass.All(n => n.Bounds.Bottom < staff[^1].Bounds.Bottom + (3 * 8)),
            "the bass part is buried in ledger lines, so it was drawn in the wrong clef");
    });

    [TestMethod]
    public void ButVoicesBarredDifferentlyStackHonestlyInstead() => UiThread.Run(() =>
    {
        // Real tunebooks do this, and it is nobody's mistake. Forcing a grid onto voices that disagree
        // about where the bars are would misalign every bar after the first difference.
        var uneven = "X:1\nM:4/4\nL:1/8\nK:C\nV:1\nCDEF GABc|cBAG|\nV:2\nC,D,E,F,|G,A,B,C|CB,A,G,|\n";

        var layout = AbcLayout.Build(uneven, 700, Brushes.Black, 1.0);

        Assert.AreEqual(2, layout.Root.Children.Count(n => n.Kind == "system"));
        Assert.AreEqual(0, layout.Root.Children.Count(n => n.Kind == "bracket"),
            "nothing may be bracketed that is not simultaneous");
    });

    [TestMethod]
    public void ZoomingOutReEngravesRatherThanShrinksThePicture() => UiThread.Run(() =>
    {
        // Both are given the same room to engrave in — one has it directly, the other because zooming out
        // is what buys it. So they are the same page, drawn at two sizes.
        var big = Engraved(SpeedThePlough, 840, zoom: 1.0);
        var small = Engraved(SpeedThePlough, 420, zoom: 0.5);

        Assert.IsTrue(small.DesiredSize.Width <= 421, $"it overflowed its room: {small.DesiredSize.Width:F0}");
        Assert.AreEqual(big.DesiredSize.Width / 2, small.DesiredSize.Width, 1.5, "the same page, half the size");
        Assert.AreEqual(big.DesiredSize.Height / 2, small.DesiredSize.Height, 1.5);

        // …which is the half worth stating: a smaller notation gets MORE bars on a line, so it is not the
        // same page at all when the room is what stays fixed.
        var cramped = Engraved(SpeedThePlough, 420, zoom: 1.0);
        Assert.IsTrue(small.DesiredSize.Height < cramped.DesiredSize.Height,
            $"in the same {420}px, zoomed out came to {small.DesiredSize.Height:F0} and full size to "
            + $"{cramped.DesiredSize.Height:F0}");
    });

    [TestMethod]
    public void AndAClickStillLandsOnTheNoteItLooksLikeItLandsOn() => UiThread.Run(() =>
    {
        // The layout is in its own coordinates whatever the zoom, so a pointer has to be divided by it on
        // the way in. Without that every note but the first is off by however far it was scaled — which is
        // invisible on the first bar of a short tune and wrong everywhere else.
        const string Tune = "X:1\nL:1/4\nK:C\nCDEF GABc|\n";

        var big = Engraved(Tune, 840, zoom: 1.0);
        var at = new Point(big.DesiredSize.Width * 0.72, big.DesiredSize.Height / 2);

        big.BeginPointerSelect(at);
        ((IEditableBlock)big).HandleKey(Key.PageUp, ModifierKeys.None);
        Assert.AreNotEqual(Tune, big.Source, "the point has to be over a note for this to prove anything");

        // The same page at half the size, clicked in the same place on it. InteractiveSelection owns one
        // selection for the whole process, so the first is finished with before the second starts.
        var small = Engraved(Tune, 420, zoom: 0.5);

        small.BeginPointerSelect(new Point(at.X / 2, at.Y / 2));
        ((IEditableBlock)small).HandleKey(Key.PageUp, ModifierKeys.None);

        Assert.AreEqual(big.Source, small.Source, "the same click, and the same note under it");
    });

    /// <summary>A score, measured and arranged into a given width at a given zoom.</summary>
    private static AbcElement Engraved(string abc, double available, double zoom)
    {
        var element = new AbcElement(abc, MarkdownPalette.Dark) { Zoom = zoom };
        element.Measure(new Size(available, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(0, 0), element.DesiredSize));
        return element;
    }

    /// <summary>The first note in a tune.</summary>
    private static ILayoutNode Note(AbcLayout layout) =>
        layout.Root.SelfAndDescendants().First(n => n.Kind == "note");

    /// <summary>How many things a piece of layout drew — the cheap way to ask whether a mark landed.</summary>
    private static int Marks(ILayoutNode node) =>
        node.SelfAndDescendants().OfType<AbcLayoutNode>().Sum(n => n.Marks.Count);

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
