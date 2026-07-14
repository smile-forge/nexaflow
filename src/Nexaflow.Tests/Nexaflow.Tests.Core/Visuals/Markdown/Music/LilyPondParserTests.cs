using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using Nexaflow.Visuals.Text.Markdown.Music.Parsers;

namespace Nexaflow.Tests.Core.Visuals.Markdown.Music;

/// <summary>
/// The LilyPond subset parser: a simple top-level tune, and the complex "Exercise 3" example — which it
/// must not choke on. From the exercise it renders the cantus-firmus (<c>cf</c>) staff, resolving
/// <c>\global</c> (time/key) and <c>\relative</c>, while tolerating Scheme, <c>\score</c>/PianoStaff,
/// <c>\figuremode</c> and <c>\markup</c>.
/// </summary>
[TestClass]
[CoversNode("ly-core")]
public class LilyPondParserTests
{
    private const string Exercise3 =
        "#(ly:set-option 'point-and-click #f)\n" +
        "#(set-global-staff-size 24)\n\n" +
        "global = {\n  \\time 4/4\n  \\numericTimeSignature\n  \\key c \\major\n}\n\n" +
        "cf = \\relative {\n  \\clef bass\n  \\global\n  c4 c' b a |\n  g a f d |\n  e f g g, |\n  c1\n}\n\n" +
        "upper = \\relative c'' {\n  \\global\n  r4 s4 s2 |\n  s1*2 |\n  s2 s4 s\n  \\bar \"||\"\n}\n\n" +
        "bassFigures = \\figuremode {\n  s1*2 | s4 <6> <6 4> <7> | s1\n}\n\n" +
        "\\markup { \"Exercise 3: Write 8th notes against the given bass line.\" }\n\n" +
        "\\score {\n  \\new PianoStaff <<\n    \\new Staff { \\upper }\n    \\new Staff = lower { << \\cf \\new FiguredBass \\bassFigures >> }\n  >>\n  \\layout {}\n}\n";

    [TestMethod]
    public void SimpleTune_Parses()
    {
        var score = new LilyPondParser().Parse("\\relative c' { \\clef treble \\key c \\major \\time 4/4 c4 d e f | g1 }");
        Assert.AreEqual(1, score.Staves.Count);
        var st = score.Staves[0];
        Assert.AreEqual(ClefKind.Treble, st.Clef);
        Assert.AreEqual(new TimeSignature(4, 4), st.Time);
        Assert.AreEqual(2, st.Measures.Count, "c d e f | g1 → two measures");
        Assert.AreEqual(4, st.Measures[0].Events.Count);
        var first = (Note)st.Measures[0].Events[0];
        Assert.AreEqual('C', first.Pitch.Letter);
    }

    [TestMethod]
    public void Exercise3_DoesNotThrow_AndRendersCantusFirmus()
    {
        var score = new LilyPondParser().Parse(Exercise3);
        Assert.AreEqual(1, score.Staves.Count, "renders exactly one staff (the cf)");
        var st = score.Staves[0];
        Assert.AreEqual(ClefKind.Bass, st.Clef, "\\clef bass");
        Assert.AreEqual(0, st.Key.Fifths, "\\key c \\major via \\global");
        Assert.AreEqual(new TimeSignature(4, 4), st.Time, "\\time 4/4 via \\global");
        Assert.AreEqual(4, st.Measures.Count, "cf is four bars");

        int notes = 0;
        foreach (var m in st.Measures) foreach (var e in m.Events) if (e is Note) notes++;
        Assert.AreEqual(13, notes, "cf has 13 notes (4+4+4+1)");
    }

    [TestMethod]
    public void Exercise3_ExtractsMarkupTitle_AndWarns()
    {
        var score = new LilyPondParser().Parse(Exercise3);
        StringAssert.Contains(score.Title ?? "", "Exercise 3");
        Assert.IsTrue(score.Warnings.Count > 0, "unsupported constructs should be surfaced");
    }

    [TestMethod]
    public void Relative_ResolvesOctaves()
    {
        // c c' b a → C3, C4, B3, A3 with the default (C3) reference.
        var score = new LilyPondParser().Parse("\\relative { \\clef bass c4 c' b a }");
        var evs = score.Staves[0].Measures[0].Events;
        var p = (Note)evs[0];
        var p2 = (Note)evs[1];
        Assert.AreEqual(p.Pitch.Octave + 1, p2.Pitch.Octave, "the ' mark raises c by an octave");
        Assert.AreEqual('C', p.Pitch.Letter);
        Assert.AreEqual('B', ((Note)evs[2]).Pitch.Letter);
    }
}
