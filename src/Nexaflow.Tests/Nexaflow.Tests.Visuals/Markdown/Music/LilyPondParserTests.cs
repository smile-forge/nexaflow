using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using Nexaflow.Visuals.Text.Markdown.Music.Parsers;

namespace Nexaflow.Tests.Visuals.Markdown.Music;

/// <summary>
/// The LilyPond parser end to end: a simple top-level tune, and the complex "Exercise 3" example — a real
/// worksheet, with Scheme, a <c>\score</c>, a <c>PianoStaff</c>, figured bass and a <c>\markup</c> title around
/// the music. Both staves of the exercise are engraved (the empty upper one the student writes into, and the
/// given cantus firmus below it), resolving <c>\global</c> and <c>\relative</c> along the way.
///
/// The language itself is covered construct-by-construct in <see cref="LilyPondSyntaxTests"/>.
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
        Assert.AreEqual(4, st.Time.Numerator);
        Assert.AreEqual(4, st.Time.Denominator);
        Assert.AreEqual(TimeSymbol.Common, st.Time.Symbol, "LilyPond draws 4/4 as the C symbol");
        Assert.AreEqual(2, st.Measures.Count, "c d e f | g1 → two measures");
        Assert.AreEqual(4, st.Measures[0].Events.Count);
        var first = (Note)st.Measures[0].Events[0];
        Assert.AreEqual('C', first.Pitch.Letter);
    }

    [TestMethod]
    public void Exercise3_EngravesBothStavesOfThePianoStaff()
    {
        var score = new LilyPondParser().Parse(Exercise3);
        Assert.AreEqual(2, score.Staves.Count, "the PianoStaff holds the blank upper staff and the given bass");

        var upper = score.Staves[0];
        Assert.AreEqual(4, upper.Measures.Count);
        Assert.IsFalse(upper.Measures.SelectMany(m => m.Events).OfType<Note>().Any(),
            "the upper staff is the one the student fills in — rests and spacers only");
        Assert.AreEqual(BarlineKind.Double, upper.Measures[^1].EndBarline, "\\bar \"||\" closes it");

        var cf = score.Staves[1];
        Assert.AreEqual(ClefKind.Bass, cf.Clef, "\\clef bass");
        Assert.AreEqual(0, cf.Key.Fifths, "\\key c \\major via \\global");
        Assert.AreEqual(4, cf.Time.Numerator, "\\time 4/4 via \\global");
        Assert.AreEqual(TimeSymbol.Numeric, cf.Time.Symbol, "…which \\numericTimeSignature prints as figures");
        Assert.AreEqual(4, cf.Measures.Count, "cf is four bars");
        Assert.AreEqual(13, cf.Measures.SelectMany(m => m.Events).OfType<Note>().Count(), "cf has 13 notes (4+4+4+1)");
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
