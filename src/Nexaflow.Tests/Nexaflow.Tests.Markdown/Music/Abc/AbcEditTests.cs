using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Music.Abc;

/// <summary>
/// The gestures a reader has on a note, as the stretches of source each one writes over.
///
/// <para>
/// The thing worth asserting about every one of them is not only what it wrote but what it <em>left
/// alone</em>. A gesture names the note it changed and nothing around it; the bar it stands in, the spacing
/// somebody lined up by hand, and the rest of the tune are never written at all. That is the whole reason a
/// gesture answers with a change rather than a tune, and it is the first thing to go if one ever reaches for
/// the stretches it walked past.
/// </para>
/// </summary>
[TestClass]
[CoversNode("abc-editing")]
public class AbcEditTests
{
    private const string Tune = "X:1\nL:1/8\nK:G\nGABc  dedB|c2A2|\n";

    /// <summary>The tune read the way an editor holds it, and the notes in it in written order.</summary>
    private static (ContentPart Tune, IReadOnlyList<ContentPart> Notes) Read(string abc = Tune)
    {
        var tune = ContentPart.Of(AbcPipeline.Of().Run(AbcParser.Parse(abc)));

        var notes = tune.SelfAndDescendants()
            .Where(p => p.Kind == AbcKinds.Note && !p.Derived && p.Part(AbcRoles.Letter) is not null)
            .OrderBy(p => p.Start)
            .ToList();

        return (tune, notes);
    }

    /// <summary>The source with every stretch a change names written over — what the engine does with one.</summary>
    private static string Made(string abc, ContentChange change)
    {
        foreach (var write in change.Writes.OrderByDescending(write => write.Start))
            abc = string.Concat(abc.AsSpan(0, write.Start), write.Text, abc.AsSpan(write.End));

        return abc;
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
            var (_, notes) = Read(abc);
            var change = AbcEdit.Octave([notes[0]], 1);
            Assert.IsNotNull(change, $"step {i}");

            abc = Made(abc, change!);
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
            var (_, notes) = Read(abc);
            abc = Made(abc, AbcEdit.Octave([notes[0]], -1)!);
        }

