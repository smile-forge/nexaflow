using Nexaflow.IO.Documents.Processing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Documents;

/// <summary>
/// A document translated passage by passage: its words in the other language and everything else exactly as written; every
/// passage handed what the document is and how its names are given; an answer that loses a mark asked for again, one that
/// runs out of room asked for in halves; kept, so translating again asks nothing.
/// </summary>
[TestClass]
[CoversNode("documents-translation")]
public class DocumentTranslatorTests
{
    private const string Document =
        "---\ntitle: Pets\n---\n# Pets\n\n" +
        "Dogs **bark** at [the moon](https://moon.example) and `ls -la` too.\n\n" +
        "```python\nprint(\"dogs\")\n```\n\n" +
        "| Name | Sound |\n|------|-------|\n| Dog  | Woof  |\n\n" +
        "- Cats purr\n- Birds sing\n\n" +
        "See [the guide][guide].\n\n" +
        "[guide]: https://guide.example\n";

    private static readonly DocumentGist Gist = DocumentGist.Read(ScriptedModel.Said)!;

    private static Task<string> Translate(IDocumentModel model, IDocumentNotes notes, DocumentGlossary? glossary = null, string source = Document) =>
        new DocumentTranslator(model, notes).TranslateAsync(DocumentReading.Of(source), Gist, glossary ?? DocumentGlossary.None, "Shouting", CancellationToken.None);

    [TestMethod]
    public async Task TheWordsAreTranslated_AndEverythingElseIsAsWritten()
    {
        var translated = await Translate(ScriptedModel.Plain(), new MemoryNotes());

        foreach (var kept in new[] { "title: Pets", "print(\"dogs\")", "(https://moon.example)", "`ls -la`", "[guide]: https://guide.example", "|------|-------|" })
            StringAssert.Contains(translated, kept, "held back, so written as it was");
        foreach (var words in new[] { "# PETS", "DOGS **BARK** AT [THE MOON]", "| NAME | SOUND |", "- CATS PURR", "- BIRDS SING", "SEE [THE GUIDE][guide]." })
            StringAssert.Contains(translated, words, "the words translated, in their markup");
    }

    [TestMethod]
    public async Task EveryPassage_IsHandedWhatTheDocumentIs_AndTheGlossaryEntriesItUses()
    {
        var model = ScriptedModel.Plain();
        var glossary = new DocumentGlossary([("Dogs", "HOUNDS"), ("Unicorns", "HORNS")]);

        await Translate(model, new MemoryNotes(), glossary);

        var instructions = model.Asked.Select(question => question.Instructions).ToList();
        Assert.IsTrue(instructions.All(said => said.Contains("REGISTER: neutral")));
        Assert.IsTrue(instructions.Any(said => said.Contains("Dogs => HOUNDS")), "an entry the passage uses");
        Assert.IsFalse(instructions.Any(said => said.Contains("Unicorns")), "and none it does not");
    }

    [TestMethod]
    public async Task TranslatingAgain_AsksNothing()
    {
        var notes = new MemoryNotes();
        var first = await Translate(ScriptedModel.Plain(), notes);

        var again = ScriptedModel.Plain();
        Assert.AreEqual(first, await Translate(again, notes));
        Assert.AreEqual(0, again.Asked.Count);
    }

    [TestMethod]
    public async Task AnAnswerLosingAMark_IsAskedForAgainSayingWhatWasWrong()
    {
        var wrongOnce = true;
        var model = new ScriptedModel((instructions, asked) =>
        {
            if (!instructions.StartsWith(ScriptedModel.Translation, StringComparison.Ordinal)) return new(ScriptedModel.Answer(instructions, asked), DocumentReplyEnd.Finished);

            var answer = ScriptedModel.Translated(asked, ScriptedModel.Shout);
            if (wrongOnce && answer.Contains("⟦0⟧"))
            {
                wrongOnce = false;
                answer = answer.Replace("⟦0⟧", string.Empty);
            }

            return new(answer, DocumentReplyEnd.Finished);
        });

        var translated = await Translate(model, new MemoryNotes());

        StringAssert.Contains(translated, "DOGS **BARK**");
        Assert.IsTrue(model.Asked.Any(question => question.Asked.Contains("Your last answer was wrong") && question.Asked.Contains("is missing")));
    }

    [TestMethod]
    public async Task AModelThatNeverKeepsTheMarks_StopsTheJob_SayingWhere()
    {
        var model = new ScriptedModel((_, asked) => new(ScriptedModel.Translated(asked, piece => System.Text.RegularExpressions.Regex.Replace(piece, @"⟦[^⟧]*⟧", string.Empty)), DocumentReplyEnd.Finished));

        var failed = await Assert.ThrowsExactlyAsync<DocumentWorkException>(() => Translate(model, new MemoryNotes()));

        StringAssert.Contains(failed.Message, "Pets");
    }

    [TestMethod]
    public async Task APassageThatRunsOutOfRoom_IsAskedForInHalves()
    {
        var model = new ScriptedModel((_, asked) => ScriptedModel.Pieces(asked) is { Count: > 1 }
            ? new(string.Empty, DocumentReplyEnd.LengthLimit)
            : new(ScriptedModel.Translated(asked, ScriptedModel.Shout), DocumentReplyEnd.Finished));

        var translated = await Translate(model, new MemoryNotes());

        StringAssert.Contains(translated, "- CATS PURR");
    }

    [TestMethod]
    public async Task OnlyNamesUsedMoreThanOnce_AndFoundInTheDocument_MakeTheGlossary()
    {
        var model = new ScriptedModel((instructions, _) => new(
            instructions.StartsWith(ScriptedModel.Glossary, StringComparison.Ordinal) ? "- Rex\n- Moon\n- Unicorn"
          : instructions.StartsWith(ScriptedModel.GlossaryTranslation, StringComparison.Ordinal) ? "Rex => Rexo"
          : string.Empty, DocumentReplyEnd.Finished));

        var glossary = await new DocumentGlossaryPass(model, new MemoryNotes())
            .GatherAsync(DocumentReading.Of("Rex runs. Rex sleeps. The Moon rises.\n"), Gist, "Esperanto", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { ("Rex", "Rexo") }, glossary.Entries.ToArray(), "the moon is named once, and no unicorn is in it");
    }

    [TestMethod]
    public async Task ALinkIntoTheDocument_LeadsToItsHeadingByTheNameItHasNow()
    {
        const string Source = "# Pets\n\nSee [the cats](#cats), and [them][c].\n\n## Cats\n\nThey purr.\n\n[c]: #cats\n";
        var model = new ScriptedModel((instructions, asked) => new(
            instructions.StartsWith(ScriptedModel.Translation, StringComparison.Ordinal)
                ? ScriptedModel.Translated(asked, piece => piece.Replace("Cats", "Chats"))
                : ScriptedModel.Answer(instructions, asked),
            DocumentReplyEnd.Finished));

        var translated = await Translate(model, new MemoryNotes(), source: Source);

        StringAssert.Contains(translated, "## Chats");
        StringAssert.Contains(translated, "[the cats](#chats)", "a link's place is the heading's new name");
        StringAssert.Contains(translated, "[c]: #chats", "and so is a definition's");
    }
}
