using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Markdown.Music.Abc.Stages;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Markdown.Music;

namespace Nexaflow.Tests.Markdown.Music.Abc;

/// <summary>
/// The rule every stage obeys, and what the stages between them actually work out.
///
/// <para>
/// The rule first, because it is what makes the rest safe: a stage may nest differently, replace a piece
/// or hang something underneath, and the characters coming out must be the ones that went in. Checked
/// stage by stage rather than end to end, so a failure names the actor that broke it instead of leaving a
/// round-trip test failing somewhere downstream.
/// </para>
/// </summary>
[TestClass]
[CoversNode("abc-ast-pipeline")]
public class AbcPipelineTests
{
    [TestMethod]
    public void EveryStageLeavesTheSourceExactlyAsItFoundIt()
    {
        foreach (var (what, abc) in AbcConstructs.EverythingAndItsLines())
        {
            var tree = AbcParser.Parse(abc);

            foreach (var stage in AbcPipeline.Of().Stages)
            {
                tree = stage.Run(tree);
                Assert.AreEqual(abc, tree.Print(), $"{what}: after {stage.Name}");
            }
        }
    }

    [TestMethod]
    public void AndSoDoesShowingAStretchAsWritten()
    {
        // The one stage a caret asks for, over every offset and every length a caret could ask about.
        foreach (var (what, abc) in AbcConstructs.Everything)
            for (var start = 0; start < abc.Length; start += 3)
                for (var length = 1; length <= Math.Min(12, abc.Length - start); length += 4)
                    Assert.AreEqual(abc, AbcPipeline.Of(editing: (start, length)).Run(AbcParser.Parse(abc)).Print(),
                        $"{what}: showing {start}+{length}");
    }

    [TestMethod]
    public void NothingAStageAddsTakesUpAnySource()
    {
        // The other half of the rule, and the one that makes it hold by construction rather than by care:
        // everything a stage hangs underneath a piece is derived, so it is no characters wide and sits
        // where the piece it explains sits.
        foreach (var (what, abc) in AbcConstructs.Everything)
        {
            var reading = ContentReading.Of(AbcPipeline.Of().Run(AbcParser.Parse(abc)));

            foreach (var part in reading.Root.SelfAndDescendants())
            {
                if (!part.Derived) continue;

                Assert.AreEqual(0, part.Length, $"{what}: a derived {part.Kind} claims source");
                Assert.AreEqual("", part.Print(), $"{what}: a derived {part.Kind} prints something");
            }
        }
    }

    [TestMethod]
    public void EveryPartStillLandsWhereItsCharactersAre()
    {
        // Re-nesting must not move anything. A part that is not derived still names the stretch of source
        // it prints as, wherever a stage has since put it in the tree.
        foreach (var (what, abc) in AbcConstructs.Everything)
        {
            var reading = ContentReading.Of(AbcPipeline.Of().Run(AbcParser.Parse(abc)));

            foreach (var part in reading.Root.SelfAndDescendants())
            {
                if (part.Derived) continue;

                Assert.IsTrue(part.End <= abc.Length, $"{what}: {part.Kind} claims past the end");
                Assert.AreEqual(abc.Substring(part.Start, part.Length), part.Print(),
                    $"{what}: {part.Kind} at {part.Start} is not what the source says");
            }
        }
    }

    // ── What the stages actually work out ───────────────────────────────────

    [TestMethod]
    public void NotesWrittenTogetherAreOneGroup()
    {
        var tune = AbcPipeline.Of().Run(AbcParser.Parse("X:1\nL:1/8\nK:D\nABcd ABcd|A2 B2|\n"));

        var beams = tune.SelfAndDescendants().Where(n => n.Kind == AbcKinds.Beam).ToList();

        Assert.AreEqual(2, beams.Count, "two runs of four, and two lone notes that are not a group");
        Assert.AreEqual("ABcd", beams[0].Print());
        Assert.AreEqual("ABcd", beams[1].Print());
    }

