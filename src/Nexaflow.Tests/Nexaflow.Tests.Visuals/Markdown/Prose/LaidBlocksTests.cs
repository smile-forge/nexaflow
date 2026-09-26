using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
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
        var options = new DiagramRenderOptions { Palette = style, ReadOnly = false };

        var documents = TestSampleData.Files("markdown").ToList();
        documents.Add(Path.GetFullPath(Path.Combine(TestSampleData.Root, "..", "docs", "MarkdownSupport.md")));

        foreach (var path in documents)
        {
            var text = File.ReadAllText(path);
            var content = MarkdownContent.Of(style, new ContentEngine(options));
            content.Lay(EditState.For(text), Room, false);

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
                var again = content.Lay(EditState.For(edited), Room, false);
                var fresh = MarkdownContent.Of(style, new ContentEngine(options)).Lay(EditState.For(edited), Room, false);

                Same(fresh, again, Path.GetFileName(path));
            }
        }
    }

    [TestMethod]
    public void ABlockThatReadsAsItDidKeepsItsPicture()
    {
        var content = MarkdownContent.Of(StyleFormat.Dark, new ContentEngine());

        var before = Wholes(content.Lay(EditState.For("First words.\n\nSecond words.\n"), Room, false));
        var after = Wholes(content.Lay(EditState.For("First words.\n\nSecond words, and more.\n"), Room, false));

        Assert.AreSame(before[0].Painting?.Kept, after[0].Painting?.Kept, "the paragraph nobody touched is set down as it was");
        Assert.AreNotSame(before[1].Painting?.Kept, after[1].Painting?.Kept, "the one typed in is laid again");
    }

    [TestMethod]
    public void ABlockSetDownAgainStandsForWhereItsCharactersNowAre()
    {
        var content = MarkdownContent.Of(StyleFormat.Dark, new ContentEngine());
        content.Lay(EditState.For("alpha\n\nbeta\n"), Room, false);

        const string source = "an alpha\n\nbeta\n";
        var laid = content.Lay(EditState.For(source), Room, false);

        var beta = laid.Root.SelfAndDescendants().First(piece => piece.Words is not null && Written(source, piece) == "beta");

        Assert.AreEqual(source.IndexOf("beta", StringComparison.Ordinal), beta.Part!.Start);
    }

    [TestMethod]
    public void ABlockWhoseLinkWasDefinedAgainElsewhereIsLaidAgain()
    {
        // The paragraph's characters are the same either side of the edit; where its link goes is not.
        var content = MarkdownContent.Of(StyleFormat.Dark, new ContentEngine());

        var before = Wholes(content.Lay(EditState.For("Go [there][a].\n\n[a]: https://one.example\n"), Room, false));
        var after = Wholes(content.Lay(EditState.For("Go [there][a].\n\n[a]: https://two.example\n"), Room, false));

        Assert.AreNotSame(before[0].Painting?.Kept, after[0].Painting?.Kept);
    }

    [TestMethod]
    public void AfterBeingToldToForgetNothingIsSetDownAsItWas()
    {
        // Which nodes of a diagram are opened is not in the characters, so a host changing it says so.
        var content = MarkdownContent.Of(StyleFormat.Dark, new ContentEngine());
        const string source = "First words.\n\nSecond words.\n";

        var before = Wholes(content.Lay(EditState.For(source), Room, false));
        content.Forget();
        var after = Wholes(content.Lay(EditState.For(source), Room, false));

        Assert.AreNotSame(before[0].Painting?.Kept, after[0].Painting?.Kept);
    }

    [TestMethod]
    public void ABlockShownAsItWasWrittenIsNotKeptAsIfItWereRead()
    {
        var content = MarkdownContent.Of(StyleFormat.Dark, new ContentEngine());
        const string source = "# Title\n\nwords\n";

        content.Lay(new EditState(source, 7, null, new RawZone(0, 7)), Room, false);
        var read = content.Lay(EditState.For(source), Room, false);
        var fresh = MarkdownContent.Of(StyleFormat.Dark, new ContentEngine()).Lay(EditState.For(source), Room, false);

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
        Assert.AreEqual(want.Count, got.Count, $"{what}: pieces");

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

    private static (int, int)? Span(ISourcePart? part) => part is null ? null : (part.Start, part.Length);

    private static string Said(Diagnostic trouble) =>
        $"{trouble.Start}+{trouble.Length} {trouble.Message} {Span(trouble.Part)}";

    [TestMethod]
    public void ADrawingOfTheDayItIsReadOnIsKeptWhileItReadsAsItDid()
    {
        // A Gantt chart's today is part of what its block means, read with it — so a block that reads as it did is set down as it was.
        const string chart = "```mermaid\ngantt\n    dateFormat YYYY-MM-DD\n    Task :2024-01-01, 3d\n```\n";
        var content = MarkdownContent.Of(StyleFormat.Dark, new ContentEngine());

        var before = Wholes(content.Lay(EditState.For(chart + "\nWords.\n"), Room, false));
        var after = Wholes(content.Lay(EditState.For(chart + "\nMore words.\n"), Room, false));

        Assert.AreSame(before[0].Painting?.Kept, after[0].Painting?.Kept);
    }
}
