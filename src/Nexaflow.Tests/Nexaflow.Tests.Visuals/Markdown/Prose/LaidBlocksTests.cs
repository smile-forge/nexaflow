using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using System.Threading;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Code;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown.Prose;

/// <summary>
/// A document laid again after an edit sets down what it laid last time for every block that reads as it did — and must
/// come out exactly as the same source laid from nothing, piece for piece, wherever the kept blocks now stand.
/// </summary>
[TestClass]
[CoversNode("markdown-text")]
public class LaidBlocksTests
{
    private const double Room = 640;

    [TestMethod]
    public void EverySampleLaidAgainAfterAnEditIsTheSampleLaidAfresh()
    {
        var style = StyleFormat.Dark;

        var documents = TestSampleData.Files("markdown").ToList();
        documents.Add(Path.GetFullPath(Path.Combine(TestSampleData.Root, "..", "docs", "MarkdownSupport.md")));

        foreach (var path in documents)
        {
            var text = File.ReadAllText(path);
            var content = new ContentEngine();
            Settled(content, text, style);

            // Typed in the middle, typed before everything so every block moves, taken back near the end, and undone.
            var middle = WordStart(text, text.Length / 2);
            string[] edits =
            [
                text.Insert(middle, "typed "),
                "Opening words.\n\n" + text.Insert(middle, "typed "),
                text.Length > 10 ? text.Remove(WordStart(text, text.Length - 10), 1) : text,
                text,
            ];

            foreach (var edited in edits)
            {
                // Both sides have to be laid from the same reading of these characters. What a slower reading comes to is
                // kept for every engine by the language, the stage and the characters, so letting it land once here on an
                // engine of its own means both lays below find it kept and neither waits on anything — while the one being
                // laid again keeps the blocks whose reuse is the point of the test.
                Settled(new ContentEngine(), edited, style);

                var again = content.Lay(null, EditState.For(edited), StyleFormat.Dark, Room, false);
                var fresh = new ContentEngine().Lay(null, EditState.For(edited), style, Room, false);

                Same(fresh, again, Path.GetFileName(path));
            }
        }
    }

    /// <summary>
    /// <paramref name="engine"/> having laid <paramref name="text"/>, with every slower reading of it landed and
    /// whatever was laid without them forgotten — which is what a host does when one lands (<c>ContentElement.OnReread</c>
    /// calls <c>Refresh</c>, and refreshing is forgetting and laying again).
    ///
    /// <para>
    /// Code is read twice: as written at once, and by its grammar a moment later, away from the thread that draws. A
    /// layout kept from before the second reading landed is not the layout the same characters make afresh once it has,
    /// and neither of them is wrong — so comparing the two means letting every reading land first.
    /// </para>
    /// <para>
    /// Waits on <see cref="ContentEngine.Rereading"/> rather than on the <c>Reread</c> event, because the event says one
    /// reading landed and not whether more are coming: a document fencing several grammars is read several times over,
    /// and waiting for the first of them settles only part of it.
    /// </para>
    /// </summary>
    private static Laid Settled(ContentEngine engine, string text, StyleFormat style)
    {
        using var landed = new ManualResetEventSlim();

        void Done(object? sender, EventArgs args) => landed.Set();

        engine.Reread += Done;

        try
        {
            var laid = engine.Lay(null, EditState.For(text), style, Room, false);

            for (var settling = 0; settling < Readings && (engine.Rereading || landed.IsSet); settling++)
            {
                if (engine.Rereading) landed.Wait(Patience);

                landed.Reset();
                engine.Forget();
                laid = engine.Lay(null, EditState.For(text), style, Room, false);
            }

            return laid;
        }
        finally
        {
            engine.Reread -= Done;
        }
    }

    /// <summary>How many times a document is laid again while its slower readings land, before waiting is given up on.</summary>
    private const int Readings = 8;

