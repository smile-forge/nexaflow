using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.LilyPond;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music.LilyPond;

namespace Nexaflow.Tests.Visuals.Markdown.Music.LilyPond;

/// <summary>
/// The LilyPond builder, asked of the picture it makes.
///
/// <para>
/// The three things LilyPond leaves to whoever engraves it get the most attention, because they are where a
/// LilyPond engraving can be wrong in a way an ABC one cannot: bars come from the meter rather than from a typed
/// <c>|</c>, beams come from the meter rather than from the spacing, and a printed accidental is a decision rather
/// than something the source asked for. All three are only known once the music is played through — which is
/// the builder's job, and why they are tested here rather than against the tree.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("ly-layout")]
public class LilyPondBuilderTests
{
    // ── What every picture promises ─────────────────────────────────────────

    [TestMethod]
    public void EveryPieceOfThePictureNamesARealPartOfTheSourceOrNothingAtAll() => UiThread.Run(() =>
    {
        foreach (var (what, ly) in LilyPondConstructs.Everything)
        {
            var layout = Lay(ly);
            var parts = ContentReading.Of(LilyPondPipeline.Read(ly)).Root
                .SelfAndDescendants()
                .Select(p => (p.Kind, p.Start, p.Length))
                .ToHashSet();

            ContentPart? root = null;

            foreach (var node in layout.Root.SelfAndDescendants())
            {
                if (node.Part is null) continue;

                if (node.Part is ContentPart part)
                {
                    Assert.IsTrue(parts.Contains((part.Kind, part.Start, part.Length)),
                        $"{what}: {node.Kind} names a part that is not in this reading");

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
                Assert.IsTrue(at.Start >= 0 && at.End <= ly.Length,
                    $"{what}: {node.Kind} claims {at.Start}+{at.Length} of {ly.Length}");
            }
        }
    });

    [TestMethod]
    public void EveryConstructEngravesAStaff_AndNothingInItIsMarked() => UiThread.Run(() =>
    {
        foreach (var (what, ly) in LilyPondConstructs.Everything)
        {
            var layout = Lay(ly);

            Assert.IsTrue(layout.Size.Width > 0 && layout.Size.Height > 0, $"{what}: engraved to nothing");
            Assert.IsTrue(All(layout, "system").Count > 0, $"{what}: no staff");
            Assert.AreEqual(0, layout.Trouble.Count,
                $"{what}: {string.Join("; ", layout.Trouble.Select(t => t.Message))}");
        }
    });

    // ── Bars come from the meter ────────────────────────────────────────────

    [TestMethod]
    public void BarsAreClosedByTheMeter_WithNoBarLinesTyped() => UiThread.Run(() =>
    {
        // Not one '|' in the source. In LilyPond a bar line is implied, and getting this wrong is the biggest
        // way a LilyPond engraving can differ from an ABC one that looks the same.
        CollectionAssert.AreEqual(new[] { 3, 3, 3 }, NotesPerBar(Lay(@"\relative c' { \time 3/4 c4 d e f g a b c d }")));
    });

    [TestMethod]
    public void ABarCheckNamesTheLineItChecks() => UiThread.Run(() =>
    {
        const string Ly = @"\relative c' { \time 4/4 c4 d e f | g1 }";
        var lines = All(Lay(Ly), "barline");

        Assert.AreEqual("|", Named(lines[0], Ly), "the check is what a reader points at to point at the line");
    });

    [TestMethod]
    public void AndALineTheMeterImpliesNamesNothing() => UiThread.Run(() =>
    {
        var lines = All(Lay("{ c4 d e f g1 }"), "barline");

        Assert.IsTrue(lines.Count >= 1);
        Assert.IsNull(lines[0].Part, "nobody wrote it, so it cannot be selected");
    });

    [TestMethod]
    public void APickupShortensTheFirstBarOnly() => UiThread.Run(() =>
    {
        CollectionAssert.AreEqual(new[] { 1, 4, 1 },
            NotesPerBar(Lay(@"\relative c' { \time 4/4 \partial 4 g4 | c4 d e f | g1 }")));
    });

    [TestMethod]
    public void AMeterChangeRebarsWhatFollows() => UiThread.Run(() =>
    {
        var layout = Lay(@"\relative c' { \time 4/4 c4 d e f \time 3/4 g4 a b c d e }");

        CollectionAssert.AreEqual(new[] { 4, 3, 3 }, NotesPerBar(layout));
        Assert.IsTrue(All(Bars(layout)[1], "meter").Count > 0, "the new meter is printed where it takes effect");
    });

    [TestMethod]
    public void AWholeBarRestIsABarEach_AndSoIsASpacer() => UiThread.Run(() =>
    {
        var rests = Lay(@"\relative c' { \time 4/4 c1 R1*3 c1 }");
        Assert.AreEqual(5, Bars(rests).Count, "R1*3 is three bars, not one long one");

        var spacers = Lay(@"{ \time 4/4 s1*2 c'1 }");
        Assert.AreEqual(3, Bars(spacers).Count, "s1*2 is two bars of nothing");
        Assert.AreEqual(0, All(spacers, "rest-glyph").Count, "…and prints nothing");
    });

    [TestMethod]
    public void AWrittenBarLineLandsOnTheBarItFollows_EvenWhenTheMeterAlreadyClosedIt() => UiThread.Run(() =>
    {
        // The meter closes the bar after f; the \bar arrives afterwards and still has to be that bar's line.
        const string Ly = @"\relative c' { \time 4/4 c4 d e f \bar ""||"" g1 \bar ""|."" }";
        var layout = Lay(Ly);

        Assert.AreEqual(2, Bars(layout).Count);
        CollectionAssert.AreEqual(new[] { @"\bar ""||""", @"\bar ""|.""" },
            All(layout, "barline").Select(line => Named(line, Ly)).ToArray());
    });

    [TestMethod]
    public void ACadenzaHasNoMeter_AndOnlyAWrittenLineEndsABar() => UiThread.Run(() =>
    {
        var layout = Lay(@"{ \cadenzaOn \autoBeamOff c\breve c1 c2 c4 c8 c16 \bar ""|."" }");

        CollectionAssert.AreEqual(new[] { 6 }, NotesPerBar(layout));
        Assert.AreEqual(0, All(layout, "meter").Count, "free meter prints no signature");
    });

    // ── Repeats ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void ARepeatDrawsItsLines_AndItsEndingsTheirBrackets() => UiThread.Run(() =>
    {
        const string Ly = @"\relative c' { \time 4/4 \repeat volta 2 { c4 d e f } \alternative { { g1 } { a1 } } }";
        var layout = Lay(Ly);

        Assert.AreEqual(3, Bars(layout).Count);
        Assert.AreEqual(2, All(layout, "volta").Count, "one bracket per ending");
        Assert.IsTrue(All(layout, "barline").Any(line => Named(line, Ly) == @"\repeat"),
            "the repeat's opening line is what the \\repeat was written as");
    });

