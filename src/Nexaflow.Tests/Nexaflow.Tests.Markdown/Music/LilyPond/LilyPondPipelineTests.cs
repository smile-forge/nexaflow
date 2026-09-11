using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music;
using Nexaflow.Markdown.Music.LilyPond;
using Nexaflow.Markdown.Music.LilyPond.Stages;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Music.LilyPond;

/// <summary>
/// The rule every stage obeys, and what LilyPond's stages work out: how long each event lasts and what each
/// note sounds.
///
/// <para>
/// Both are facts about what was written before a note, in the order it was written, which is why they are
/// stages. The facts that depend on where a note is <em>played</em> — its bar, its beam, whether its accidental
/// prints — are the builder's, and tested there.
/// </para>
/// </summary>
[TestClass]
[CoversNode("ly-ast-pipeline")]
public class LilyPondPipelineTests
{
    // ── The rule ────────────────────────────────────────────────────────────

    [TestMethod]
    public void EveryStageLeavesTheSourceExactlyAsItFoundIt()
    {
        foreach (var (what, ly) in LilyPondConstructs.Everything)
        {
            var tree = LilyPondParser.Parse(ly);

            foreach (var stage in LilyPondPipeline.Of().Stages)
            {
                tree = stage.Run(tree);
                Assert.AreEqual(ly, tree.Print(), $"{what}: after {stage.Name}");
            }
        }
    }

    [TestMethod]
    public void AndSoDoesShowingAStretchAsWritten()
    {
        foreach (var (what, ly) in LilyPondConstructs.Everything)
            for (var start = 0; start < ly.Length; start += 3)
                for (var length = 1; length <= Math.Min(12, ly.Length - start); length += 4)
                    Assert.AreEqual(ly, LilyPondPipeline.Read(ly, editing: (start, length)).Print(),
                        $"{what}: showing {start}+{length}");
    }

    [TestMethod]
    public void NothingAStageAddsTakesUpAnySource()
    {
        foreach (var (what, ly) in LilyPondConstructs.Everything)
        {
            var reading = ContentReading.Of(LilyPondPipeline.Read(ly));

            foreach (var part in reading.Root.SelfAndDescendants())
            {
                if (!part.Derived) continue;

                Assert.AreEqual(0, part.Length, $"{what}: a derived {part.Kind} claims source");
                Assert.AreEqual("", part.Print(), $"{what}: a derived {part.Kind} prints something");
            }
        }
    }

    // ── How long things last ────────────────────────────────────────────────

    [TestMethod]
    public void ADurationIsCarriedUntilItChanges()
    {
        CollectionAssert.AreEqual(new[] { 1.0, 1, 1, 0.5, 0.5, 0.5 }, Lasts("{ c4 d e f8 g a }"));
    }

    [TestMethod]
    public void ADotIsCarriedWithIt_AndAMultiplierIsNot()
    {
        // c4. d is two dotted quarters. R1*3 is three bars long, and the note after it is a whole note.
        CollectionAssert.AreEqual(new[] { 1.5, 1.5, 12, 4 }, Lasts("{ c4. d R1*3 e }"));
        CollectionAssert.AreEqual(new[] { 1.5, 1.5, 4, 4 }, Writes("{ c4. d R1*3 e }"));
    }

    [TestMethod]
    public void ABreveIsWrittenAsACommand()
    {
        CollectionAssert.AreEqual(new[] { 8.0, 8 }, Writes(@"{ c\breve d }"));
    }

    [TestMethod]
    public void ATupletSqueezesWhatItHolds_WhicheverWayRoundItIsWritten()
    {
        // Three in the time of two, written \tuplet 3/2 or \times 2/3: drawn as eighths, lasting a third each.
        const string Ly = @"{ \tuplet 3/2 { c8 d e } \times 2/3 { f8 g a } b8 }";

        CollectionAssert.AreEqual(new[] { 1 / 3.0, 1 / 3.0, 1 / 3.0, 1 / 3.0, 1 / 3.0, 1 / 3.0, 0.5 }, Lasts(Ly));
        CollectionAssert.AreEqual(new[] { 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5 }, Writes(Ly));
    }

    [TestMethod]
    public void ADefinitionTakesTheLengthWrittenBeforeIt()
    {
        // In written order, the way LilyPond's own reader carries it, wherever the definition is later used.
        CollectionAssert.AreEqual(new[] { 0.5, 0.5, 0.5, 0.5 }, Lasts("a = { c8 d }\nb = { e f }\n{ \\b \\a }"));
    }

    [TestMethod]
    public void AChordLastsWhatIsWrittenAfterIt()
    {
        CollectionAssert.AreEqual(new[] { 2.0, 2 }, Lasts("{ <c e g>2 <d f> }"));
    }

    // ── What notes sound ────────────────────────────────────────────────────

    [TestMethod]
    public void PlainEntryPutsCInTheOctaveBelowMiddleC()
    {
        CollectionAssert.AreEqual(new[] { "C3", "C4", "C5", "C2" }, Sounds("{ c4 c' c'' c, }"));
    }

    [TestMethod]
    public void RelativeEntryTakesTheNearestOctave()
    {
        // From middle C a g is a fourth below; the , drops the last one another octave.
        CollectionAssert.AreEqual(new[] { "C4", "G3", "C4", "G2" }, Sounds(@"\relative c' { c4 g c g, }"));
    }