    /// <summary>How long one slower reading is waited for.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [TestMethod]
    public void ADocumentSaysWhenEverySlowerReadingOfItHasLanded()
    {
        // The sample that fences the most languages at once, so it is read a second time more than once over.
        var path = TestSampleData.Files("markdown").First(file => Path.GetFileName(file) == "mixed-content.md");
        var text = File.ReadAllText(path);

        var engine = new ContentEngine();
        var settled = Settled(engine, text, StyleFormat.Dark);

        Assert.IsFalse(engine.Rereading, "nothing of it is still being read a second time");

        // And nothing left to land is what makes the two sides of the test above comparable at all: the same
        // characters laid afresh now find every reading kept, so they come out piece for piece the same.
        Same(settled, new ContentEngine().Lay(null, EditState.For(text), StyleFormat.Dark, Room, false),
             "mixed-content.md");
    }

    [TestMethod]
    public void ABlockThatReadsAsItDidKeepsItsPicture()
    {
        var content = new ContentEngine();

        var before = Wholes(content.Lay(null, EditState.For("First words.\n\nSecond words.\n"), StyleFormat.Dark, Room, false));
        var after = Wholes(content.Lay(null, EditState.For("First words.\n\nSecond words, and more.\n"), StyleFormat.Dark, Room, false));

        Assert.AreSame(before[0].Painting?.Kept, after[0].Painting?.Kept, "the paragraph nobody touched is set down as it was");
        Assert.AreNotSame(before[1].Painting?.Kept, after[1].Painting?.Kept, "the one typed in is laid again");
    }

    [TestMethod]
    public void ABlockSetDownAgainStandsForWhereItsCharactersNowAre()
    {
        var content = new ContentEngine();
        content.Lay(null, EditState.For("alpha\n\nbeta\n"), StyleFormat.Dark, Room, false);

        const string source = "an alpha\n\nbeta\n";
        var laid = content.Lay(null, EditState.For(source), StyleFormat.Dark, Room, false);

        var beta = laid.Root.SelfAndDescendants().First(piece => piece.Words is not null && Written(source, piece) == "beta");

        Assert.AreEqual(source.IndexOf("beta", StringComparison.Ordinal), beta.Part!.Start);
    }

    [TestMethod]
    public void ABlockWhoseLinkWasDefinedAgainElsewhereIsLaidAgain()
    {
        // The paragraph's characters are the same either side of the edit; where its link goes is not.
        var content = new ContentEngine();

        var before = Wholes(content.Lay(null, EditState.For("Go [there][a].\n\n[a]: https://one.example\n"), StyleFormat.Dark, Room, false));
        var after = Wholes(content.Lay(null, EditState.For("Go [there][a].\n\n[a]: https://two.example\n"), StyleFormat.Dark, Room, false));

        Assert.AreNotSame(before[0].Painting?.Kept, after[0].Painting?.Kept);
    }

    [TestMethod]
    public void AfterBeingToldToForgetNothingIsSetDownAsItWas()
    {
        // Which nodes of a diagram are opened is not in the characters, so a host changing it says so.
        var content = new ContentEngine();
        const string source = "First words.\n\nSecond words.\n";

        var before = Wholes(content.Lay(null, EditState.For(source), StyleFormat.Dark, Room, false));
        content.Forget();
        var after = Wholes(content.Lay(null, EditState.For(source), StyleFormat.Dark, Room, false));

        Assert.AreNotSame(before[0].Painting?.Kept, after[0].Painting?.Kept);
    }

