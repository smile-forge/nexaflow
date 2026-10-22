using Nexaflow.IO.Documents.Processing;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Documents;

/// <summary>
/// A document summarised from its deepest sections up, each handed what the document is, where it stands and what came
/// before it; kept, so summarising again asks only about what changed; and written as a document of its own a reader can go
/// down into, a heading per section leading back to it.
/// </summary>
[TestClass]
[CoversNode("documents-summary")]
public class DocumentSummarizerTests
{
    private const string Document = "Intro words.\n\n# One\n\nAlpha.\n\n## One a\n\nBeta.\n\n# Two\n\nGamma.\n";

    private static readonly DocumentGist Gist = DocumentGist.Read(ScriptedModel.Said)!;

    [TestMethod]
    public async Task ASectionIsSummarisedAfterTheOnesUnderIt_AndFromTheirSummaries()
    {
        var model = ScriptedModel.Plain();

        var summary = await new DocumentSummarizer(model, new MemoryNotes()).SummarizeAsync(DocumentReading.Of(Document), Gist, CancellationToken.None);

        var asked = model.Of(ScriptedModel.Summary);
        var order = asked.Select(ScriptedModel.Where).ToList();
        CollectionAssert.AreEqual(new[] { "One › One a", "One", "Two", "whole" }, order, "the deepest first, the whole last");

        StringAssert.Contains(asked[1], "S(One › One a)", "a section is summarised from the summaries under it");
        StringAssert.Contains(asked[2], "What the part before it says, in brief:\nS(One)", "and handed what the one before it said");
        StringAssert.Contains(asked[3], "Intro words.", "the whole from what comes before the first heading");
        Assert.IsTrue(model.Asked.All(question => question.Instructions.Contains("TOPIC: pets")), "every question is handed what the document is");

        Assert.AreEqual("S(whole)", summary.Summary);
        CollectionAssert.AreEqual(new[] { "One", "Two" }, summary.Under.Select(node => node.Title).ToArray());
        Assert.AreEqual("S(One › One a)", summary.Under[0].Under[0].Summary);
    }

    [TestMethod]
    public async Task SummarisingAgain_AsksOnlyAboutWhatChanged_AndWhatHoldsIt()
    {
        var notes = new MemoryNotes();
        await new DocumentSummarizer(ScriptedModel.Plain(), notes).SummarizeAsync(DocumentReading.Of(Document), Gist, CancellationToken.None);

        var unchanged = ScriptedModel.Plain();
        await new DocumentSummarizer(unchanged, notes).SummarizeAsync(DocumentReading.Of(Document), Gist, CancellationToken.None);
        Assert.AreEqual(0, unchanged.Asked.Count, "nothing changed, nothing asked");

        var edited = ScriptedModel.Plain();
        await new DocumentSummarizer(edited, notes).SummarizeAsync(DocumentReading.Of(Document.Replace("Gamma.", "Delta.")), Gist, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "Two" }, edited.Of(ScriptedModel.Summary).Select(ScriptedModel.Where).Where(where => where != "whole").ToArray(),
                                  "only the section edited, then the whole");
    }

    [TestMethod]
    public async Task ASectionTooLongToReadAtOnce_IsReadAStretchAtATime()
    {
        var long_ = "# Long\n\n" + string.Join("\n\n", Enumerable.Range(1, 30).Select(n => $"Paragraph {n} {new string('w', 900)}."));
        var model = ScriptedModel.Plain(context: 4096);

        await new DocumentSummarizer(model, new MemoryNotes()).SummarizeAsync(DocumentReading.Of(long_), Gist, CancellationToken.None);

        Assert.IsTrue(model.Asked.Any(question => question.Asked.Contains("Your notes on it so far")), "the notes so far are carried into the next stretch");
        Assert.IsTrue(model.Asked.All(question => question.Asked.Length < 4096 * 3), "and no question is more than the model reads");
    }

    [TestMethod]
    public void WrittenOut_ItsHeadingsAreTheSectionsAndLeadBackToThem_AndNothingAModelWroteIsMarkup()
    {
        var reading = DocumentReading.Of(Document);
        var one = reading.Sections.Sections[0];
        var summary = new SummaryNode(null, string.Empty, "# not a heading\n\n*not emphasis*",
            [new SummaryNode(one, "One", "S(One)", [new SummaryNode(one.Sections[0], "One a", "S(One a)", [])])]);

        var written = SummaryMarkdown.Write(summary, Gist, "pets.md", @"C:\docs\pets.md");
        var read = DocumentReading.Of(written);

        CollectionAssert.AreEqual(new[] { "pets.md", "One", "One a" },
                                  read.Sections.All().Select(section => DocumentReading.Words(read.Part(section.Heading)).Replace(" ↗", string.Empty)).ToArray(),
                                  "the title, then each section at the depth it stands");
        StringAssert.Contains(read.Text(0, read.Root.Children.Count), "\\# not a heading", "what the model wrote is spelled as words");

        var back = new Uri(SummaryMarkdown.Back(@"C:\docs\pets.md", "one"));
        Assert.AreEqual(@"C:\docs\pets.md", back.LocalPath);
        var place = ContentPath.Read(Uri.UnescapeDataString(back.Fragment.TrimStart('#')));
        Assert.AreEqual(MarkdownKinds.Heading, place.Steps.Single().Kind);
        Assert.AreEqual("one", place.Steps.Single().Name, "a heading leads back to its section by the name its heading answers to");
    }

    [TestMethod]
    public async Task ADocumentWhoseWholeBodyStandsUnderItsTitle_IsSummarisedByThatSection_NotSaidTwice()
    {
        var model = ScriptedModel.Plain();
        var reading = DocumentReading.Of("# Pets\n\n## Dogs\n\nThey bark.\n\n## Cats\n\nThey purr.\n");

        var summary = await new DocumentSummarizer(model, new MemoryNotes()).SummarizeAsync(reading, Gist, CancellationToken.None);

        Assert.IsFalse(model.Of(ScriptedModel.Summary).Select(ScriptedModel.Where).Contains("whole"), "the title's section already says it all");
        Assert.AreEqual("S(Pets)", summary.Summary);

        var read = DocumentReading.Of(SummaryMarkdown.Write(summary, Gist, "pets", @"C:\docs\pets.md"));
        CollectionAssert.AreEqual(new[] { (1, "Pets"), (2, "Dogs"), (2, "Cats") },
                                  read.Sections.All().Select(section => (section.Rank, DocumentReading.Words(read.Part(section.Heading)).Replace(" ↗", string.Empty))).ToArray(),
                                  "the title once, its sections the summary's outermost");
    }

    [TestMethod]
    public void WhatAModelWrote_KeepsItsInlineMarkup_ButOpensNoBlock()
    {
        var summary = new SummaryNode(null, "Notes", "Call `Run()` *first*.\n# Not a heading\n- not a list", []);

        var read = DocumentReading.Of(SummaryMarkdown.Write(summary, Gist, "notes", source: null));
        var kinds = read.Root.SelfAndDescendants().Select(part => part.Kind).ToHashSet();

        Assert.IsTrue(kinds.Contains(MarkdownKinds.Code) && kinds.Contains(MarkdownKinds.Emphasis), "code and emphasis read as written");
        Assert.AreEqual(1, read.Sections.All().Count(), "the title is the only heading");
        Assert.IsFalse(kinds.Contains(MarkdownKinds.List), "and nothing it wrote opened a list");
    }
}
