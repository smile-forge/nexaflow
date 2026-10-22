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

    private static async Task<string> Translate(IDocumentModel model, IDocumentNotes notes, DocumentGlossary? glossary = null, string source = Document) =>
        (await Translation(model, notes, glossary, source)).Text;

    private static Task<DocumentTranslation> Translation(IDocumentModel model, IDocumentNotes notes, DocumentGlossary? glossary = null, string source = Document) =>
        new DocumentTranslator(model, notes).TranslateAsync(DocumentReading.Of(source), Gist, glossary ?? DocumentGlossary.None, "Shouting", Note, CancellationToken.None);

    /// <summary>What the note before a passage kept as written says.</summary>
    private const string Note = "Kept as written";

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
    public async Task APassageWhoseMarksAreNeverKept_IsKeptAsWritten_WithANote_AndTheRestIsTranslated()
    {
        // The passage with a link and code in it loses them every time; every other passage is translated.
        var model = new ScriptedModel((instructions, asked) => new(
            !instructions.StartsWith(ScriptedModel.Translation, StringComparison.Ordinal) ? ScriptedModel.Answer(instructions, asked)
          : ScriptedModel.Translated(asked, piece => piece.Contains("moon") ? System.Text.RegularExpressions.Regex.Replace(piece, @"⟦[^⟧]*⟧", string.Empty) : ScriptedModel.Shout(piece)),
            DocumentReplyEnd.Finished));

        var translation = await Translation(model, new MemoryNotes());

        StringAssert.Contains(translation.Text, $"*{Note}*\n\nDogs **bark** at [the moon](https://moon.example) and `ls -la` too.",
            "the passage as the document writes it, the note before it");
        StringAssert.Contains(translation.Text, "- CATS PURR", "and the rest translated all the same");
        CollectionAssert.AreEqual(new[] { "Pets" }, translation.Untranslated.ToArray(), "where it stands is said");
    }

    [TestMethod]
    public async Task AModelThatCannotBeAsked_StopsTheJob_SayingWhereAndWhy()
    {
        var model = new ScriptedModel((instructions, asked) => instructions.StartsWith(ScriptedModel.Translation, StringComparison.Ordinal)
            ? new(string.Empty, DocumentReplyEnd.Failed, "the host is gone")
            : new(ScriptedModel.Answer(instructions, asked), DocumentReplyEnd.Finished));

        var failed = await Assert.ThrowsExactlyAsync<DocumentWorkException>(() => Translation(model, new MemoryNotes()));

        StringAssert.Contains(failed.Message, "Pets");
        StringAssert.Contains(failed.Message, "the host is gone");
        Assert.HasCount(2, model.Of(ScriptedModel.Translation), "its first passage asked once more, then the job stopped");
    }

    [TestMethod]
    public async Task APassageNoReplyCanBeHadFor_IsKeptAsWritten_WithANote_AndTheRestIsTranslated()
    {
        var model = new ScriptedModel((instructions, asked) =>
            instructions.StartsWith(ScriptedModel.Translation, StringComparison.Ordinal) && ScriptedModel.Pieces(asked).Any(piece => piece.Contains("Cats are"))
                ? new(string.Empty, DocumentReplyEnd.Failed, "timed out")
                : new(ScriptedModel.Answer(instructions, asked), DocumentReplyEnd.Finished), context: 1000);

        var translation = await Translation(model, new MemoryNotes(), source: Sections("Dogs", "Cats", "Birds"));

        StringAssert.Contains(translation.Text, $"*{Note}*\n\n{Company("Cats")}", "kept as written, the note before it");
        StringAssert.Contains(translation.Text, Company("Dogs").ToUpperInvariant());
        StringAssert.Contains(translation.Text, Company("Birds").ToUpperInvariant(), "the passages after it translated all the same");
        CollectionAssert.Contains(translation.Untranslated.ToList(), "Pets › Cats");
        Assert.HasCount(2, model.Of(ScriptedModel.Translation).Where(asked => ScriptedModel.Pieces(asked).Any(piece => piece.Contains("Cats are"))).ToList(),
                           "asked for once more before it was kept");
    }

    [TestMethod]
    public async Task AModelLostPartWay_StopsTheJob_OnceItsRepliesFailOneAfterAnother()
    {
        var answered = 0;
        var model = new ScriptedModel((instructions, asked) =>
            !instructions.StartsWith(ScriptedModel.Translation, StringComparison.Ordinal) || answered++ == 0
                ? new(ScriptedModel.Answer(instructions, asked), DocumentReplyEnd.Finished)
                : new(string.Empty, DocumentReplyEnd.Failed, "the host is gone"), context: 1000);

        var failed = await Assert.ThrowsExactlyAsync<DocumentWorkException>(() => Translation(model, new MemoryNotes(), source: Sections("Dogs", "Cats", "Birds", "Fish")));

        StringAssert.Contains(failed.Message, "the host is gone");
        Assert.HasCount(5, model.Of(ScriptedModel.Translation), "one answered, then two passages asked twice each");
    }

    [TestMethod]
    public async Task AnAnswer_IsHeldToTwiceWhatItTranslates()
    {
        var model = ScriptedModel.Plain();

        await Translate(model, new MemoryNotes());

        var asked = model.Asked.Zip(model.Longest).Where(question => question.First.Instructions.StartsWith(ScriptedModel.Translation, StringComparison.Ordinal)).ToList();
        Assert.IsNotEmpty(asked);
        foreach (var ((_, question), longest) in asked)
        {
            var translating = ScriptedModel.Pieces(question).Sum(piece => piece.Length);
            Assert.IsTrue(longest >= 2 * translating, "room for a translation longer than what it translates");
            Assert.IsTrue(longest < DocumentBudget.Of(model).Characters, "but not all the model could write");
        }
    }

    /// <summary>A document of a section for each of <paramref name="names"/>, each a passage long enough to be asked for on its own by a model that reads a page.</summary>
    private static string Sections(params string[] names) => "# Pets\n\n" + string.Concat(names.Select(name => $"## {name}\n\n{Company(name)}\n\n"));

    /// <summary>What the section on <paramref name="name"/> says.</summary>
    private static string Company(string name) => string.Concat(Enumerable.Repeat($"{name} are good company. ", 30)).TrimEnd();

    [TestMethod]
    public async Task APassageKeptAsWritten_IsAskedForAgain_WhenTranslatingAgain()
    {
        var notes = new MemoryNotes();
        var losing = new ScriptedModel((_, asked) => new(ScriptedModel.Translated(asked, piece => System.Text.RegularExpressions.Regex.Replace(piece, @"⟦[^⟧]*⟧", string.Empty)), DocumentReplyEnd.Finished));
        await Translation(losing, notes);

        var keeping = ScriptedModel.Plain();
        var again = await Translation(keeping, notes);

        Assert.AreEqual(0, again.Untranslated.Count);
        StringAssert.Contains(again.Text, "DOGS **BARK**", "nothing kept stands in for a translation");
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

    [TestMethod]
    public async Task AnAnswerAddingAMarkOfItsOwn_IsTranslatedAllTheSame()
    {
        var model = new ScriptedModel((instructions, asked) => new(
            instructions.StartsWith(ScriptedModel.Translation, StringComparison.Ordinal)
                ? ScriptedModel.Translated(asked, piece => ScriptedModel.Shout(piece) + " ⟦99⟧")
                : ScriptedModel.Answer(instructions, asked),
            DocumentReplyEnd.Finished));

        var translation = await Translation(model, new MemoryNotes());

        Assert.AreEqual(0, translation.Untranslated.Count);
        StringAssert.Contains(translation.Text, "- CATS PURR");
        Assert.IsFalse(translation.Text.Contains("⟦"), "and nothing it made up is in it");
    }

    [TestMethod]
    public async Task PassagesAnsweredShortTogether_AreAskedForInHalves_NotEachOnItsOwn()
    {
        // Asked more than two at once, the model leaves the last out.
        var model = new ScriptedModel((instructions, asked) =>
        {
            if (!instructions.StartsWith(ScriptedModel.Translation, StringComparison.Ordinal)) return new(ScriptedModel.Answer(instructions, asked), DocumentReplyEnd.Finished);

            var pieces = ScriptedModel.Pieces(asked);
            var answered = ScriptedModel.Translated(asked, ScriptedModel.Shout);
            return new(pieces.Count > 2 ? answered[..answered.LastIndexOf("@@", StringComparison.Ordinal)] : answered, DocumentReplyEnd.Finished);
        });

        var translation = await Translation(model, new MemoryNotes());
        var asked = model.Of(ScriptedModel.Translation);

        Assert.AreEqual(0, translation.Untranslated.Count);
        Assert.IsTrue(asked.Any(question => ScriptedModel.Pieces(question).Count == 2), "halves are asked for");
        Assert.IsFalse(asked.Skip(2).All(question => ScriptedModel.Pieces(question).Count == 1), "not every piece on its own");
    }
}