    [TestMethod]
    public void EndingsWrittenWithVoltaAreNumberedAsTheySay() => UiThread.Run(() =>
    {
        // LilyPond's own spelling since 2.24, and abc2ly's: each ending says which time through it is played.
        // Read as a bare command beside its music, every \volta passed for an ending of its own — four endings,
        // numbered 1 to 4, two of them brackets over nothing.
        var classic = Lay(@"\relative c' { \time 4/4 \repeat volta 2 { c4 d e f } \alternative { { g1 } { a1 } } }");
        var numbered = Lay(@"\relative c' { \time 4/4 \repeat volta 2 { c4 d e f } \alternative { \volta 1 { g1 } \volta 2 { a1 } } }");

        Assert.AreEqual(Bars(classic).Count, Bars(numbered).Count, "the same bars either way");
        CollectionAssert.AreEqual(new[] { "1", "2" },
            All(numbered, "volta").Select(v => Printed(v).Trim().TrimEnd('.')).ToArray());
    });

    [TestMethod]
    public void AnUnfoldedRepeatIsWrittenOut_EveryTimeNamingTheSameNotes() => UiThread.Run(() =>
    {
        var layout = Lay(@"\relative c' { \time 4/4 \repeat unfold 3 { c4 d e f } }");
        var notes = All(layout, "note");

        Assert.AreEqual(3, Bars(layout).Count);
        Assert.AreEqual(12, notes.Count);
        Assert.AreEqual(4, notes.Select(n => (n.Part!.Start, n.Part.Length)).Distinct().Count(),
            "twelve notes drawn, four written");
    });

