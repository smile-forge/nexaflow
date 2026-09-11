using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Nexaflow.Markdown.Music;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;

namespace Nexaflow.Tests.Visuals.Markdown.Music;

/// <summary>
/// The engraving judgements the builder makes — which way a stem points, how a beam leans, and how the room on
/// a line is shared out. The first two are pure geometry, so they are asserted directly rather than inferred
/// from a picture; the third is asked of the laid tree.
/// </summary>
[TestClass]
public class EngravingRulesTests
{
    private static readonly StaffGeometry Treble = StaffGeometry.For(ClefKind.Treble);

    /// <summary>Where a natural note sits on the treble staff, in half-spaces above its bottom line.</summary>
    private static int At(char letter, int octave) =>
        Treble.HalfSpacesAbove(new Pitch(Pitch.Letters.IndexOf(letter), 0, octave).DiatonicIndex);

    // ── Stem direction ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("sr-notes")]
    public void StemFlips_AtTheMiddleLine()
    {
        // Treble middle line is B4. Everything below it stems up; the middle line itself and above stem
        // down. The middle line is a tie either way is valid for, so this asserts a convention rather than
        // a rule — the one the corpus's own engraver keeps across all ten thousand of its tunes.
        Assert.IsFalse(Engraving.StemDown([At('C', 4)]), "middle C, below the staff");
        Assert.IsFalse(Engraving.StemDown([At('A', 4)]), "second space");
        Assert.IsTrue(Engraving.StemDown([At('B', 4)]), "ON the middle line — the tie goes down");
        Assert.IsTrue(Engraving.StemDown([At('C', 5)]), "third space");
        Assert.IsTrue(Engraving.StemDown([At('G', 5)]), "top line");
    }

    [TestMethod]
    [CoversNode("sr-notes")]
    public void BeamGroup_TakesTheDirectionOfTheNoteFurthestFromTheMiddleLine()
    {
        // G A B c reaches two half-spaces below the middle line and only one above → the group stems up.
        Assert.IsFalse(Engraving.StemDown([At('G', 4), At('A', 4), At('B', 4), At('C', 5)]));

        // d e d B reaches three above and none below → down.
        Assert.IsTrue(Engraving.StemDown([At('D', 5), At('E', 5), At('D', 5), At('B', 4)]));
    }

    [TestMethod]
    [CoversNode("sr-notes")]
    public void Chord_StemsFromItsOutermostNote()
    {
        // C E c: the top reaches 1 above the middle line, but the bottom reaches 6 below.
        Assert.IsFalse(Engraving.StemDown([At('C', 4), At('E', 4), At('C', 5)]));
    }

    // ── Beam slope ──────────────────────────────────────────────────────────

    private static double Slope(params double[] outerY) =>
        Engraving.BeamSlope([.. Enumerable.Range(0, outerY.Length).Select(i => i * 20.0)], outerY);

    [TestMethod]
    [CoversNode("sr-beaming")]
    public void Beam_Rises_WhenTheGroupRises()
    {
        double s = Slope(100, 96, 92, 88);          // pitch climbing (y shrinking)
        Assert.IsTrue(s < 0, "a rising group beams upward to the right");
    }

    [TestMethod]
    [CoversNode("sr-beaming")]
    public void Beam_Falls_WhenTheGroupFalls()
    {
        Assert.IsTrue(Slope(88, 92, 96, 100) > 0);
    }

    [TestMethod]
    [CoversNode("sr-beaming")]
    public void Beam_IsFlat_WhenTheGroupIsNotMonotonic()
    {
        // ABcdABcd — two rising runs, but the contour is a zig-zag, so the beam must not lean.
        Assert.AreEqual(0, Slope(100, 96, 92, 88, 100, 96, 92, 88), 1e-9,
            "a beam drawn through a zig-zag would assert a direction the music doesn't have");
        Assert.AreEqual(0, Slope(100, 100, 100), 1e-9, "a repeated note beams flat");
    }

    [TestMethod]
    [CoversNode("sr-beaming")]
    public void Beam_NeverExceedsItsSlopeCap_HoweverBigTheInterval()
    {
        double s = Slope(200, 100);                 // a huge leap over one note-width
        Assert.IsTrue(System.Math.Abs(s) <= 0.25 + 1e-9, "the beam stays a beam, not a staircase");
    }

    // ── Layout: line widths ─────────────────────────────────────────────────