    [TestMethod]
    public void ABlockShownAsItWasWrittenIsNotKeptAsIfItWereRead()
    {
        var content = new ContentEngine();
        const string source = "# Title\n\nwords\n";

        content.Lay(null, new EditState(source, 7, null, new RawZone(0, 7)), StyleFormat.Dark, Room, false);
        var read = content.Lay(null, EditState.For(source), StyleFormat.Dark, Room, false);
        var fresh = new ContentEngine().Lay(null, EditState.For(source), StyleFormat.Dark, Room, false);

        Same(fresh, read, "a title shown as written, then read");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static List<Piece> Wholes(Laid laid) =>
        [.. laid.Root.SelfAndDescendants().Where(piece => piece.Kind == MarkdownPieces.Whole)];

    private static string Written(string source, Piece piece) =>
        piece.Part is { } part ? source.Substring(part.Start, part.Length) : "";

    private static int WordStart(string text, int at)
    {
        while (at > 0 && !char.IsWhiteSpace(text[at - 1])) at--;
        return at;
    }

    /// <summary>Two layouts of one source agree about every piece — what it is, where, and what it stands for — and about everything around them.</summary>
    private static void Same(Laid expected, Laid actual, string what)
    {
        Assert.AreEqual(expected.Size, actual.Size, $"{what}: size");

        var want = expected.Root.SelfAndDescendants().ToList();
        var got = actual.Root.SelfAndDescendants().ToList();
        Assert.AreEqual(want.Count, got.Count, $"{what}: pieces{Diverged(want, got)}");

        for (var at = 0; at < want.Count; at++)
        {
            var (a, b) = (want[at], got[at]);

            Assert.AreEqual(a.Kind, b.Kind, $"{what}: piece {at}");
            // Except the line at today, drawn from the clock: two layouts made a moment apart need not agree on the moment.
            if (a.Kind != Nexaflow.Visuals.Text.Markdown.Mermaid.Gantt.GanttPiece.Today)
                Assert.AreEqual(a.Bounds, b.Bounds, $"{what}: where piece {at} ({a.Kind}) stands");
            Assert.AreEqual(Span(a.Part), Span(b.Part), $"{what}: what piece {at} ({a.Kind}) stands for");
            Assert.AreEqual(a.Part?.GetType(), b.Part?.GetType(), $"{what}: piece {at} ({a.Kind})");
        }

        CollectionAssert.AreEqual(expected.Places.Select(place => (place.Offset, place.Trailing)).ToList(),
                                  actual.Places.Select(place => (place.Offset, place.Trailing)).ToList(), $"{what}: caret places");

        CollectionAssert.AreEquivalent(expected.Trouble.Select(Said).ToList(), actual.Trouble.Select(Said).ToList(), $"{what}: trouble");
    }

    /// <summary>
    /// Where two layouts of the same content stop agreeing, for a count that does not match.
    ///
    /// <para>
    /// A count on its own says nothing about which block went wrong, and the two are long: this names the first piece
    /// they differ on and what each said there, which is enough to find the block that was kept when it should not have
    /// been.
    /// </para>
    /// </summary>
    private static string Diverged(IReadOnlyList<Piece> want, IReadOnlyList<Piece> got)
    {
        for (var at = 0; at < Math.Min(want.Count, got.Count); at++)
        {
            if (want[at].Kind == got[at].Kind && Span(want[at].Part) == Span(got[at].Part)) continue;

            return $"\nfirst differ at piece {at}:"
                 + $"\n  afresh: {want[at].Kind} at {want[at].Bounds} for {Span(want[at].Part)}"
                 + $"\n  again : {got[at].Kind} at {got[at].Bounds} for {Span(got[at].Part)}"
                 + $"\n  the piece before: {(at > 0 ? want[at - 1].Kind : "(none)")}";
        }

        return $"\nthey agree for the first {Math.Min(want.Count, got.Count)} pieces, so one simply has more";
    }

    private static (int, int)? Span(ISourcePart? part) => part is null ? null : (part.Start, part.Length);

    private static string Said(Diagnostic trouble) =>
        $"{trouble.Start}+{trouble.Length} {trouble.Message} {Span(trouble.Part)}";

    [TestMethod]
    public void ADrawingOfTheDayItIsReadOnIsKeptWhileItReadsAsItDid()
    {
        // A Gantt chart's today is part of what its block means, read with it — so a block that reads as it did is set down as it was.
        const string chart = "```mermaid\ngantt\n    dateFormat YYYY-MM-DD\n    Task :2024-01-01, 3d\n```\n";
        var content = new ContentEngine();

        var before = Wholes(content.Lay(null, EditState.For(chart + "\nWords.\n"), StyleFormat.Dark, Room, false));
        var after = Wholes(content.Lay(null, EditState.For(chart + "\nMore words.\n"), StyleFormat.Dark, Room, false));

        Assert.AreSame(before[0].Painting?.Kept, after[0].Painting?.Kept);
    }
}