    // ── Beams come from the meter ───────────────────────────────────────────

    [TestMethod]
    [DataRow(@"{ \time 4/4 c8 d e f g a b c }", new[] { 4, 4 }, "eighths in fours in common time")]
    [DataRow(@"{ \time 6/8 c8 d e f g a }", new[] { 3, 3 }, "in threes in a compound meter")]
    [DataRow(@"{ \time 3/4 c8 d e f g a }", new[] { 6 }, "the whole bar under one beam in three-four, as LilyPond does")]
    [DataRow(@"{ \time 3/4 c4 d8 e f g }", new[] { 4 }, "…and whatever of the bar is left after a quarter")]
    [DataRow(@"{ \time 2/4 c8 d e f g a b c }", new[] { 2, 2, 2, 2 }, "but by the beat in two-four")]
    [DataRow(@"{ \time 4/4 c16 d e f g a b c d e f g a b c d }", new[] { 4, 4, 4, 4 }, "sixteenths by the beat")]
    [DataRow(@"{ \time 4/4 c8 d r8 e f g a b }", new[] { 2, 4 }, "a rest breaks the beam")]
    [DataRow(@"{ \time 4/4 c8[ d e] f g a b c }", new[] { 3, 4 }, "a beam by hand wins over the meter")]
    [DataRow(@"{ \time 4/4 c8[ d e] f[ g a b c] }", new[] { 3, 5 }, "two by hand, back to back, keep every note")]
    [DataRow(@"{ \time 4/4 \partial 4 c8 d | e f g a b c d e }", new[] { 2, 4, 4 }, "a beam starts again at the bar line, pickup or not")]
    [DataRow(@"{ \time 4/4 \tuplet 3/2 { c8 d e } \tuplet 3/2 { f g a } c4 c4 }", new[] { 3, 3 }, "a tuplet beams as itself")]
    public void BeamsComeFromTheMeter(string ly, int[] beams, string because) => UiThread.Run(() =>
    {
        CollectionAssert.AreEqual(beams, All(Lay(ly), "beam").Select(beam => All(beam, "note").Count).ToArray(), because);
    });

    // ── Accidentals are an engraving decision ───────────────────────────────

    [TestMethod]
    [DataRow(@"\relative c' { \key g \major fis4 g a b }", 0, "the key signature already says F sharp")]
    [DataRow(@"\relative c' { \key g \major f4 g a b }", 1, "F natural in G major has to be cancelled")]
    [DataRow(@"\relative c' { \time 4/4 fis4 fis fis fis | fis4 g a b }", 2, "printed once a bar, and again after the bar line")]
    [DataRow(@"\relative c' { \time 4/4 fis4 fis' fis, fis }", 2, "an accidental holds only for its own line or space")]
    [DataRow(@"{ \key g \major fis'!4 fis'?4 }", 2, "a ! or ? prints it whatever is in force")]
    [DataRow(@"{ \time 4/4 fis'1~ | fis'1 }", 1, "a note tied over the bar line is not given it again")]
    public void AnAccidentalIsPrintedWhereTheNoteDepartsFromWhatIsInForce(string ly, int printed, string because) =>
        UiThread.Run(() => Assert.AreEqual(printed, All(Lay(ly), "accidental").Count, because));