    /// <summary>The layout rule the user's eye catches first: every system but a short tail reaches the same
    /// right edge, and the tail — though ragged — keeps the same note spacing as the lines above it, rather
    /// than bunching up at its natural width.</summary>
    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("sr-layout")]
    public void ShortFinalSystem_IsRagged_ButKeepsTheSpacingOfTheLinesAboveIt() => UiThread.Run(() =>
    {
        const string Tune =
            "X:1\nT:t\nM:4/4\nK:G\n" +
            "GABc dedB|dedB dedB|c2ec B2dB|c2A2 A2BA|\n" +
            "GABc dedB|dedB dedB|c2ec B2dB|A2F2 G4|\n" +
            "GABc dedB|dedB dedB|\n";                       // a two-bar tail

        var systems = Systems(Tune, 900);
        Assert.AreEqual(3, systems.Count);

        Assert.AreEqual(Right(systems[0]), Right(systems[1]), 1.5, "the two full lines share one width");
        Assert.IsTrue(Right(systems[2]) < Right(systems[0]) - 20, "the two-bar tail is not stretched across the page");

        Assert.AreEqual(Gaps(systems[0]).Average(), Gaps(systems[2]).Average(), 2.0,
            "…but its notes are spaced like the lines above, not squeezed to their natural width");
    });

    // ── Layout: note spacing ────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("sr-layout")]
    public void ShorterNotes_TakeLessRoomThanLongerOnes() => UiThread.Run(() =>
    {
        // A whole note is not eight times an eighth — the curve is compressed — but it is decidedly wider,
        // and every step up the ladder gains room.
        var gaps = Gaps(Systems("X:1\nM:C\nL:1/16\nK:C\nA/2 A A2 A4 A8 A16 |]\n", 900)[0]);

        for (var i = 1; i < gaps.Count; i++)
            Assert.IsTrue(gaps[i] > gaps[i - 1] + 0.5,
                $"a note of {i + 1} steps' value should sit wider than the one before it ({gaps[i - 1]:F1} → {gaps[i]:F1})");
    });

    /// <summary>
    /// A syllable is centred under its note head, so it only needs half of itself on each side. Charging a note
    /// the <em>full</em> width of its own syllable — which is what made a sung line lurch — over-pays by about
    /// double: on this line it would push the first gap to roughly 4× the plain one instead of 2×.
    /// </summary>
    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("sr-layout")]
    public void ASungNote_IsChargedHalfItsSyllable_NotAllOfIt() => UiThread.Run(() =>
    {
        var gaps = Gaps(Systems("X:1\nM:4/4\nL:1/4\nK:C\nA A A A A A |\nw:extraordinarily by a to be it\n", 900)[0]);

        double min = gaps.Min(), max = gaps.Max();
        Assert.IsTrue(max <= min * 2.5,
            $"even the longest syllable should not blow the line apart (gaps {min:F1}–{max:F1})");
        Assert.IsTrue(max > min * 1.1, "…but a long syllable does still ask for room");
    });

    // ── Layout: room above the staff ────────────────────────────────────────

    /// <summary>A chord symbol belongs above the <em>music</em>, and how high that is depends on how high the
    /// music went. Pinned a fixed distance above the top line, it collided with anything reaching over it.</summary>
    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("sr-layout")]
    public void ChordSymbols_ClearTheNotesBeneathThem_HoweverHighTheyReach() => UiThread.Run(() =>
    {
        var low = Systems("X:1\nM:C\nL:1/4\nK:C\n\"Dm\"G \"A7\"G G G |]\n", 900)[0];
        var high = Systems("X:1\nM:C\nL:1/4\nK:C\n\"Dm\"g' \"A7\"g' g' g' |]\n", 900)[0];

        // What was drawn rather than the room reserved: a glyph's box is far taller than its ink.
        foreach (var system in new[] { low, high })
            Assert.IsTrue(All(system, "chord").Max(c => c.Ink().Bottom) <= All(system, "head").Min(h => h.Ink().Top),
                "a chord symbol sits over every note beneath it, not on its ledger lines");

        Assert.IsTrue(Top(high) - All(high, "chord")[0].Ink().Top > Top(low) - All(low, "chord")[0].Ink().Top + (2 * 8.0),
            "…and moves up with notes two octaves higher rather than keeping a fixed distance from the staff");
    });

    // ── Reading the picture ─────────────────────────────────────────────────

    private static List<Piece> Systems(string abc, double width) =>
        All(AbcBuilder.Build(abc, width, Brushes.Black, 1.0).Root, "system");

    private static List<Piece> All(Piece root, string kind) => [.. root.SelfAndDescendants().Where(p => p.Kind == kind)];

    private static double Right(Piece system) => All(system, "staff-line").Max(line => line.Bounds.Right);

    private static double Top(Piece system) => All(system, "staff-line").Min(line => line.Bounds.Top);

    /// <summary>The distance from each note head to the next in a system's first bar.</summary>
    private static List<double> Gaps(Piece system)
    {
        var xs = All(All(system, "measure")[0], "head").Select(head => head.Bounds.Left).Order().ToList();

        var gaps = new List<double>();
        for (var i = 1; i < xs.Count; i++) gaps.Add(xs[i] - xs[i - 1]);
        return gaps;
    }
}