    [TestMethod]
    public void WithNoStartTheFirstNoteIsWhereWritingItPlainlyPutsIt()
    {
        foreach (var letter in "cdefgab")
            foreach (var marks in new[] { "", "'", "''", "," })
                Assert.AreEqual(Sounds($"{{ {letter}{marks} }}")[0], Sounds($@"\relative {{ {letter}{marks} }}")[0],
                    $"{letter}{marks}");
    }

    [TestMethod]
    public void FixedEntryAnchorsTheOctave_AndFollowsNothing()
    {
        CollectionAssert.AreEqual(new[] { "C4", "G4", "C5" }, Sounds(@"\fixed c' { c4 g c' }"));
    }

    [TestMethod]
    [DataRow("cis", "C#3")]
    [DataRow("cisis", "C##3")]
    [DataRow("ces", "Cb3")]
    [DataRow("ceses", "Cbb3")]
    [DataRow("as", "Ab3")]
    [DataRow("es", "Eb3")]
    [DataRow("aes", "Ab3")]
    [DataRow("ees", "Eb3")]
    [DataRow("ases", "Abb3")]
    public void ADutchNameCarriesItsOwnAlteration(string name, string sounds)
    {
        Assert.AreEqual(sounds, Sounds($"{{ {name}4 }}")[0]);
    }

    [TestMethod]
    public void AChordIsMeasuredNoteByNote_AndWhatFollowsItFromItsFirst()
    {
        // <c e g> then g: the g is measured from the chord's c, not its g, and so goes down.
        CollectionAssert.AreEqual(new[] { "C4", "E4", "G4", "G3" }, Sounds(@"\relative c' { <c e g>2 g2 }"));
    }

    [TestMethod]
    public void ARepeatedChordSoundsTheChordItRepeats()
    {
        var q = Written(LilyPondPipeline.Read("{ <c e g>4 q }")).Single(n => n.Kind == LilyPondKinds.ChordRepeat);
        CollectionAssert.AreEqual(new[] { "C3", "E3", "G3" }, ResolvePitches.PitchesOf(q).Select(Named).ToArray());
    }

    [TestMethod]
    public void ATranspositionMovesEverythingInsideIt()
    {
        // Up a major second: the E becomes an F sharp rather than a G flat, because a second is one letter.
        CollectionAssert.AreEqual(new[] { "D3", "F#3", "A3" }, Sounds(@"\transpose c d { c4 e g }"));
    }

    [TestMethod]
    public void GraceNotesMoveTheReferenceLikeAnyOther()
    {
        CollectionAssert.AreEqual(new[] { "B4", "C5" }, Sounds(@"\relative c'' { \grace b8 c4 }"));
    }

    [TestMethod]
    public void ARelativeBlockInsideAnotherIsItsOwn()
    {
        // The inner block measures from its own start, and the outer one carries on from where it was.
        CollectionAssert.AreEqual(new[] { "C5", "C3", "D5" }, Sounds(@"\relative c'' { c4 \relative c { c } d }"));
    }

    [TestMethod]
    public void APitchGivenToACommandIsNotPlayed()
    {
        var tree = LilyPondPipeline.Read(@"\relative c' { \key g \major c4 } \transpose c d { e4 }");
        var given = Written(tree).Where(n => n.Kind == LilyPondKinds.Note && n.Role == LilyPondRoles.Argument).ToList();

        Assert.AreEqual(4, given.Count, "c', g, c and d are handed to commands");
        Assert.IsTrue(given.All(n => ResolvePitches.PitchOf(n) is null), "and none of them is played");
        CollectionAssert.AreEqual(new[] { "C4", "F#3" }, Sounds(@"\relative c' { \key g \major c4 } \transpose c d { e4 }"));
    }

    // ── Reading the answers ─────────────────────────────────────────────────

    /// <summary>Everything written, outermost first, leaving out what a stage worked out.</summary>
    private static IEnumerable<ContentNode> Written(ContentNode node)
    {
        if (node.IsDerived) yield break;

        yield return node;
        foreach (var child in node.Children)
            foreach (var inner in Written(child))
                yield return inner;
    }

    /// <summary>Everything that takes time, in the order written.</summary>
    private static List<ContentNode> Events(string ly) =>
        [.. Written(LilyPondPipeline.Read(ly)).Where(n =>
            n.Kind is LilyPondKinds.Note or LilyPondKinds.Rest or LilyPondKinds.Chord or LilyPondKinds.ChordRepeat
            && n.Role is not (LilyPondRoles.Argument or LilyPondRoles.Note))];

    private static double[] Lasts(string ly) => [.. Events(ly).Select(e => ResolveDurations.SoundsOf(e).Quarters)];

    private static double[] Writes(string ly) => [.. Events(ly).Select(e => ResolveDurations.WrittenOf(e).Quarters)];

    /// <summary>What every played note sounds, chord members included, in the order written.</summary>
    private static string[] Sounds(string ly) =>
        [.. Written(LilyPondPipeline.Read(ly))
            .Where(n => n.Kind == LilyPondKinds.Note && n.Role != LilyPondRoles.Argument)
            .Select(n => ResolvePitches.PitchOf(n) is { } p ? Named(p) : "?")];

    private static string Named(Pitch p) =>
        $"{Pitch.Letters[p.Step]}{p.Alter switch { 1 => "#", 2 => "##", -1 => "b", -2 => "bb", _ => "" }}{p.Octave}";
}