    // ── Keys and meter ──────────────────────────────────────────────────────

    [TestMethod]
    [DataRow("c", "major", 0)]
    [DataRow("g", "major", 1)]
    [DataRow("f", "major", 1)]
    [DataRow("fis", "major", 6)]
    [DataRow("bes", "major", 2)]
    [DataRow("a", "minor", 0)]
    [DataRow("d", "dorian", 0)]
    [DataRow("e", "phrygian", 0)]
    [DataRow("b", "locrian", 0)]
    [DataRow("es", "minor", 6)]
    public void AKeyIsPrintedAsItsTonicAndModeSay(string tonic, string mode, int signs) => UiThread.Run(() =>
    {
        Assert.AreEqual(signs, All(Lay($@"{{ \key {tonic} \{mode} c'1 }}"), "key").Count);
    });

    [TestMethod]
    [DataRow(@"{ \time 4/4 c1 }", 1, "common time is the C sign")]
    [DataRow(@"{ \time 2/2 c1 }", 1, "cut time is the ¢ sign")]
    [DataRow(@"{ c1 }", 1, "LilyPond's default meter is 4/4, as C")]
    [DataRow(@"{ \time 3/4 c2. }", 2, "anything else is figures")]
    [DataRow(@"{ \numericTimeSignature \time 4/4 c1 }", 2, "figures when the source asks for them")]
    [DataRow(@"{ \time 4/4 \numericTimeSignature c1 }", 2, "…even when it asks after the \\time")]
    public void AMeterIsASignOrFigures(string ly, int glyphs, string because) =>
        UiThread.Run(() => Assert.AreEqual(glyphs, All(Lay(ly), "meter").Count, because));

    // ── What hangs on a note ────────────────────────────────────────────────

    [TestMethod]
    public void ATieAndASlurAreCurves() => UiThread.Run(() =>
    {
        var layout = Lay(@"\relative c' { \time 4/4 c4~ c2. | c4( d e f) }");

        Assert.AreEqual(1, All(layout, "tie").Count);
        Assert.AreEqual(1, All(layout, "slur").Count);
    });

    [TestMethod]
    public void MarksAreDrawn_WhetherSpelledOrNamed() => UiThread.Run(() =>
    {
        Assert.AreEqual(4, All(Lay(@"\relative c' { c4-. d-> e\fermata f\trill }"), "articulation").Count);
    });

    [TestMethod]
    public void ATupletIsNumbered_AndItsTimeScaledSoTheBarStillAddsUp() => UiThread.Run(() =>
    {
        var layout = Lay(@"\relative c' { \time 4/4 \tuplet 3/2 { c8 d e } \tuplet 3/2 { f8 g a } c4 c4 }");

        CollectionAssert.AreEqual(new[] { 8 }, NotesPerBar(layout), "six triplet eighths and two quarters are one bar");
        Assert.AreEqual(2, All(layout, "tuplet").Count);
    });

    [TestMethod]
    public void AChordKeepsEveryNote_OnOneStem() => UiThread.Run(() =>
    {
        var notes = All(Lay(@"\relative c' { \time 4/4 <c e g>2 <g c e>2 }"), "note");

        Assert.AreEqual(2, notes.Count);
        Assert.IsTrue(notes.All(note => All(note, "head").Count == 3), "a chord is not just its lowest note");
        Assert.IsTrue(notes.All(note => All(note, "stem").Count == 1));
    });

    [TestMethod]
    public void GraceNotesGoWithTheNoteAfterThem_AndTakeNoTime() => UiThread.Run(() =>
    {
        var layout = Lay(@"\relative c' { \time 4/4 \grace d8 c4 d e f }");

        CollectionAssert.AreEqual(new[] { 4 }, NotesPerBar(layout));
        Assert.AreEqual(1, All(All(layout, "note")[0], "graces").Count);
    });