    [TestMethod]
    public void AndABarIsWhatIsBetweenTwoBarLines()
    {
        var tune = AbcPipeline.Of().Run(AbcParser.Parse("X:1\nK:C\nABc|def|gab|\n"));

        var bars = tune.SelfAndDescendants().Where(n => n.Kind == AbcKinds.Measure).ToList();

        Assert.AreEqual(3, bars.Count);
        Assert.AreEqual("ABc|", bars[0].Print(), "a bar carries the line that closed it");
        Assert.AreEqual("def|", bars[1].Print());
    }

    [TestMethod]
    public void ARepeatMayOpenAfterAThickLine_AndCloseOnOne()
    {
        var tune = AbcPipeline.Of().Run(AbcParser.Parse("X:1\nK:C\nABc[|:def:|]gab|\n"));

        CollectionAssert.AreEqual(new[] { "[|:", ":|]", "|" },
                                  tune.SelfAndDescendants().Where(n => n.Kind == AbcKinds.Barline).Select(n => n.Print()).ToArray());
    var troubled = ContentPart.Of(tune).SelfAndDescendants().Where(p => p.Trouble is not null && p.Length > 0 && !p.Derived).Select(p => $"{p.Kind} '{p.Print()}': {p.Trouble}").ToList();
        Assert.AreEqual(0, troubled.Count, "each is a bar line, not a stray ':' or ']': " + string.Join("; ", troubled));
    }

    [TestMethod]
    public void ABarThatRunsOnToTheNextLineSaysSo()
    {
        var tune = AbcPipeline.Of().Run(AbcParser.Parse("X:1\nK:C\nABc|def\nghi|\n"));

        var bars = tune.SelfAndDescendants().Where(n => n.Kind == AbcKinds.Measure).ToList();

        Assert.AreEqual(3, bars.Count);
        Assert.IsFalse(GroupBars.Continues(bars[0]), "closed by a bar line");
        Assert.IsTrue(GroupBars.Continues(bars[1]), "the line ended first");
    }

    [TestMethod]
    public void AKeySignatureReachesTheNotesUnderIt()
    {
        var tune = AbcPipeline.Of().Run(AbcParser.Parse("X:1\nK:D\nFGA|\n"));

        var alters = Notes(tune).Select(n => Event(n).Pitch!.Value.Alter).ToList();

        CollectionAssert.AreEqual(new[] { 1, 0, 0 }, alters, "F is sharp in D major; G and A are not");
    }

    [TestMethod]
    public void AndAnAccidentalLastsTheBarAndNoLonger()
    {
        var tune = AbcPipeline.Of().Run(AbcParser.Parse("X:1\nK:C\n^FGF|FGF|\n"));

        var alters = Notes(tune).Select(n => Event(n).Pitch!.Value.Alter).ToList();

        CollectionAssert.AreEqual(new[] { 1, 0, 1, 0, 0, 0 }, alters,
            "the written sharp holds to the bar line and stops there");
    }

    [TestMethod]
    public void TheUnitNoteLengthIsWhatALengthSuffixMultiplies()
    {
        var eighths = Notes(AbcPipeline.Of().Run(AbcParser.Parse("X:1\nL:1/8\nK:C\nA A2 A4|\n")))
            .Select(n => Event(n).Lasts.Quarters).ToList();

        CollectionAssert.AreEqual(new[] { 0.5, 1.0, 2.0 }, eighths);

        var quarters = Notes(AbcPipeline.Of().Run(AbcParser.Parse("X:1\nL:1/4\nK:C\nA A2 A4|\n")))
            .Select(n => Event(n).Lasts.Quarters).ToList();

        CollectionAssert.AreEqual(new[] { 1.0, 2.0, 4.0 }, quarters, "the same tune, written in quarters");
    }

