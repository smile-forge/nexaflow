using System.Text.RegularExpressions;

using Nexaflow.IO.Documents.Processing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Documents;

/// <summary>
/// A question about a whole document answered by going down what its summary says of each section to the parts that bear on
/// it, reading only those, a reading at a time, and citing them by number — from the record a section pass kept, matched to
/// the document as it is now.
/// </summary>
[TestClass]
[CoversNode("documents-answer")]
public class DocumentAnswererTests
{
    private static readonly DocumentGist Gist = DocumentGist.Read(ScriptedModel.Said)!;

    private static readonly string Polar = "Polar form writes a number by its modulus and its angle. " + new string('p', 600);

    private static readonly string Algebra = "The square of i is minus one. " + new string('a', 600);

    private static readonly string Numbers =
        $"# Numbers\n\nHow numbers are built.\n\n## Real\n\nThe reals fill the line.\n\n## Complex\n\n### Polar\n\n{Polar}\n\n### Algebra\n\n{Algebra}\n\n# Pets\n\nDogs bark.\n";

    [TestMethod]
    public async Task AQuestion_GoesDownToThePartsThatBearOnIt_ReadsOnlyThose_AndCitesThem()
    {
        var (reading, record) = await RecordedAsync(Numbers);
        var model = Answering(Choose("Numbers", "Complex", "Polar"), "Polar form gives a modulus and an angle [1].", context: 1024);

        var answer = await new DocumentAnswerer(model).AnswerAsync(reading, record, "How are complex numbers written by angle?", CancellationToken.None);

        var choosing = model.Of(ScriptedModel.Choosing);
        Assert.AreEqual(3, choosing.Count, "the outermost sections, then Numbers', then Complex's");
        StringAssert.Contains(choosing[0], "1. Numbers:\nS(Numbers)", "each part is chosen by what its summary says");
        StringAssert.Contains(choosing[1], "Before the sections under it:\nHow numbers are built.", "and what a section says before its own sections is a part too");

        var part = answer.Parts.Single();
        Assert.AreEqual("Numbers › Complex › Polar", part.Where);
        Assert.AreEqual("polar", part.Section?.Anchor, "it leads back to its section");
        Assert.IsTrue(part.Cited);
        Assert.IsFalse(answer.InBrief);

        var read = model.Of(ScriptedModel.Answering).Single();
        StringAssert.Contains(read, "[1] Numbers › Complex › Polar:\nPolar form writes");
        Assert.IsFalse(read.Contains("square of i") || read.Contains("Dogs bark"), "nothing not chosen is read");
        Assert.IsTrue(model.Asked.All(question => question.Instructions.Contains("TOPIC: pets")), "every question is handed what the document is");
    }

    [TestMethod]
    public async Task ALevelWithOnePartToChooseFrom_IsGoneDownWithoutAsking_AndAShortSectionIsReadWhole()
    {
        var (reading, record) = await RecordedAsync("# Pets\n\n## Dogs\n\nThey bark.\n\n## Cats\n\nThey purr.\n");
        var model = Answering(Choose(), "They purr [1].");

        var answer = await new DocumentAnswerer(model).AnswerAsync(reading, record, "What do cats do?", CancellationToken.None);

        Assert.AreEqual(0, model.Of(ScriptedModel.Choosing).Count, "one title over everything leaves nothing to choose");
        Assert.AreEqual("Pets", answer.Parts.Single().Where);
        StringAssert.Contains(model.Of(ScriptedModel.Answering).Single(), "## Cats\n\nThey purr.", "a short section is read whole, its own sections in it");
    }

    [TestMethod]
    public async Task WhereNoPartBearsOnIt_NothingIsRead_AndNothingAnswered()
    {
        var (reading, record) = await RecordedAsync(Numbers);
        var model = Answering((_, _) => "NONE", "unasked");

        var answer = await new DocumentAnswerer(model).AnswerAsync(reading, record, "What is the capital of France?", CancellationToken.None);

        Assert.AreEqual(string.Empty, answer.Said);
        Assert.AreEqual(0, answer.Parts.Count);
        Assert.AreEqual(0, model.Of(ScriptedModel.Answering).Count);
    }