    [TestMethod]
    public void TextIsPlacedAboveOrBelow() => UiThread.Run(() =>
    {
        var layout = Lay(@"\relative c' { \time 4/4 c4^""Fine"" d_""dolce"" e4 f4 }");
        var staff = All(layout, "staff-line");
        var annotations = All(layout, "annotation");

        Assert.AreEqual(2, annotations.Count);
        Assert.IsTrue(annotations[0].Bounds.Bottom <= staff.Min(l => l.Bounds.Top) + 1, "Fine is above the staff");
        Assert.IsTrue(annotations[1].Bounds.Top >= staff.Max(l => l.Bounds.Bottom) - 1, "dolce is below it");
    });

    // ── Words and chord names ───────────────────────────────────────────────

    [TestMethod]
    public void AChordNameSitsOverTheNoteItStartsOn_AndNamesWhatWroteIt() => UiThread.Run(() =>
    {
        const string Ly = "<<\n  \\new ChordNames \\chordmode { c1 | g1:7 }\n  \\new Staff \\relative c' { \\time 4/4 c4 e g c | b4 d g b }\n>>";
        var chords = All(Lay(Ly), "chord");

        CollectionAssert.AreEqual(new[] { "c1", "g1:7" }, chords.Select(c => Named(c, Ly)).ToArray(),
            "one name per chord, not one per note, each naming the chord line");
        CollectionAssert.AreEqual(new[] { "C", "G7" }, chords.Select(Printed).ToArray());
    });

    [TestMethod]
    public void AChordNameIsSpelledTheWayALeadSheetSpellsIt() => UiThread.Run(() =>
    {
        const string Ly = "<<\n  \\new ChordNames \\chordmode { d2:m7 bes2:maj g1:m }\n  \\new Staff \\relative c' { \\time 4/4 c2 d2 | e1 }\n>>";
        // A lead sheet's maj7 rather than LilyPond's own triangle, and the flat drawn as a flat.
        CollectionAssert.AreEqual(new[] { "Dm7", "B♭maj7", "Gm" }, All(Lay(Ly), "chord").Select(Printed).ToArray());
    });

    [TestMethod]
    public void LyricsLineUpUnderTheNotes_EachNamingItsSyllable() => UiThread.Run(() =>
    {
        const string Ly = "\\relative c' { \\time 4/4 c4 d e f | g4 a b c }\n\\addlyrics { One two three four five six sev -- en }";
        var syllables = All(Lay(Ly), "syllable");

        CollectionAssert.AreEqual(new[] { "One", "two", "three", "four", "five", "six", "sev", "en" },
            syllables.Select(s => Named(s, Ly)).ToArray());
        Assert.AreEqual("sev-", Printed(syllables[6]), "-- prints a hyphen and keeps the word together");
    });

    [TestMethod]
    public void ASyllablesDurationIsNotPrinted_ButAFullStopIs() => UiThread.Run(() =>
    {
        var syllables = All(Lay("\\relative c' { \\time 4/4 c4 d e f }\n\\addlyrics { Ly4 -- rics4. in sky. }"), "syllable");
        CollectionAssert.AreEqual(new[] { "Ly-", "rics", "in", "sky." }, syllables.Select(Printed).ToArray());
    });

    [TestMethod]
    public void ARestTakesNoSyllable_AndASlurHoldsOneOverWhatItJoins() => UiThread.Run(() =>
    {
        var layout = Lay("\\relative c' { \\time 4/4 c4 r4 d4( e4) | f1 }\n\\addlyrics { one two three }");
        var notes = All(layout, "note");
        var syllables = All(layout, "syllable");

        Assert.AreEqual(3, syllables.Count);
        Assert.AreEqual(0, Under(notes, syllables[0]), "one is sung on c");
        Assert.AreEqual(1, Under(notes, syllables[1]), "two skips the rest and lands on d");
        Assert.AreEqual(3, Under(notes, syllables[2]), "three skips e, which the slur holds two over");
    });