    [TestMethod]
    public void AndAMeterSetsItWhenNothingElseDoes()
    {
        // ABC's own rule: a sixteenth under three quarters to the bar, an eighth above it. Which is why a
        // tune in 2/4 that names no L: is written in sixteenths and one in 4/4 in eighths.
        Assert.AreEqual(0.5, Notes(AbcPipeline.Of().Run(AbcParser.Parse("X:1\nM:4/4\nK:C\nA|\n")))
            .Select(n => Event(n).Lasts.Quarters).Single(), "4/4 is one eighth");

        Assert.AreEqual(0.25, Notes(AbcPipeline.Of().Run(AbcParser.Parse("X:1\nM:2/4\nK:C\nA|\n")))
            .Select(n => Event(n).Lasts.Quarters).Single(), "2/4 is one sixteenth");
    }

    [TestMethod]
    public void ATripletTakesTheTimeOfTwo()
    {
        var lengths = Notes(AbcPipeline.Of().Run(AbcParser.Parse("X:1\nL:1/8\nK:C\n(3ABc|\n")))
            .Select(n => Event(n).Lasts).ToList();

        Assert.AreEqual(3, lengths.Count);
        foreach (var length in lengths) Assert.AreEqual(new Duration(1, 3).Quarters, length.Quarters, 1e-9);

        // Three of them add up to two eighths — the whole point of the notation.
        Assert.AreEqual(1.0, lengths.Sum(l => l.Quarters), 1e-9);
    }

    [TestMethod]
    public void ABrokenRhythmMovesTimeFromOneNoteToTheOther()
    {
        var lengths = Notes(AbcPipeline.Of().Run(AbcParser.Parse("X:1\nL:1/8\nK:C\nA>B|\n")))
            .Select(n => Event(n).Lasts.Quarters).ToList();

        CollectionAssert.AreEqual(new[] { 0.75, 0.25 }, lengths, "dotted, then halved");

        var back = Notes(AbcPipeline.Of().Run(AbcParser.Parse("X:1\nL:1/8\nK:C\nA<B|\n")))
            .Select(n => Event(n).Lasts.Quarters).ToList();

        CollectionAssert.AreEqual(new[] { 0.25, 0.75 }, back, "and the other way round");
    }

    [TestMethod]
    public void ASyllableLandsUnderTheNoteItIsSungOn()
    {
        var tune = AbcPipeline.Of().Run(AbcParser.Parse("X:1\nL:1/4\nK:C\nABcd|\nw:one two- three four\n"));

        var sung = Notes(tune).Select(n => Event(n).Sung.Select(l => l.Text).FirstOrDefault()).ToList();

        CollectionAssert.AreEqual(new[] { "one", "two", "three", "four" }, sung);
    }

    [TestMethod]
    public void AndASecondVerseStacksUnderTheSameNotes()
    {
        var tune = AbcPipeline.Of().Run(AbcParser.Parse("X:1\nL:1/4\nK:C\nAB|\nw:one two\nw:un deux\n"));

        var first = Notes(tune).First();
        var verses = Event(first).Sung.OrderBy(l => l.Verse).Select(l => l.Text).ToList();

        CollectionAssert.AreEqual(new[] { "one", "un" }, verses);
    }

    // ── What fields, marks and words are said to be ─────────────────────────

    [TestMethod]
    public void AFieldSaysWhatItsValueMeansForItsLetter()
    {
        var fields = AbcPipeline.Of().Run(AbcParser.Parse("X:1\nM:C|\nL:1/16\nV:1 clef=bass name=\"Tenor\"\nK:F bass\nA|\n"))
            .SelfAndDescendants().OfType<AbcFieldNode>().ToDictionary(field => field.Letter);

        Assert.AreEqual(MeterSign.Cut, fields['M'].Sign);
        Assert.AreEqual((2, 2), fields['M'].Meter);
        Assert.AreEqual(0.25, fields['L'].Unit!.Value.Quarters, 1e-9, "a sixteenth is a quarter of a quarter note");
        Assert.AreEqual("1", fields['V'].Voice);
        Assert.AreEqual("Tenor", fields['V'].VoiceName);
        Assert.AreEqual(ClefKind.Bass, fields['V'].Clef);
        Assert.AreEqual(-1, fields['K'].Fifths);
        Assert.AreEqual(ClefKind.Bass, fields['K'].Clef, "a bare clef name counts on K:");
    }

