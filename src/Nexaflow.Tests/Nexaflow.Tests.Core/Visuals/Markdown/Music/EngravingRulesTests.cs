using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using Nexaflow.Visuals.Text.Markdown.Music.Parsers;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;

namespace Nexaflow.Tests.Core.Visuals.Markdown.Music;

/// <summary>
/// The two engraving judgements the renderer makes — which way a stem points, and how a beam leans. Both are
/// pure geometry, so they are asserted directly rather than inferred from a bitmap.
/// </summary>
[TestClass]
[CoversNode("sr-notes")]
[CoversNode("sr-beaming")]
[CoversNode("sr-layout")]
public class EngravingRulesTests
{
    private static readonly StaffGeometry Treble = StaffGeometry.For(ClefKind.Treble);

    private static Note At(char letter, int octave) =>
        new() { Pitch = new Pitch(System.Array.IndexOf(Pitch.StepLetters, letter), 0, octave) };

    // ── Stem direction ──────────────────────────────────────────────────────

    [TestMethod]
    public void StemFlips_StrictlyAboveTheMiddleLine()
    {
        // Treble middle line is B4. Everything up to and including it stems up; c5 and above stem down.
        Assert.IsFalse(Engraving.StemDown(At('C', 4), Treble), "middle C, below the staff");
        Assert.IsFalse(Engraving.StemDown(At('A', 4), Treble), "second space");
        Assert.IsFalse(Engraving.StemDown(At('B', 4), Treble), "ON the middle line — stem up");
        Assert.IsTrue(Engraving.StemDown(At('C', 5), Treble), "third space — the first note that stems down");
        Assert.IsTrue(Engraving.StemDown(At('G', 5), Treble), "top line");
    }

    [TestMethod]
    public void BeamGroup_TakesTheDirectionOfTheNoteFurthestFromTheMiddleLine()
    {
        // G A B c reaches two half-spaces below the middle line and only one above → the group stems up.
        MusicalEvent[] up = [At('G', 4), At('A', 4), At('B', 4), At('C', 5)];
        Assert.IsFalse(Engraving.StemDown(up, Treble));

        // d e d B reaches three above and none below → down.
        MusicalEvent[] down = [At('D', 5), At('E', 5), At('D', 5), At('B', 4)];
        Assert.IsTrue(Engraving.StemDown(down, Treble));
    }

    [TestMethod]
    public void Chord_StemsFromItsOutermostNote()
    {
        var low = new Chord();
        low.Notes.Add(At('C', 4));
        low.Notes.Add(At('E', 4));
        low.Notes.Add(At('C', 5));       // reaches 1 above the middle line, but C4 reaches 6 below
        Assert.IsFalse(Engraving.StemDown(low, Treble));
    }

    // ── Beam slope ──────────────────────────────────────────────────────────

    private static double Slope(params double[] outerY) =>
        Engraving.BeamSlope([.. Enumerable.Range(0, outerY.Length).Select(i => i * 20.0)], outerY);

    [TestMethod]
    public void Beam_Rises_WhenTheGroupRises()
    {
        double s = Slope(100, 96, 92, 88);          // pitch climbing (y shrinking)
        Assert.IsTrue(s < 0, "a rising group beams upward to the right");
    }

    [TestMethod]
    public void Beam_Falls_WhenTheGroupFalls()
    {
        Assert.IsTrue(Slope(88, 92, 96, 100) > 0);
    }

    [TestMethod]
    public void Beam_IsFlat_WhenTheGroupIsNotMonotonic()
    {
        // ABcdABcd — two rising runs, but the contour is a zig-zag, so the beam must not lean.
        Assert.AreEqual(0, Slope(100, 96, 92, 88, 100, 96, 92, 88), 1e-9,
            "a beam drawn through a zig-zag would assert a direction the music doesn't have");
        Assert.AreEqual(0, Slope(100, 100, 100), 1e-9, "a repeated note beams flat");
    }

    [TestMethod]
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
    public void ShortFinalSystem_IsRagged_ButKeepsTheSpacingOfTheLinesAboveIt()
    {
        const string tune =
            "X:1\nT:t\nM:4/4\nK:G\n" +
            "GABc dedB|dedB dedB|c2ec B2dB|c2A2 A2BA|\n" +
            "GABc dedB|dedB dedB|c2ec B2dB|A2F2 G4|\n" +
            "GABc dedB|dedB dedB|\n";                       // a two-bar tail

        var layout = new ScoreLayoutEngine(new AbcParser().Parse(tune), 8.0, 1.0).Build(900);
        Assert.AreEqual(3, layout.Systems.Count);

        Assert.AreEqual(layout.Systems[0].RightX, layout.Systems[1].RightX, 1.5,
            "the two full lines share one width");
        Assert.IsTrue(layout.Systems[2].RightX < layout.Systems[0].RightX - 20,
            "the two-bar tail is not stretched across the page");

        Assert.AreEqual(NoteGap(layout.Systems[0]), NoteGap(layout.Systems[2]), 2.0,
            "…but its notes are spaced like the lines above, not squeezed to their natural width");
    }

    /// <summary>Mean distance between adjacent note heads in a system's first bar.</summary>
    private static double NoteGap(SystemLayout sys)
    {
        var xs = sys.Measures[0].Events.Select(e => e.HeadX).ToArray();
        double total = 0;
        for (int i = 1; i < xs.Length; i++) total += xs[i] - xs[i - 1];
        return total / (xs.Length - 1);
    }
}