    [TestMethod]
    public async Task AnAnswerNamingParts_IsReadForTheNumbersItOpensEachLineWith()
    {
        var (reading, record) = await RecordedAsync("# One\n\nAlpha.\n\n# Two\n\nBeta.\n\n# Three\n\nGamma.\n");
        var model = Answering((_, _) => "Parts 1 and 3 — not 2, which says nothing of it", "Alpha and gamma [1, 2].");

        var answer = await new DocumentAnswerer(model).AnswerAsync(reading, record, "Which?", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "One", "Three" }, answer.Parts.Select(part => part.Where).ToArray(), "what follows a word is not read as a choice");
        Assert.IsTrue(answer.Parts.All(part => part.Cited), "a citation may name several parts");
    }

    [TestMethod]
    public async Task PartsTooLongForOneReading_AreReadInSeveral_TheAnswerSoFarCarriedIntoEach()
    {
        var (reading, record) = await RecordedAsync(Sections(3, 1000), context: 1024);
        var model = Answering(ChooseAll, "So far [1].", context: 1024);

        var answer = await new DocumentAnswerer(model).AnswerAsync(reading, record, "What do they say?", CancellationToken.None);

        var readings = model.Of(ScriptedModel.Answering);
        Assert.IsTrue(readings.Count > 1);
        Assert.IsTrue(readings.Skip(1).All(asked => asked.Contains("Your answer so far, from the parts before these:\nSo far [1].")));
        Assert.IsTrue(model.Asked.All(question => question.Asked.Length < 4096), "and no question is more than the model reads");
        Assert.IsFalse(answer.InBrief);
    }

    [TestMethod]
    public async Task PartsTooMuchToReadInFull_AreReadFromWhatTheirSummariesSay()
    {
        var (reading, record) = await RecordedAsync(Sections(12, 1000), context: 1024);
        var model = Answering(ChooseAll, "In brief [3].", context: 1024);

        var answer = await new DocumentAnswerer(model).AnswerAsync(reading, record, "What do they say?", CancellationToken.None);

        Assert.IsTrue(answer.InBrief);
        Assert.AreEqual(12, answer.Parts.Count);
        var read = string.Join("\n", model.Of(ScriptedModel.Answering));
        StringAssert.Contains(read, "[3] Section 3:\nS(Section 3)");
        Assert.IsFalse(read.Contains(new string('w', 100)), "what they say in full is not read");
    }

    [TestMethod]
    public async Task WhatAPassCameTo_IsKeptAsTheRecordOfWhatTheDocumentSaysNow()
    {
        var notes = new MemoryNotes();
        var reading = DocumentReading.Of(Numbers);
        var result = await new SectionPass(ScriptedModel.Plain(), notes, new SummaryQuestion()).RunAsync(reading, Gist, CancellationToken.None);

        Assert.IsTrue(DocumentRecord.Held(notes, SummaryQuestion.Shelf, Numbers));
        var record = await DocumentRecord.ReadAsync(notes, SummaryQuestion.Shelf, DocumentReading.Of(Numbers), CancellationToken.None);

        Assert.IsNotNull(record);
        Assert.AreEqual(Gist, record.Gist);
        Assert.AreEqual(result.Said, record.Said.Said);
        var complex = record.Said.Under[0].Under[1];
        Assert.AreEqual(("Complex", "complex", "S(Numbers › Complex)"), (complex.Title, complex.Section?.Anchor, complex.Said), "matched to the document's own sections");
        CollectionAssert.AreEqual(new[] { "Polar", "Algebra" }, complex.Under.Select(node => node.Title).ToArray());

        var edited = Numbers.Replace("Dogs bark.", "Dogs howl.");
        Assert.IsFalse(DocumentRecord.Held(notes, SummaryQuestion.Shelf, edited), "an edited document has no record until the pass works over it again");
        Assert.IsNull(await DocumentRecord.ReadAsync(notes, SummaryQuestion.Shelf, DocumentReading.Of(edited), CancellationToken.None));
    }

    /// <summary>A document of <paramref name="count"/> sections, each saying <paramref name="length"/> characters.</summary>
    private static string Sections(int count, int length) =>
        string.Concat(Enumerable.Range(1, count).Select(n => $"# Section {n}\n\n{new string('w', length)}.\n\n"));

    /// <summary><paramref name="source"/> read, and the record a summary of it leaves.</summary>
    private static async Task<(DocumentReading Reading, DocumentRecord Record)> RecordedAsync(string source, int context = 8192)
    {
        var notes = new MemoryNotes();
        var reading = DocumentReading.Of(source);
        await new SectionPass(ScriptedModel.Plain(context), notes, new SummaryQuestion()).RunAsync(reading, Gist, CancellationToken.None);

        return (reading, (await DocumentRecord.ReadAsync(notes, SummaryQuestion.Shelf, reading, CancellationToken.None))!);
    }

    /// <summary>A model that chooses parts with <paramref name="choose"/> and answers with <paramref name="answer"/>.</summary>
    private static ScriptedModel Answering(Func<string, string, string> choose, string answer, int context = 8192) =>
        new((instructions, asked) => new(
            instructions.StartsWith(ScriptedModel.Choosing, StringComparison.Ordinal) ? choose(instructions, asked)
          : instructions.StartsWith(ScriptedModel.Answering, StringComparison.Ordinal) ? answer
          : ScriptedModel.Answer(instructions, asked),
            DocumentReplyEnd.Finished), context);

    /// <summary>Chooses the parts titled one of <paramref name="titles"/>, a number and its title a line.</summary>
    private static Func<string, string, string> Choose(params string[] titles) => (_, asked) =>
        string.Join('\n', Options(asked).Where(option => titles.Contains(option.Title)).Select(option => $"{option.Number}. {option.Title}"));

    /// <summary>Chooses every part.</summary>
    private static string ChooseAll(string instructions, string asked) => string.Join('\n', Options(asked).Select(option => option.Number));

    private static IEnumerable<(int Number, string Title)> Options(string asked) =>
        Regex.Matches(asked, @"^(\d+)\. (.+):$", RegexOptions.Multiline).Select(match => (int.Parse(match.Groups[1].Value), match.Groups[2].Value));
}
