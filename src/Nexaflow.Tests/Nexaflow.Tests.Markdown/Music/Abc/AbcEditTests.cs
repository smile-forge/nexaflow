using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Music.Abc;

/// <summary>
/// The gestures a reader has on a note, as operations on the tree.
///
/// <para>
/// The thing worth asserting about every one of them is not only what it wrote but what it <em>left
/// alone</em>. An edit expressed against a part touches that part; the bar it stands in, the spacing
/// somebody lined up by hand, and the rest of the tune come back byte for byte. That is the whole reason
/// for editing a tree rather than splicing a string, and it is the first thing to go if a gesture ever
/// reprints what it walked past.
/// </para>
/// </summary>
[TestClass]
[CoversNode("abc-editing")]
public class AbcEditTests
{
    private const string Tune = "X:1\nL:1/8\nK:G\nGABc  dedB|c2A2|\n";

    /// <summary>The tune read the way an editor holds it, and the notes in it in written order.</summary>
    private static (ContentReading Reading, IReadOnlyList<ContentPart> Notes) Read(string abc = Tune)
    {
        var reading = ContentReading.Of(AbcPipeline.Read(abc));

        var notes = reading.Root.SelfAndDescendants()
            .Where(p => p.Kind == AbcKinds.Note && !p.Derived && p.Part(AbcRoles.Letter) is not null)
            .OrderBy(p => p.Start)
            .ToList();

        return (reading, notes);
    }

    // ── Octave ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void AnOctaveUpWalksTheLetterFromCaseToMarks()
    {
        // C, → C → c → c' — the whole ladder, one press at a time.
        var written = new List<string>();
        var abc = "X:1\nK:C\nC,|\n";

        for (var i = 0; i < 3; i++)
        {
            var (reading, notes) = Read(abc);
            var write = AbcEdit.Octave(reading, [notes[0]], 1);
            Assert.IsNotNull(write, $"step {i}");

            abc = write!.Value.Tree.Print();
            written.Add(abc.Split('\n')[2]);
        }

        CollectionAssert.AreEqual(new[] { "C|", "c|", "c'|" }, written);
    }

    [TestMethod]
    public void AndDownAgainRetracesIt()
    {
        var abc = "X:1\nK:C\nc'|\n";

        for (var i = 0; i < 3; i++)
        {
            var (reading, notes) = Read(abc);
            abc = AbcEdit.Octave(reading, [notes[0]], -1)!.Value.Tree.Print();
        }

        Assert.AreEqual("X:1\nK:C\nC,|\n", abc);
    }

    [TestMethod]
    public void AndItLeavesEverythingElseExactlyAsItWasWritten()
    {
        var (reading, notes) = Read();

        var after = AbcEdit.Octave(reading, [notes[0]], 1)!.Value.Tree.Print();

        Assert.AreEqual("X:1\nL:1/8\nK:G\ngABc  dedB|c2A2|\n", after,
            "the two spaces somebody put between the groups are still two spaces");
    }

    // ── Accidental ──────────────────────────────────────────────────────────

    [TestMethod]
    public void SharpeningWritesTheAccidentalOut()
    {
        var (reading, notes) = Read("X:1\nK:C\nCDE|\n");

        Assert.AreEqual("X:1\nK:C\n^CDE|\n", AbcEdit.Accidental(reading, [notes[0]], 1)!.Value.Tree.Print());
    }

    [TestMethod]
    public void AndFlatteningANoteTheKeyHasAlreadySharpenedWritesANatural()
    {
        // The gesture that only works if it asks what the note *sounds*. F in G major is F sharp; taking
        // the accidental away would leave the key signature to sharpen it again, so a flat has to be
        // spelled — and one semitone down from F sharp is F natural.
        var (reading, notes) = Read("X:1\nK:G\nFGA|\n");

        Assert.AreEqual("X:1\nK:G\n=FGA|\n", AbcEdit.Accidental(reading, [notes[0]], -1)!.Value.Tree.Print());
    }

    [TestMethod]
    public void AndItGoesAsFarAsADoubleAndNoFurther()
    {
        var abc = "X:1\nK:C\nC|\n";
        var seen = new List<string>();

        for (var i = 0; i < 4; i++)
        {
            var (reading, notes) = Read(abc);
            if (AbcEdit.Accidental(reading, [notes[0]], 1) is not { } write) { seen.Add("(nothing)"); continue; }

            abc = write.Tree.Print();
            seen.Add(abc.Split('\n')[2]);
        }

        CollectionAssert.AreEqual(new[] { "^C|", "^^C|", "(nothing)", "(nothing)" }, seen);
    }

