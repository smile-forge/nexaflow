using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using Nexaflow.Visuals.Text.Markdown.Music.Parsers;

namespace Nexaflow.Tests.Core.Visuals.Markdown.Music;

/// <summary>
/// The ABC parser against the canonical "Speed the Plough" tune — headers, key/meter, 16 measures with
/// the right repeat bar lines, beat-complete measures, and whitespace beam grouping.
/// </summary>
[TestClass]
[CoversNode("abc-core")]
public class AbcParserTests
{
    private const string SpeedThePlough =
        "X:1\n" +
        "T:Speed the Plough\n" +
        "M:4/4\n" +
        "C:Trad.\n" +
        "K:G\n" +
        "|:GABc dedB|dedB dedB|c2ec B2dB|c2A2 A2BA|\n" +
        "  GABc dedB|dedB dedB|c2ec B2dB|A2F2 G4:|\n" +
        "|:g2gf gdBd|g2f2 e2d2|c2ec B2dB|c2A2 A2df|\n" +
        "  g2gf g2Bd|g2f2 e2d2|c2ec B2dB|A2F2 G4:|\n";

    private static Staff ParseStaff() => new AbcParser().Parse(SpeedThePlough).Staves[0];

    [TestMethod]
    public void Headers_TitleComposerKeyMeterClef()
    {
        var score = new AbcParser().Parse(SpeedThePlough);
        Assert.AreEqual("Speed the Plough", score.Title);
        Assert.AreEqual("Trad.", score.Composer);
        var st = score.Staves[0];
        Assert.AreEqual(1, st.Key.Fifths, "G major = 1 sharp");
        Assert.AreEqual(new TimeSignature(4, 4), st.Time);
        Assert.AreEqual(ClefKind.Treble, st.Clef);
    }

    [TestMethod]
    public void Structure_SixteenMeasures_WithRepeatBarlines()
    {
        var st = ParseStaff();
        Assert.AreEqual(16, st.Measures.Count, "four lines of four bars");
        Assert.AreEqual(BarlineKind.RepeatStart, st.Measures[0].StartBarline);
        Assert.AreEqual(BarlineKind.RepeatEnd, st.Measures[7].EndBarline, "end of line 2");
        Assert.AreEqual(BarlineKind.RepeatStart, st.Measures[8].StartBarline, "start of line 3");
        Assert.AreEqual(BarlineKind.RepeatEnd, st.Measures[15].EndBarline, "final bar");
    }

    [TestMethod]
    public void SourceLineBreaks_BecomeSystemBreaks()
    {
        // Each of the four ABC body lines ends at a bar line → a suggested system break on measures 4, 8, 12, 16.
        var st = ParseStaff();
        foreach (int end in new[] { 3, 7, 11, 15 })
            Assert.IsTrue(st.Measures[end].SystemBreak, $"expected a system break after measure {end + 1}");
        Assert.IsFalse(st.Measures[0].SystemBreak, "no break mid-line");
    }

    [TestMethod]
    public void EveryMeasure_IsBeatComplete()
    {
        var st = ParseStaff();
        for (int m = 0; m < st.Measures.Count; m++)
        {
            double sum = 0;
            foreach (var ev in st.Measures[m].Events) sum += ev.Duration.QuarterLength;
            Assert.AreEqual(4.0, sum, 0.001, $"measure {m + 1} should hold 4 quarter-lengths");
        }
    }

    [TestMethod]
    public void FirstMeasure_EightEighths_StartingOnG4()
    {
        var m0 = ParseStaff().Measures[0];
        Assert.AreEqual(8, m0.Events.Count);
        var first = (Note)m0.Events[0];
        Assert.AreEqual('G', first.Pitch.Letter);
        Assert.AreEqual(4, first.Pitch.Octave);
        Assert.AreEqual(8, first.Duration.Base, "eighth note (L:1/8 default)");
    }

    [TestMethod]
    public void FirstMeasure_TwoBeamGroupsOfFour()
    {
        var m0 = ParseStaff().Measures[0];
        var ids = m0.Events.Select(e => e.BeamId).ToList();
        // "GABc dedB" → {G,A,B,c} one beam, {d,e,d,B} another; the space breaks the beam.
        Assert.IsTrue(ids[0] != 0 && ids[0] == ids[1] && ids[1] == ids[2] && ids[2] == ids[3], "first beam group of 4");
        Assert.IsTrue(ids[4] != 0 && ids[4] == ids[5] && ids[5] == ids[6] && ids[6] == ids[7], "second beam group of 4");
        Assert.AreNotEqual(ids[0], ids[4], "the two groups are distinct beams");
    }

    [TestMethod]
    public void FSharp_ComesFromKey_NotAnExplicitAccidental()
    {
        // "A2F2" in bar 8: the F is F#, but implied by the key signature — no drawn accidental.
        var st = ParseStaff();
        var fNote = st.Measures[7].Events.OfType<Note>().First(n => n.Pitch.Letter == 'F');
        Assert.AreEqual(1, fNote.Pitch.Alter, "F is sharp in G major");
        Assert.AreEqual(AccidentalKind.None, fNote.Accidental, "key-signature sharp is not drawn on the note");
    }
}
