using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Prose;

/// <summary>
/// Every heading answers to the name GitHub gives it — the name Markdig's own identifiers give it — and a long document read
/// a stretch at a time names its headings exactly as it does read whole, however the stretches fall.
/// </summary>
[TestClass]
[CoversNode("md-anchor-links")]
public class MarkdownHeadingNamesTests
{
    [TestMethod]
    public void EverySample_NamesItsHeadings_AsMarkdigsGitHubIdentifiersDo()
    {
        var theirs = MarkdownParser.Reading(new MarkdownPipelineBuilder());
        theirs.Extensions.RemoveAll(extension => extension is MarkdownHeadingNames);
        var markdig = theirs.UseAutoIdentifiers(AutoIdentifierOptions.GitHub).Build();

        foreach (var path in Documents())
        {
            var text = File.ReadAllText(path);

            CollectionAssert.AreEqual(Names(Markdig.Markdown.Parse(text, markdig)), Names(Markdig.Markdown.Parse(text, MarkdownParser.Pipeline)),
                                      Path.GetFileName(path));
        }
    }

    [TestMethod]
    public void HeadingsOfOneName_InDifferentStretches_AreNamedAsTheWholeDocumentNamesThem()
    {
        var source = string.Concat(Enumerable.Range(0, 60).Select(at =>
            at % 7 == 3 ? "> ## Notes\n>\n> quoted\n\n" : $"## Notes\n\nParagraph {at} says something.\n\n"));

        foreach (var length in new[] { 1, 64, 4096 })
        {
            var chunks = new ContentChunks(source, MarkdownParser.Language);
            var parse = MarkdownParser.Windowed();

            while (chunks.Next(length) is { } next)
                chunks.Add(ContentChunk.Read(parse, source, next.From, next.SoftMaxLength, next.Resume, null));

            CollectionAssert.AreEqual(Anchors(MarkdownParser.Parsing()(source).Tree), Anchors(chunks.Tree()), $"read {length} at a time");
        }
    }

    [TestMethod]
    public void AHeadingRenamedPartWayDown_IsReadOnPastIt_SoEveryLaterNameMovesWithIt()
    {
        var source = string.Concat(Enumerable.Range(0, 60).Select(at => $"## Notes\n\nParagraph {at}.\n\n"));
        var parse = MarkdownParser.Windowed();
        var chunks = new ContentChunks(source, MarkdownParser.Language);

        while (chunks.Next(64) is { } next) chunks.Add(ContentChunk.Read(parse, source, next.From, next.SoftMaxLength, next.Resume, null));

        var renamed = source.Insert(source.IndexOf("## Notes\n\nParagraph 10.", StringComparison.Ordinal) + 3, "Other ");
        var (edited, _) = chunks.Edited(renamed, parse, 64);

        while (edited.Next(64) is { } next) edited.Add(ContentChunk.Read(parse, renamed, next.From, next.SoftMaxLength, next.Resume, null));

        CollectionAssert.AreEqual(Anchors(MarkdownParser.Parsing()(renamed).Tree), Anchors(edited.Tree()));
    }

    [TestMethod]
    public void AHeadingRenamedPartWayDown_IsReadAgainOnlyAsFarAsIsAsked_AndTheRestIsReadLater()
    {
        var source = string.Concat(Enumerable.Range(0, 200).Select(at => $"## Notes\n\nParagraph {at}.\n\n"));
        var parse = MarkdownParser.Windowed();
        var chunks = new ContentChunks(source, MarkdownParser.Language);

        while (chunks.Next(64) is { } next) chunks.Add(ContentChunk.Read(parse, source, next.From, next.SoftMaxLength, next.Resume, null));

        var renamed = source.Insert(source.IndexOf("## Notes\n\nParagraph 10.", StringComparison.Ordinal) + 3, "Other ");
        var enough = renamed.IndexOf("Paragraph 30.", StringComparison.Ordinal);
        var (edited, _) = chunks.Edited(renamed, parse, 64, enough);

        Assert.IsFalse(edited.Done, "All of it was read again.");
        Assert.IsTrue(edited.Reached < renamed.Length / 2, $"Read again to {edited.Reached} of {renamed.Length}.");

        while (edited.Next(64) is { } next) edited.Add(ContentChunk.Read(parse, renamed, next.From, next.SoftMaxLength, next.Resume, null));

        CollectionAssert.AreEqual(Anchors(MarkdownParser.Parsing()(renamed).Tree), Anchors(edited.Tree()));
    }

    private static List<string> Documents()
    {
        var documents = TestSampleData.Files("markdown").ToList();
        documents.Add(Path.GetFullPath(Path.Combine(TestSampleData.Root, "..", "docs", "MarkdownSupport.md")));

        return documents;
    }

    private static List<string?> Names(MarkdownDocument document) =>
        [.. document.Descendants<HeadingBlock>().Select(heading => heading.GetAttributes().Id)];

    /// <summary>The name each top-level heading of a read document was given, with where it stands.</summary>
    private static List<string> Anchors(ContentNode tree) =>
        [.. tree.Children.Where(block => block.Kind == MarkdownKinds.Heading)
                .Select(heading => $"{heading.Offset}:{heading.SelfAndDescendants().FirstOrDefault(node => node.Held is string && node.Kind == MarkdownKinds.Anchor)?.Held}")];
}