    [TestMethod]
    public void AndItReplacesTheAccidentalRatherThanStackingOne()
    {
        var (reading, notes) = Read("X:1\nK:C\n_B|\n");

        Assert.AreEqual("X:1\nK:C\n=B|\n", AbcEdit.Accidental(reading, [notes[0]], 1)!.Value.Tree.Print());
    }

    // ── Length ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void LongerDoublesTheWrittenLength()
    {
        var written = new List<string>();
        var abc = "X:1\nL:1/8\nK:C\nA|\n";

        for (var i = 0; i < 3; i++)
        {
            var (reading, notes) = Read(abc);
            abc = AbcEdit.Length(reading, [notes[0]], 1)!.Value.Tree.Print();
            written.Add(abc.Split('\n')[3]);
        }

        CollectionAssert.AreEqual(new[] { "A2|", "A4|", "A8|" }, written);
    }

    [TestMethod]
    public void AndShorterHalvesIt()
    {
        var written = new List<string>();
        var abc = "X:1\nL:1/8\nK:C\nA|\n";

        for (var i = 0; i < 3; i++)
        {
            var (reading, notes) = Read(abc);
            abc = AbcEdit.Length(reading, [notes[0]], -1)!.Value.Tree.Print();
            written.Add(abc.Split('\n')[3]);
        }

        CollectionAssert.AreEqual(new[] { "A/|", "A/4|", "A/8|" }, written);
    }

    [TestMethod]
    public void AndADottedNoteStaysDotted()
    {
        var (reading, notes) = Read("X:1\nL:1/8\nK:C\nA3/2|\n");

        Assert.AreEqual("X:1\nL:1/8\nK:C\nA3|\n", AbcEdit.Length(reading, [notes[0]], 1)!.Value.Tree.Print(),
            "three eighths doubled is three quarters, still a dotted note");
    }

    // ── Several at once ─────────────────────────────────────────────────────

    [TestMethod]
    public void AGestureAppliesToEveryNoteItWasGiven()
    {
        var (reading, notes) = Read("X:1\nK:C\nCDEF|\n");

        var after = AbcEdit.Octave(reading, [.. notes.Take(2)], 1)!.Value.Tree.Print();

        Assert.AreEqual("X:1\nK:C\ncdEF|\n", after);
    }

    [TestMethod]
    public void AndItSaysWhereTheWritingLanded()
    {
        var (reading, notes) = Read("X:1\nK:C\nCDEF|\n");

        var write = AbcEdit.Accidental(reading, [notes[1]], 1)!.Value;
        var source = write.Tree.Print();

        Assert.AreEqual("^D", source.Substring(write.Start, write.Length),
            "not the length of what was handed in — the accidental grew the note by a character");
    }

    // ── Typing a note ───────────────────────────────────────────────────────

    [TestMethod]
    public void ANewNoteTakesTheOctaveOfTheOneBeforeIt()
    {
        var (reading, _) = Read("X:1\nK:C\nc'd'|\n");

        Assert.AreEqual("e'", AbcEdit.NoteAt(reading, reading.Source.Length - 2, 'E'),
            "typed after two notes an octave up, the new one is up there too");
    }

    [TestMethod]
    public void AndWhereThereIsNoNoteBeforeItTakesTheMiddleOne()
    {
        var (reading, _) = Read("X:1\nK:C\n\n");

        Assert.AreEqual("e", AbcEdit.NoteAt(reading, reading.Source.Length, 'E'));
    }

    // ── The rule every edit keeps ───────────────────────────────────────────

    [TestMethod]
    public void EveryGestureLeavesATuneThatStillReadsBack()
    {
        // The contract: what an edit hands back is provisional, and it is printed and read again. So the
        // one thing it must never do is print something the parser will not give back unchanged.
        foreach (var (what, abc) in AbcConstructs.Everything)
        {
            var (reading, notes) = Read(abc);
            if (notes.Count == 0) continue;

            foreach (var write in new[]
                     {
                         AbcEdit.Octave(reading, notes, 1),
                         AbcEdit.Octave(reading, notes, -1),
                         AbcEdit.Accidental(reading, notes, 1),
                         AbcEdit.Accidental(reading, notes, -1),
                         AbcEdit.Length(reading, notes, 1),
                         AbcEdit.Length(reading, notes, -1),
                     })
            {
                if (write is not { } made) continue;

                var printed = made.Tree.Print();
                Assert.AreEqual(printed, AbcParser.Parse(printed).Print(), what);
                Assert.IsTrue(made.End <= printed.Length, $"{what}: the writing runs past the end");
            }
        }
    }
}