    [TestMethod]
    public void EverySpellingOfAMarkIsTheOneMark()
    {
        var marks = AbcPipeline.Of().Run(AbcParser.Parse("X:1\nK:C\nTA !trill!B .c !staccato!d|\n"))
            .SelfAndDescendants().OfType<MusicMarkNode>().Select(node => node.Mark).ToList();

        CollectionAssert.AreEqual(new[] { MusicMark.Trill, MusicMark.Trill, MusicMark.Staccato, MusicMark.Staccato }, marks);
    }

    [TestMethod]
    public void AQuotedRunIsAChordUnlessItSaysWhereItGoes()
    {
        var said = AbcPipeline.Of().Run(AbcParser.Parse("X:1\nK:C\n\"Am\"A \"^fine\"B \"_soft\"c|\n"))
            .SelfAndDescendants().OfType<MusicAnnotationNode>().Select(node => (node.Said, node.Placement)).ToList();

        CollectionAssert.AreEqual(new (string, AnnotationPlacement?)[]
        {
            ("Am", null),
            ("fine", AnnotationPlacement.Above),
            ("soft", AnnotationPlacement.Below),
        }, said);
    }

    [TestMethod]
    public void ADecorationNamingNoMarkIsSaidToHaveNothingToDraw()
    {
        var tune = AbcPipeline.Of().Run(AbcParser.Parse("X:1\nK:C\n!wibble!A !trill!B|\n"));

        var troubled = tune.SelfAndDescendants().Where(node => node.Trouble is not null).Select(node => node.Print()).ToList();

        CollectionAssert.AreEqual(new[] { "!wibble!" }, troubled);
    }

    [TestMethod]
    public void ASyllableSaysWhichWordsLineItWasWrittenOn()
    {
        // A line carried on with a backslash is one row of music with words under each of its source lines; each syllable
        // points at the line it was written on, not at whichever verse of the row it falls in.
        const string abc = "X:1\nL:1/4\nK:C\nAB|\\\nw:one two\ncd|\nw:three four\n";
        var tune = AbcPipeline.Of().Run(AbcParser.Parse(abc));

        var lines = tune.Children;
        foreach (var sung in Notes(tune).SelectMany(n => Event(n).Sung))
        {
            var words = lines[sung.Line].Part(AbcRoles.Value)!.Children[sung.At];
            Assert.AreEqual(sung.Text, words.Print(), "the syllable is the characters it points at");
        }
    }

    [TestMethod]
    public void AHyphenWrittenAsAWordIsSungAsOne()
    {
        var tune = AbcPipeline.Of().Run(AbcParser.Parse("X:1\nL:1/4\nK:C\nABc|\nw:vi \\- dit\n"));

        CollectionAssert.AreEqual(new[] { "vi", "-", "dit" }, Notes(tune).Select(n => Event(n).Sung.Single().Text).ToList());
    }

    /// <summary>Every note in the tune, in written order, chord members left out.</summary>
    private static IEnumerable<ContentNode> Notes(ContentNode tune) =>
        tune.SelfAndDescendants().Where(n => n.Kind == AbcKinds.Note
                                             && n.Role != AbcRoles.Note
                                             && n.Part(AbcRoles.Letter) is not null);

    /// <summary>A note as the stages leave it.</summary>
    private static AbcEventNode Event(ContentNode note) => (AbcEventNode)note;
}
