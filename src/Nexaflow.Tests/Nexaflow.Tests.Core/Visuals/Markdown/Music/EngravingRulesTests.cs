using System.Collections.Generic;
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

    // ── Layout: note spacing ────────────────────────────────────────────────

    [TestMethod]
    public void ShorterNotes_TakeLessRoomThanLongerOnes()
    {
        // A whole note is not eight times an eighth — the curve is compressed — but it is decidedly wider,
        // and every step up the ladder gains room.
        var layout = Layout("X:1\nM:C\nL:1/16\nK:C\nA/2 A A2 A4 A8 A16 |]\n", 900);
        var gaps = Gaps(layout.Systems[0].Measures[0]);

        for (int i = 1; i < gaps.Count; i++)
            Assert.IsTrue(gaps[i] > gaps[i - 1] + 0.5,
                $"a note of {i + 1} steps' value should sit wider than the one before it ({gaps[i - 1]:F1} → {gaps[i]:F1})");
    }

    /// <summary>
    /// A syllable is centred under its note head, so it only needs half of itself on each side. Charging a note
    /// the <em>full</em> width of its own syllable — which is what made a sung line lurch — over-pays by about
    /// double: on this line it would push the first gap to roughly 4× the plain one instead of 2×.
    /// </summary>
    [TestMethod]
    public void ASungNote_IsChargedHalfItsSyllable_NotAllOfIt()
    {
        var layout = Layout("X:1\nM:4/4\nL:1/4\nK:C\nA A A A A A |\nw:extraordinarily by a to be it\n", 900);
        var gaps = Gaps(layout.Systems[0].Measures[0]);

        double min = gaps.Min(), max = gaps.Max();
        Assert.IsTrue(max <= min * 2.5,
            $"even the longest syllable should not blow the line apart (gaps {min:F1}–{max:F1})");
        Assert.IsTrue(max > min * 1.1, "…but a long syllable does still ask for room");
    }

    // ── Layout: a bracketed system ──────────────────────────────────────────

    private const string PartSong =
        "X:1\nT:Part song\nM:C\nL:1/4\n" +
        "V: P1 name=\"Soprano\"\nV: P2 name=\"Alto\"\nV: P3 name=\"Bass\"\nK:C\n" +
        "[V: P1] cdec | gfed | cdec | g4 |]\n" +
        "[V: P2] GABG | ecBA | GABG | c4 |]\n" +
        "[V: P3] C,E,G,C, | C,2 G,,2 | C,E,G,C, | C,4 |]\n";

    /// <summary>What makes several voices a <em>system</em> rather than a stack: the same bars at the same x on
    /// every staff. Without that the bar lines don't line up, and a reader can't tell the parts are sounding
    /// together.</summary>
    [TestMethod]
    public void VoicesThatRunInStep_ShareOneBarGrid()
    {
        var layout = Layout(PartSong, 900);

        Assert.AreEqual(1, layout.Groups.Count, "one system, holding all three voices");
        var group = layout.Groups[0];
        Assert.AreEqual(3, group.Staves.Count);
        Assert.IsTrue(group.IsBracketed);

        var lead = group.Staves[0];
        foreach (var sys in group.Staves)
        {
            Assert.AreEqual(lead.ContentStartX, sys.ContentStartX, 0.01, "every voice starts at the same x");
            Assert.AreEqual(lead.RightX, sys.RightX, 0.01, "…and ends at the same x");
            Assert.AreEqual(lead.Measures.Count, sys.Measures.Count);
            for (int k = 0; k < lead.Measures.Count; k++)
                Assert.AreEqual(lead.Measures[k].EndX, sys.Measures[k].EndX, 0.01,
                    $"bar {k + 1} must end at the same x on every staff, or the bar lines don't line up");
        }
    }

    [TestMethod]
    public void ABassVoice_GetsABassStaff()
    {
        var layout = Layout(PartSong, 900);
        var staves = layout.Groups[0].Staves;
        Assert.AreEqual(StaffGeometry.For(ClefKind.Treble).ClefGlyph, staves[0].Geom.ClefGlyph);
        Assert.AreEqual(StaffGeometry.For(ClefKind.Bass).ClefGlyph, staves[2].Geom.ClefGlyph);
        Assert.AreEqual("Soprano", staves[0].StaffName);
        Assert.AreEqual("Bass", staves[2].StaffName);
    }

    /// <summary>Voices the source barred differently aren't a system — they fall back to an honest stack rather
    /// than a false alignment.</summary>
    [TestMethod]
    public void VoicesThatDoNotRunInStep_AreNotBracketed()
    {
        var layout = Layout(
            "X:1\nM:C\nL:1/4\nV: P1\nV: P2\nK:C\n[V: P1] cdec | gfed |]\n[V: P2] GABG |]\n", 900);

        Assert.AreEqual(2, layout.Groups.Count, "one group per staff — a stack, not a system");
        foreach (var g in layout.Groups) Assert.IsFalse(g.IsBracketed);
    }

    // ── Layout: room above the staff ────────────────────────────────────────

    /// <summary>A chord symbol belongs above the <em>music</em>, and how high that is depends on how high the
    /// music went. Pinned a fixed distance above the top line, it collided with anything reaching over it.</summary>
    [TestMethod]
    public void ChordSymbols_ClearTheNotesBeneathThem_HoweverHighTheyReach()
    {
        var low = Layout("X:1\nM:C\nL:1/4\nK:C\n\"Dm\"G \"A7\"G G G |]\n", 900).Systems[0];
        var high = Layout("X:1\nM:C\nL:1/4\nK:C\n\"Dm\"g' \"A7\"g' g' g' |]\n", 900).Systems[0];

        Assert.IsTrue(high.AboveMusic > low.AboveMusic + 2 * 8.0,
            "notes two octaves higher need more head-room");
        Assert.IsTrue(high.TopLineY - high.ChordTextTop > low.TopLineY - low.ChordTextTop,
            "…and the chord symbols move up with them rather than sitting on their ledger lines");
    }

    private static ScoreLayout Layout(string abc, double width) =>
        new ScoreLayoutEngine(new AbcParser().Parse(abc), 8.0, 1.0).Build(width);

    private static List<double> Gaps(MeasureLayout ml)
    {
        var gaps = new List<double>();
        for (int i = 1; i < ml.Events.Count; i++)
            gaps.Add(ml.Events[i].HeadX - ml.Events[i - 1].HeadX);
        return gaps;
    }
}