        Assert.AreEqual("X:1\nK:C\nC,|\n", abc);
    }

    [TestMethod]
    public void AndItLeavesEverythingElseExactlyAsItWasWritten()
    {
        var (_, notes) = Read();

        Assert.AreEqual("X:1\nL:1/8\nK:G\ngABc  dedB|c2A2|\n", Made(Tune, AbcEdit.Octave([notes[0]], 1)!),
            "the two spaces somebody put between the groups are still two spaces");
    }

    [TestMethod]
    public void AndItWritesOverTheNoteAndNothingElse()
    {
        // The rule the whole shape turns on: an edit updates the source and the engine reads the tune again, so a
        // gesture names the characters it changed rather than handing back a tune for somebody to print.
        var (_, notes) = Read();

        var change = AbcEdit.Octave([notes[0]], 1)!;
        var write = change.Writes.Single();

        Assert.AreEqual(notes[0].Start, write.Start, "the stretch written starts where the note does");
        Assert.AreEqual(notes[0].Length, write.Length, "and is as long as the note, not as long as the tune");
    }

    // ── Accidental ──────────────────────────────────────────────────────────

    [TestMethod]
    public void SharpeningWritesTheAccidentalOut()
    {
        const string abc = "X:1\nK:C\nCDE|\n";
        var (_, notes) = Read(abc);

        Assert.AreEqual("X:1\nK:C\n^CDE|\n", Made(abc, AbcEdit.Accidental([notes[0]], 1)!));
    }

    [TestMethod]
    public void AndFlatteningANoteTheKeyHasAlreadySharpenedWritesANatural()
    {
        // The gesture that only works if it asks what the note *sounds*. F in G major is F sharp; taking
        // the accidental away would leave the key signature to sharpen it again, so a flat has to be
        // spelled — and one semitone down from F sharp is F natural.
        const string abc = "X:1\nK:G\nFGA|\n";
        var (_, notes) = Read(abc);

        Assert.AreEqual("X:1\nK:G\n=FGA|\n", Made(abc, AbcEdit.Accidental([notes[0]], -1)!));
    }

    [TestMethod]
    public void AndItGoesAsFarAsADoubleAndNoFurther()
    {
        var abc = "X:1\nK:C\nC|\n";
        var seen = new List<string>();

        for (var i = 0; i < 4; i++)
        {
            var (_, notes) = Read(abc);
            if (AbcEdit.Accidental([notes[0]], 1) is not { } change) { seen.Add("(nothing)"); continue; }

            abc = Made(abc, change);
            seen.Add(abc.Split('\n')[2]);
        }

        CollectionAssert.AreEqual(new[] { "^C|", "^^C|", "(nothing)", "(nothing)" }, seen);
    }

    [TestMethod]
    public void AndItReplacesTheAccidentalRatherThanStackingOne()
    {
        const string abc = "X:1\nK:C\n_B|\n";
        var (_, notes) = Read(abc);

        Assert.AreEqual("X:1\nK:C\n=B|\n", Made(abc, AbcEdit.Accidental([notes[0]], 1)!));
    }

    // ── Length ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void LongerDoublesTheWrittenLength()
    {
        var written = new List<string>();
        var abc = "X:1\nL:1/8\nK:C\nA|\n";

        for (var i = 0; i < 3; i++)
        {
            var (_, notes) = Read(abc);
            abc = Made(abc, AbcEdit.Length([notes[0]], 1)!);
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
            var (_, notes) = Read(abc);
            abc = Made(abc, AbcEdit.Length([notes[0]], -1)!);
            written.Add(abc.Split('\n')[3]);
        }

        CollectionAssert.AreEqual(new[] { "A/|", "A/4|", "A/8|" }, written);
    }

    [TestMethod]
    public void AndADottedNoteStaysDotted()
    {
        const string abc = "X:1\nL:1/8\nK:C\nA3/2|\n";
        var (_, notes) = Read(abc);

        Assert.AreEqual("X:1\nL:1/8\nK:C\nA3|\n", Made(abc, AbcEdit.Length([notes[0]], 1)!),
            "three eighths doubled is three quarters, still a dotted note");
    }

    // ── Several at once ─────────────────────────────────────────────────────

    [TestMethod]
    public void AGestureAppliesToEveryNoteItWasGiven()
    {
        const string abc = "X:1\nK:C\nCDEF|\n";
        var (_, notes) = Read(abc);

        var change = AbcEdit.Octave([.. notes.Take(2)], 1)!;

        Assert.AreEqual(2, change.Writes.Count, "two notes changed, two stretches written");
        Assert.AreEqual("X:1\nK:C\ncdEF|\n", Made(abc, change));
    }

    [TestMethod]
    public void AndItSaysWhereTheWritingLanded()
    {
        const string abc = "X:1\nK:C\nCDEF|\n";
        var (_, notes) = Read(abc);

        var change = AbcEdit.Accidental([notes[1]], 1)!;
        var write = change.Writes.Single();

        Assert.AreEqual("D", abc.Substring(write.Start, write.Length), "the note as it stood");
        Assert.AreEqual("^D", write.Text, "and the accidental the gesture spelled in front of it");
        Assert.AreEqual(write.Start + 2, change.Caret,
            "the caret ends after what was written — not after what was replaced, which is a character shorter");
    }

    [TestMethod]
    public void AndWhereSeveralNotesGrowTheCaretCountsWhatCameBeforeIt()
    {
        const string abc = "X:1\nK:C\nCDEF|\n";
        var (_, notes) = Read(abc);

        var change = AbcEdit.Accidental([.. notes.Take(2)], 1)!;
        var made = Made(abc, change);

        Assert.AreEqual("X:1\nK:C\n^C^DEF|\n", made);
        Assert.AreEqual(made.IndexOf("^D", StringComparison.Ordinal) + 2, change.Caret,
            "the first note grew by a character, which moved the second one along");
    }

    // ── Typing a note ───────────────────────────────────────────────────────

    [TestMethod]
    public void ANewNoteTakesTheOctaveOfTheOneBeforeIt()
    {
        const string abc = "X:1\nK:C\nc'd'|\n";
        var (tune, _) = Read(abc);

        Assert.AreEqual("e'", AbcEdit.NoteAt(tune, abc.Length - 2, 'E'),
            "typed after two notes an octave up, the new one is up there too");
    }

    [TestMethod]
    public void AndWhereThereIsNoNoteBeforeItTakesTheMiddleOne()
    {
        const string abc = "X:1\nK:C\n\n";
        var (tune, _) = Read(abc);

        Assert.AreEqual("e", AbcEdit.NoteAt(tune, abc.Length, 'E'));
    }

    // ── The rule every edit keeps ───────────────────────────────────────────

    [TestMethod]
    public void EveryGestureLeavesATuneThatStillReadsBack()
    {
        // The contract: a gesture names stretches of the source it was read from, the engine writes them, and the
        // tune is read again. So the one thing a gesture must never write is something the parser will not give
        // back unchanged — and the caret it asks for has to stand in the source that comes of it.
        foreach (var (what, abc) in AbcConstructs.Everything)
        {
            var (_, notes) = Read(abc);
            if (notes.Count == 0) continue;

            foreach (var answer in new[]
                     {
                         AbcEdit.Octave(notes, 1),
                         AbcEdit.Octave(notes, -1),
                         AbcEdit.Accidental(notes, 1),
                         AbcEdit.Accidental(notes, -1),
                         AbcEdit.Length(notes, 1),
                         AbcEdit.Length(notes, -1),
                     })
            {
                if (answer is not { } change) continue;

                var made = Made(abc, change);

                Assert.AreEqual(made, AbcParser.Parse(made).Print(), what);
                Assert.IsTrue(change.Caret <= made.Length, $"{what}: the caret stands past the end");
            }
        }
    }

    [TestMethod]
    public void AndNoGestureEverWritesTwoStretchesOverOneAnother()
    {
        // What makes several notes at once safe to write in one go: every stretch a change names is a note, and no
        // note is written inside another. The engine writes them back to front and trusts exactly this.
        foreach (var (what, abc) in AbcConstructs.Everything)
        {
            var (_, notes) = Read(abc);
            if (notes.Count == 0) continue;

            foreach (var answer in new[] { AbcEdit.Octave(notes, 1), AbcEdit.Accidental(notes, 1), AbcEdit.Length(notes, 1) })
            {
                if (answer is not { } change) continue;

                var ordered = change.Writes.OrderBy(write => write.Start).ToList();

                for (var at = 1; at < ordered.Count; at++)
                    Assert.IsTrue(ordered[at].Start >= ordered[at - 1].End,
                        $"{what}: a write at {ordered[at].Start} starts inside the one ending at {ordered[at - 1].End}");
            }
        }
    }
}