    [TestMethod]
    public void VersesStack_AndLyricsToFindTheirVoiceByName() => UiThread.Run(() =>
    {
        var stacked = All(Lay("\\relative c' { \\time 4/4 c4 d e f }\n\\addlyrics { one two three four }\n\\addlyrics { ein zwei drei vier }"), "syllable");
        Assert.AreEqual(8, stacked.Count);

        // Drawn note by note rather than verse by verse, so found by what they say.
        var one = stacked.Single(s => Printed(s) == "one");
        var ein = stacked.Single(s => Printed(s) == "ein");
        Assert.IsTrue(ein.Bounds.Top > one.Bounds.Bottom - 1, "the second verse is under the first");
        Assert.AreEqual(Centre(one), Centre(ein), 1, "…and under the same note");

        const string ByName = "<<\n  \\new Staff \\new Voice = \"melody\" \\relative c' { \\time 4/4 c4 d e f }\n  \\new Lyrics \\lyricsto \"melody\" { one two three four }\n>>";
        Assert.AreEqual(4, All(Lay(ByName), "syllable").Count);
    });

    // ── Staves ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void EachNewStaffIsAStaff_WithItsOwnClefAndName_AndTheyAreBracketed() => UiThread.Run(() =>
    {
        var layout = Lay("""
            \score {
              \new ChoirStaff <<
                \new Staff \with { instrumentName = "Soprano" }
                  \relative c'' { \time 4/4 \key g \major g4 a b c | d1 }
                \new Staff \with { instrumentName = "Bass" }
                  { \clef bass \time 4/4 \key g \major g,4 fis, e, d, | g,1 }
              >>
            }
            """);

        var systems = All(layout, "system");
        Assert.AreEqual(2, systems.Count, "one staff per \\new Staff");
        Assert.AreEqual(1, All(layout, "bracket").Count, "run in step, so bracketed into one system");
        CollectionAssert.AreEqual(new[] { "Soprano", "Bass" }, All(layout, "voice").Select(Printed).ToArray());

        // Written low, in the bass clef it asked for: on the staff rather than hanging off it on ledger lines.
        var staff = All(systems[1], "staff-line");
        Assert.IsTrue(All(systems[1], "note").All(n => n.Bounds.Bottom < staff.Max(l => l.Bounds.Bottom) + 24));
    });

    [TestMethod]
    public void TwoVoicesOnOneStaffEngraveTheFirst() => UiThread.Run(() =>
    {
        var layout = Lay(@"\new Staff { \time 4/4 << { c'4 d' e' f' } \\ { a4 b c' d' } >> }");

        Assert.AreEqual(1, All(layout, "system").Count);
        Assert.AreEqual(4, All(layout, "note").Count);
    });

    [TestMethod]
    public void ADefinitionIsPlayedWhereItIsUsed_InTheMeterThatPlaceIsIn() => UiThread.Run(() =>
    {
        // The melody is written with no meter of its own; where it is used, \global has just set three-four.
        // Barring it where it was written would bar it in four.
        var layout = Lay("global = { \\time 3/4 }\nmelody = \\relative c'' { c4 d e f g a }\n\\new Staff { \\global \\melody }");

        CollectionAssert.AreEqual(new[] { 3, 3 }, NotesPerBar(layout));
    });

    [TestMethod]
    public void AFileOfDefinitionsEngravesItsRichest() => UiThread.Run(() =>
    {
        var layout = Lay("global = { \\time 4/4 }\ncf = \\relative { \\clef bass \\global c4 c' b a | g a f d }");

        Assert.AreEqual(1, All(layout, "system").Count);
        Assert.AreEqual(8, All(layout, "note").Count);
    });

    [TestMethod]
    public void Exercise3_EngravesBothStavesOfThePianoStaff() => UiThread.Run(() =>
    {
        var layout = Lay(
            "#(ly:set-option 'point-and-click #f)\n" +
            "global = {\n  \\time 4/4\n  \\numericTimeSignature\n  \\key c \\major\n}\n" +
            "cf = \\relative {\n  \\clef bass\n  \\global\n  c4 c' b a |\n  g a f d |\n  e f g g, |\n  c1\n}\n" +
            "upper = \\relative c'' {\n  \\global\n  r4 s4 s2 |\n  s1*2 |\n  s2 s4 s\n  \\bar \"||\"\n}\n" +
            "bassFigures = \\figuremode {\n  s1*2 | s4 <6> <6 4> <7> | s1\n}\n" +
            "\\markup { \"Exercise 3: Write 8th notes against the given bass line.\" }\n" +
            "\\score {\n  \\new PianoStaff <<\n    \\new Staff { \\upper }\n    \\new Staff = lower { << \\cf \\new FiguredBass \\bassFigures >> }\n  >>\n  \\layout {}\n}\n");

        var systems = All(layout, "system");
        Assert.AreEqual(2, systems.Count, "the blank upper staff and the given bass");
        Assert.AreEqual(1, All(layout, "bracket").Count, "four bars each, so one bracketed system");
        Assert.AreEqual(0, All(systems[0], "note").Count, "the upper staff is the one the student fills in");
        Assert.AreEqual(13, All(systems[1], "note").Count, "the cantus firmus is thirteen notes");
        Assert.AreEqual(4, All(layout, "meter").Count, "\\numericTimeSignature, through \\global, on both staves");
        Assert.IsTrue(Printed(All(layout, "title").Single()).StartsWith("Exercise 3"), "a top-level \\markup is the title");
    });

    // ── The header ──────────────────────────────────────────────────────────

    [TestMethod]
    public void TheHeaderGoesWhereLilyPondPrintsEachField_NamingWhatWasWritten() => UiThread.Run(() =>
    {
        const string Ly = "\\header {\n  title = \"Speed the Plough\"\n  subtitle = \"a reel\"\n  composer = \"Trad.\"\n  poet = \"Reel\"\n}\n\\relative c' { c4 d e f }";
        var layout = Lay(Ly);

        Assert.AreEqual("Speed the Plough", Printed(All(layout, "title").Single()));
        Assert.AreEqual("a reel", Printed(All(layout, "subtitle").Single()));
        Assert.AreEqual("Trad.", Printed(All(layout, "credit").Single()));
        Assert.AreEqual("Reel", Printed(All(layout, "rhythm").Single()));
        Assert.AreEqual("Speed the Plough", Named(All(layout, "title").Single(), Ly),
            "the title names the characters between its quotes, so it selects a letter at a time");
    });

    // ── Tolerance ───────────────────────────────────────────────────────────

    [TestMethod]
    public void SchemeDynamicsAndSettingsAreSkipped_NotFatal() => UiThread.Run(() =>
    {
        var layout = Lay("""
            \version "2.24.0"
            #(set-global-staff-size 20)
            \relative c' {
              \time 4/4
              \set Staff.instrumentName = "Flute"
              c4\f d\< e f\!
              \override NoteHead.color = #red
              g1\fermata
            }
            """);

        CollectionAssert.AreEqual(new[] { 4, 1 }, NotesPerBar(layout), "the dynamics do not eat the notes");
        Assert.AreEqual("Flute", Printed(All(layout, "voice").Single()));
    });

    [TestMethod]
    public void CommentsAreNotMusic() => UiThread.Run(() =>
    {
        Assert.AreEqual(4, All(Lay("% a comment with c4 d4 in it\n\\relative c' { c4 d %{ e f %} e f }"), "note").Count);
    });

    // ── Parity with the other notation ──────────────────────────────────────

    /// <summary>
    /// One tune written in each notation, drawn by the one engraver: the same bars, the same notes on the same
    /// lines and spaces. Any drift between the two readings — an octave off, a bar closed in the wrong place, a
    /// duration mis-scaled — moves a head, and this is where it shows.
    /// </summary>
    [TestMethod]
    [CoversNode("lilypond")]
    public void TheSameTuneInBothNotations_EngravesTheSame() => UiThread.Run(() =>
    {
        var abc = AbcBuilder.Build("""
            X:1
            T:Speed the Plough
            M:4/4
            L:1/8
            K:G
            |:GABc dedB|dedB dedB|c2ec B2dB|c2A2 A2BA|
              GABc dedB|dedB dedB|c2ec B2dB|A2F2 G4:|
            |:g2gf gdBd|g2f2 e2d2|c2ec B2dB|c2A2 A2df|
              g2gf g2Bd|g2f2 e2d2|c2ec B2dB|A2F2 G4:|
            """, 900, Brushes.Black, 1.0);

        var ly = Lay("""
            \header { title = "Speed the Plough" }
            \relative c'' {
              \numericTimeSignature \time 4/4 \key g \major
              \repeat volta 2 {
                g8 a b c d e d b | d e d b d e d b | c4 e8 c b4 d8 b | c4 a a b8 a |
                g8 a b c d e d b | d e d b d e d b | c4 e8 c b4 d8 b | a4 fis g2
              }
              \repeat volta 2 {
                g'4 g8 fis g d b d | g4 fis e d | c4 e8 c b4 d8 b | c4 a a d8 fis |
                g4 g8 fis g4 b,8 d | g4 fis e d | c4 e8 c b4 d8 b | a4 fis g2
              }
            }
            """);

        var one = HeadsByBar(abc);
        var other = HeadsByBar(ly);

        Assert.AreEqual(16, one.Count, "sixteen bars in ABC");
        Assert.AreEqual(one.Count, other.Count, "and in LilyPond");

        for (var bar = 0; bar < one.Count; bar++)
            CollectionAssert.AreEqual(one[bar], other[bar], $"bar {bar + 1}: the notes are not where the other drew them");
    });

    // ── Reading the picture ─────────────────────────────────────────────────

    private static Laid Lay(string ly) => LilyPondBuilder.Build(ly, 900, Brushes.Black, 1.0);

    private static List<Piece> All(Laid layout, string kind) => All(layout.Root, kind);

    private static List<Piece> All(Piece root, string kind) => [.. root.SelfAndDescendants().Where(p => p.Kind == kind)];

    private static List<Piece> Bars(Laid layout) => All(layout, "measure");

    private static int[] NotesPerBar(Laid layout) => [.. Bars(layout).Select(bar => All(bar, "note").Count)];

    /// <summary>What a piece names, as it is written.</summary>
    private static string Named(Piece piece, string ly)
    {
        var at = piece.Sits();
        return ly.Substring(at.Start, at.Length);
    }

    /// <summary>The words a piece draws.</summary>
    private static string Printed(Piece piece)
    {
        var text = new StringBuilder();
        foreach (var inner in piece.SelfAndDescendants())
            foreach (var mark in inner.Marks)
                if (mark is TextMark written) text.Append(written.Glyphs.Text);

        return text.ToString();
    }

    private static double Centre(Piece piece) => piece.Bounds.Left + (piece.Bounds.Width / 2);

    /// <summary>Which of the notes a syllable is sung on: the one it is centred nearest.</summary>
    private static int Under(List<Piece> notes, Piece syllable) =>
        Enumerable.Range(0, notes.Count).MinBy(at => Math.Abs(Centre(notes[at]) - Centre(syllable)));

    /// <summary>Every bar's note heads, as how far each sits below the top of its staff.</summary>
    private static List<int[]> HeadsByBar(Laid layout)
    {
        var bars = new List<int[]>();

        foreach (var system in All(layout, "system"))
        {
            var top = All(system, "staff-line").Min(line => line.Bounds.Top);
            foreach (var bar in All(system, "measure"))
                bars.Add([.. All(bar, "head").Select(head => (int)Math.Round(head.Bounds.Top - top))]);
        }

        return bars;
    }
}
