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
using Nexaflow.Visuals.Text.Markdown.Music;


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
            var layout = AbcBuilder.Build(abc, 700, Brushes.Black, 1.0);

            // Read again here rather than taken from the builder, which hands back a layout and nothing else.
            // So the parts cannot be matched by identity, and are matched by what they are and where they are
            // written instead — see the root check below, which is what identity was really buying.
            var parts = ContentReading.Of(AbcPipeline.Read(abc, AbcBuilder.Draws, null)).Root
                .SelfAndDescendants()
                .Select(p => (p.Kind, p.Start, p.Length))
                .ToHashSet();

            ContentPart? root = null;

            foreach (var node in layout.Root.SelfAndDescendants())
            {
                if (node.Part is null) continue;

                // Either a part of this reading, or - for a grouping the notation declares no node for,
                // which is what a section is - a stretch whose two ends are both ends of parts that are.
                // The second is what stops a span being a licence to name any two numbers: it has to
                // begin where something written begins and finish where something written finishes.
                if (node.Part is ContentPart part)
                {
                    Assert.IsTrue(parts.Contains((part.Kind, part.Start, part.Length)),
                        $"{what}: {node.Kind} names a part that is not in this reading");

                    // And every part on every piece comes from one tree. That is what the identity check used
                    // to prove: a builder that mixed two readings would put pointers into a tune nobody is
                    // looking at on half the picture, and each of them would pass the check above on its own.
                    var mine = part.Ancestors().LastOrDefault() ?? part;
                    root ??= mine;
                    Assert.AreSame(root, mine, $"{what}: {node.Kind} was drawn from a different reading");
                }
                else
                {
                    Assert.IsInstanceOfType<SourceSpan>(node.Part, $"{what}: {node.Kind} names something else");
                    Assert.IsTrue(parts.Any(p => node.Part.Start >= p.Start && node.Part.End() <= p.Start + p.Length),
                        $"{what}: {node.Kind} spans {node.Part.Start}+{node.Part.Length}, which is not inside anything written");
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
        var layout = AbcBuilder.Build(SpeedThePlough, 700, Brushes.Black, 1.0);

        foreach (var node in layout.Root.SelfAndDescendants())
        {
            if (node.Kind is not ("staff-line" or "beam-bar" or "clef" or "key" or "meter")) continue;

            Assert.IsNull(node.Part, $"a {node.Kind} says it was written");
        }

        // …and the tune is not simply empty, which is the way this test could pass for the wrong reason.
        Assert.IsTrue(layout.Root.Leaves().Any(), "nothing selectable was engraved at all");
    });

    [TestMethod]
    public void EveryConstructEngravesToSomething() => UiThread.Run(() =>
    {
        foreach (var (what, abc) in AbcConstructs.Everything)
        {
            var layout = AbcBuilder.Build(abc, 700, Brushes.Black, 1.0);

            if (!abc.Contains('|') && !abc.Contains("ABc")) continue;

            Assert.IsTrue(layout.Size.Width > 0 && layout.Size.Height > 0, $"{what}: engraved to nothing");
            Assert.IsTrue(layout.Root.Leaves().Any(), $"{what}: nothing in it can be pointed at");
        }
    });

    [TestMethod]
    public void ABarLineCanBePointedAtBecauseSomebodyWroteIt() => UiThread.Run(() =>
    {
        var layout = AbcBuilder.Build("X:1\nK:C\nCDE|FGA|]\n", 400, Brushes.Black, 1.0);

        var lines = layout.Root.SelfAndDescendants().Where(n => n.Kind == "barline").ToList();

        Assert.AreEqual(2, lines.Count);
        foreach (var line in lines)
        {
            Assert.IsNotNull(line.Part, "a bar line is written");
        }

        Assert.AreEqual("|", "X:1\nK:C\nCDE|FGA|]\n".Substring(lines[0].Sits().Start, lines[0].Sits().Length));
        Assert.AreEqual("|]", "X:1\nK:C\nCDE|FGA|]\n".Substring(lines[1].Sits().Start, lines[1].Sits().Length));
    });

    // ── What is drawn beside the notes ──────────────────────────────────────

    [TestMethod]
    public void ATieAndASlurAreCurvesThatNobodyTyped() => UiThread.Run(() =>
    {
        var layout = AbcBuilder.Build("X:1\nL:1/8\nK:C\nA-A (BcdB)|\n", 500, Brushes.Black, 1.0);

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
            Assert.IsTrue(curve.Bounds.Width > 0 && curve.Bounds.Height > 0, $"a {curve.Kind} drew nothing");
        }
    });

    [TestMethod]
    public void AndASlurAcrossASystemBreakIsDrawnAtBothEnds() => UiThread.Run(() =>
    {
        // Narrow enough that the tune cannot sit on one line, with a slur running over the break.
        var layout = AbcBuilder.Build(
            "X:1\nL:1/8\nK:C\n(ABcd ABcd|ABcd ABcd|ABcd ABcd|ABcd ABcd)|\n", 300, Brushes.Black, 1.0);

        var systems = layout.Root.SelfAndDescendants().Count(n => n.Kind == "system");
        var pieces = layout.Root.SelfAndDescendants().Count(n => n.Kind == "slur");

        Assert.IsTrue(systems >= 2, "the tune should not fit on one line at this width");
        Assert.AreEqual(2, pieces, "out to the right margin, and in from the left of the next");
    });

    [TestMethod]
    public void ADecorationIsDrawnWhereItsKindBelongs() => UiThread.Run(() =>
    {
        var plain = AbcBuilder.Build("X:1\nK:C\nA|\n", 400, Brushes.Black, 1.0);
        var marked = AbcBuilder.Build("X:1\nK:C\n.HA|\n", 400, Brushes.Black, 1.0);

        // A staccato hugs the head and a fermata stacks clear of the staff, but both are marks on the same
        // piece — so what says they landed is that the piece drew more than a bare note does.
        Assert.IsTrue(Marks(Note(marked)) > Marks(Note(plain)), "neither mark was drawn");

        // …and the tune got taller to make room for the one that lives outside the staff.
        Assert.IsTrue(marked.Size.Height > plain.Size.Height, "no room was reserved above the staff");
    });

    [TestMethod]
    public void GraceNotesAreDrawnOnTheNoteTheyBelongTo() => UiThread.Run(() =>
    {
        var plain = AbcBuilder.Build("X:1\nK:C\nA|\n", 400, Brushes.Black, 1.0);
        var graced = AbcBuilder.Build("X:1\nK:C\n{gAG}A|\n", 400, Brushes.Black, 1.0);

        var note = Note(graced);

        Assert.IsTrue(Marks(note) > Marks(Note(plain)), "the grace notes were not drawn");
        Assert.AreEqual("A", "X:1\nK:C\n{gAG}A|\n".Substring(note.Sits().Start, note.Sits().Length),
            "and they hang off the note they precede, so selecting it takes them with it");
    });

    [TestMethod]
    public void ARepeatBracketRunsFromItsNumberToWhereTheRepeatEnds() => UiThread.Run(() =>
    {
        var layout = AbcBuilder.Build(
            "X:1\nL:1/8\nK:G\n|:GABc dedB|1 dedB dedB:|2 c2ec B2dB|]\n", 700, Brushes.Black, 1.0);

        var brackets = layout.Root.SelfAndDescendants().Where(n => n.Kind == "volta").ToList();

        Assert.AreEqual(2, brackets.Count, "a first-time bracket and a second-time one");

        // Each names the number somebody wrote, which is what makes it something a reader can point at.
        var written = brackets.Select(b => "X:1\nL:1/8\nK:G\n|:GABc dedB|1 dedB dedB:|2 c2ec B2dB|]\n".Substring(b.Sits().Start, b.Sits().Length)).ToList();
        CollectionAssert.AreEqual(new[] { "1", "2" }, written);

        Assert.IsTrue(brackets[0].Bounds.Right <= brackets[1].Bounds.Left + 1,
            "the first bracket stops where the second begins");
    });

    [TestMethod]
    public void APlacedAnnotationGoesWhereItsQuotesSaid() => UiThread.Run(() =>
    {
        var layout = AbcBuilder.Build("""
            X:1
            K:C
            "^over"A "_under"B|

            """.ReplaceLineEndings("\n").Replace("            ", ""), 500, Brushes.Black, 1.0);

        var staff = layout.Root.SelfAndDescendants().First(n => n.Kind == "staff-line").Bounds;
        var notes = layout.Root.SelfAndDescendants().Where(n => n.Kind == "note").ToList();

        Assert.AreEqual(2, notes.Count);
        // What it drew, not the room it reserves: a note reserves exactly its staff.
        Assert.IsTrue(notes[0].Ink().Top < staff.Top, "the text above reaches over the staff");
        Assert.IsTrue(notes[1].Ink().Bottom > staff.Bottom, "and the text below reaches under it");
    });

    // ── Parts that sound together ───────────────────────────────────────────

    private const string PartSong =
        "X:1\nM:4/4\nL:1/8\nK:C\nV:1 clef=treble name=!Soprano!\nCDEF GABc|cBAG FEDC|\n"
      + "V:2 clef=bass name=!Bass!\nC,D,E,F, G,A,B,C|CB,A,G, F,E,D,C,|\n";

    [TestMethod]
    public void TwoVoicesAreBracketedIntoOneSystem() => UiThread.Run(() =>
    {
        var layout = AbcBuilder.Build(PartSong.Replace('!', '"'), 700, Brushes.Black, 1.0);

        var systems = layout.Root.SelfAndDescendants().Where(n => n.Kind == "system").ToList();
        var brackets = layout.Root.SelfAndDescendants().Where(n => n.Kind == "bracket").ToList();

        Assert.AreEqual(2, systems.Count, "one staff per voice");
        Assert.AreEqual(1, brackets.Count, "and one bracket joining them");

        // It reaches from the top staff to the bottom one, which is the whole of what it says. Staff line
        // to staff line, not the full height of the ink: a beam reaching above the top staff is not
        // something the bracket is joining.
        var top = Staff(systems[0]);
        var bottom = Staff(systems[1]);

        Assert.AreEqual(top.Top, brackets[0].Bounds.Top, 1.0, "it starts at the top staff");
        Assert.AreEqual(bottom.Bottom, brackets[0].Bounds.Bottom, 1.0, "and finishes at the bottom one");

        static Rect Staff(Piece system)
        {
            var lines = system.SelfAndDescendants().Where(n => n.Kind == "staff-line").ToList();
            return new Rect(lines[0].Bounds.TopLeft, lines[^1].Bounds.BottomRight);
        }
    });

    [TestMethod]
    public void AndTheirBarsLineUp() => UiThread.Run(() =>
    {
        var layout = AbcBuilder.Build(PartSong.Replace('!', '"'), 700, Brushes.Black, 1.0);

        var systems = layout.Root.SelfAndDescendants().Where(n => n.Kind == "system").ToList();
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
        var layout = AbcBuilder.Build(PartSong.Replace('!', '"'), 700, Brushes.Black, 1.0);

        Assert.AreEqual(2, layout.Root.SelfAndDescendants().Count(n => n.Kind == "voice"),
            "both voices are named at the left");

        // The bass part is written low. In the treble clef it would hang far below the staff on ledger
        // lines; in the clef it asked for it sits on it.
        var systems = layout.Root.SelfAndDescendants().Where(n => n.Kind == "system").ToList();
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

        var layout = AbcBuilder.Build(uneven, 700, Brushes.Black, 1.0);

        Assert.AreEqual(2, layout.Root.SelfAndDescendants().Count(n => n.Kind == "system"));
        Assert.AreEqual(0, layout.Root.SelfAndDescendants().Count(n => n.Kind == "bracket"),
            "nothing may be bracketed that is not simultaneous");
    });

    [TestMethod]
    public void AKeyCanNameTheClef_WithOrWithoutClefEquals() => UiThread.Run(() =>
    {
        // The standard makes `clef=` optional, and the corpus writes both. C, is the second space of a bass
        // staff and far under a treble one, so where its head lands says which clef it was drawn in.
        foreach (var key in new[] { "K:C bass", "K:C clef=bass" })
        {
            var layout = AbcBuilder.Build($"X:1\nL:1/4\n{key}\nC,|\n", 400, Brushes.Black, 1.0);
            var staff = layout.Root.SelfAndDescendants().Where(n => n.Kind == "staff-line").ToList();
            var head = layout.Root.SelfAndDescendants().First(n => n.Kind == "head").Ink();

            Assert.IsTrue(head.Top >= staff[0].Bounds.Top - 1 && head.Bottom <= staff[^1].Bounds.Bottom + 1,
                $"{key}: C, should sit on a bass staff, not hang under a treble one");
        }

        // …and only a K: or a V: can name one: a title about a bass is not a clef.
        var titled = AbcBuilder.Build("X:1\nT:Bass line\nL:1/4\nK:C\nc|\n", 400, Brushes.Black, 1.0);
        var lines = titled.Root.SelfAndDescendants().Where(n => n.Kind == "staff-line").ToList();
        var c = titled.Root.SelfAndDescendants().First(n => n.Kind == "head").Ink();
        Assert.IsTrue(c.Top >= lines[0].Bounds.Top - 1 && c.Bottom <= lines[^1].Bounds.Bottom + 1,
            "middle c's octave above sits in a treble staff");
    });

    [TestMethod]
    public void ALineThatNamesItsVoiceFirstIsThatVoicesLine() => UiThread.Run(() =>
    {
        // The chorale shape: every voice declared in the header, then each line of music opening with the voice
        // it belongs to. Filed under the voice before it, every part took its neighbour's clef — the soprano hung
        // off a bass staff and the bass off a treble one. One voice is declared `V: 2`, the way the chorales
        // declare theirs: split on the space before its id, every voice was filed under the same empty one.
        var layout = AbcBuilder.Build(
            "X:1\nL:1/4\nM:C\nV:1 clef=treble\nV: 2 clef=bass\nK:C\n[V:1] c d e f |\n[V:2] C, D, E, F, |\n",
            700, Brushes.Black, 1.0);

        var systems = layout.Root.SelfAndDescendants().Where(n => n.Kind == "system").ToList();
        Assert.AreEqual(2, systems.Count, "one staff per voice");

        foreach (var system in systems)
        {
            var staff = system.SelfAndDescendants().Where(n => n.Kind == "staff-line").ToList();
            foreach (var head in system.SelfAndDescendants().Where(n => n.Kind == "head").Select(n => n.Ink()))
                Assert.IsTrue(head.Top >= staff[0].Bounds.Top - 9 && head.Bottom <= staff[^1].Bounds.Bottom + 9,
                    "each part sits on a staff in its own clef, not its neighbour's");
        }
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

    /// <summary>A score, measured and arranged into a given width at a given zoom.</summary>
    private static Nexaflow.Visuals.Text.Editing.ContentElement Engraved(string abc, double available, double zoom)
    {
        var element = MusicScore.Engraved(MusicDialect.Abc, abc, MarkdownPalette.Dark, zoom: zoom);
        element.Measure(new Size(available, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(0, 0), element.DesiredSize));
        return element;
    }

    /// <summary>The first note in a tune.</summary>
    private static Piece Note(Laid layout) =>
        layout.Root.SelfAndDescendants().First(n => n.Kind == "note");

    /// <summary>How many things a piece of layout drew — the cheap way to ask whether a mark landed.</summary>
    private static int Marks(Piece node)
    {
        // Counted rather than summed: a piece's marks are a span over the tree's own array, and a span
        // cannot be captured by a lambda.
        var marks = 0;
        foreach (var piece in node.SelfAndDescendants()) marks += piece.Marks.Length;
        return marks;
    }

    [TestMethod]
    public void ItPaintsWithoutFaulting() => UiThread.Run(() =>
    {
        // Rendering to a bitmap forces OnRender to run — measure and arrange alone would not — so this
        // exercises every draw path end to end: clef, key, meter, heads, stems, flags, beams, bar lines.
        var element = MusicScore.Engraved(MusicDialect.Abc, SpeedThePlough, MarkdownPalette.Dark);
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
        var layout = AbcBuilder.Build(SpeedThePlough, 600, Brushes.Black, 1.0);

        var systems = layout.Root.SelfAndDescendants().Where(n => n.Kind == "system").ToList();
        Assert.IsTrue(systems.Count >= 2, "the tune should not fit on one line at this width");

        var rights = systems.Take(systems.Count - 1).Select(s => s.Bounds.Right).ToList();
        foreach (var right in rights)
            Assert.AreEqual(rights[0], right, 2.0, "every full system fills the same width");
    });
}
