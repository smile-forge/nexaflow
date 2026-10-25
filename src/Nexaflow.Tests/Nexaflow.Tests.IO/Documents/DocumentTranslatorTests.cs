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

        var failed = await Assert.ThrowsExactlyAsync<DocumentWorkException>(() => Translation(model, new MemoryNotes(), source: Paragraphs("Dogs", "Cats", "Birds", "Fish")));

        StringAssert.Contains(failed.Message, "the host is gone");
        Assert.HasCount(5, model.Of(ScriptedModel.Translation), "one answered, then two passages asked twice each");
    }

    [TestMethod]
    public async Task AStretchWithAMarkWrong_IsAskedForAgainOnItsOwn_TheRestOfTheAnswerKept()
    {
        const string source = "# Pets\n\nAlpha `a` one. Beta `b` two. Gamma `c` three. Delta `d` four. Epsilon `e` five. Zeta `f` six. Eta `g` seven. Theta `h` eight.\n";
        var model = new ScriptedModel((_, asked) =>
        {
            var answer = ScriptedModel.Translated(asked, ScriptedModel.Shout);
            return new(ScriptedModel.Pieces(asked).Any(piece => piece.StartsWith("Alpha", StringComparison.Ordinal)) ? answer.Replace("⟦3 `D`⟧", "⟦3 `D`⟧ ⟦3 `D`⟧") : answer,
                       DocumentReplyEnd.Finished);
        });

        var translated = await Translate(model, new MemoryNotes(), source: source);

        StringAssert.Contains(translated, "ALPHA `a` ONE. BETA `b` TWO. GAMMA `c` THREE. DELTA `d` FOUR. EPSILON `e` FIVE. ZETA `f` SIX. ETA `g` SEVEN. THETA `h` EIGHT.");
        var asked = model.Of(ScriptedModel.Translation).Select(ScriptedModel.Pieces).ToList();
        Assert.HasCount(2, asked, "the passage, then the stretch put right");
        Assert.IsTrue(asked[^1] is [var stretch] && stretch.StartsWith("Gamma", StringComparison.Ordinal) && stretch.TrimEnd().EndsWith("five.", StringComparison.Ordinal),
                      "only the sentences round the doubled mark asked for again");
    }

    [TestMethod]
    public async Task Chapters_AreTranslatedSideBySide_EachPassageLedIntoByTheOneBeforeItInItsOwnChapter()
    {
        var model = ScriptedModel.Plain(context: 1000);
        var source = $"# Pets\n\n## Dogs\n\n{Company("Dogs")}\n\n{Company("Hounds")}\n\n## Cats\n\n{Company("Cats")}\n";

        var translated = await Translate(model, new MemoryNotes(), source: source);

        foreach (var name in new[] { "Dogs", "Hounds", "Cats" }) StringAssert.Contains(translated, Company(name).ToUpperInvariant());
        var asked = model.Of(ScriptedModel.Translation);
        Assert.IsFalse(asked.Any(question => question.Contains("Dogs are") && question.Contains("Cats are")), "nothing asked across two chapters");
        Assert.IsTrue(asked.Single(question => ScriptedModel.Pieces(question).Any(piece => piece.StartsWith("Hounds are", StringComparison.Ordinal)))
                           .Contains("The passage before"), "a passage is led into by the one before it in its chapter");
        Assert.IsFalse(asked.Single(question => ScriptedModel.Pieces(question).Any(piece => piece.StartsWith("Cats are", StringComparison.Ordinal)))
                            .Contains("The passage before"), "a chapter starts afresh");
    }

    /// <summary>A document of a paragraph for each of <paramref name="names"/> under one title — one chapter, read straight through.</summary>
    private static string Paragraphs(params string[] names) => "# Pets\n\n" + string.Concat(names.Select(name => $"{Company(name)}\n\n"));

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
    public async Task APassageAnsweredInHalves_IsReadBackWhole_WhenTranslatingAgain()
    {
        var notes = new MemoryNotes();
        ScriptedModel Halving() => new((_, asked) => ScriptedModel.Pieces(asked) is { Count: > 1 }
            ? new(string.Empty, DocumentReplyEnd.LengthLimit)
            : new(ScriptedModel.Translated(asked, ScriptedModel.Shout), DocumentReplyEnd.Finished));

        var first = await Translate(Halving(), notes);

        var again = Halving();
        Assert.AreEqual(first, await Translate(again, notes));
        Assert.IsEmpty(again.Asked, "the whole unit kept as its halves came to, so nothing is asked again — not even the unit that ran out of room");
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
    public async Task TheGlossaryIsReadFromTheWords_SoNoFormulaIsEverAnEntry()
    {
        var model = new ScriptedModel((instructions, _) => new(
            instructions.StartsWith(ScriptedModel.Glossary, StringComparison.Ordinal) ? "- $f$\n- Funktion"
          : instructions.StartsWith(ScriptedModel.GlossaryTranslation, StringComparison.Ordinal) ? "Funktion => function\n$f$ => $f$"
          : string.Empty, DocumentReplyEnd.Finished));

        var glossary = await new DocumentGlossaryPass(model, new MemoryNotes())
            .GatherAsync(DocumentReading.Of("Sei $f$ eine Funktion. Dann ist $f$ stetig, wie jede Funktion hier.\n"), Gist, "English", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { ("Funktion", "function") }, glossary.Entries.ToArray(), "a formula is never translated, so never given one way");
        Assert.IsFalse(model.Of(ScriptedModel.Glossary).Any(asked => asked.Contains('$')), "and the model was never shown one to list");
    }

    [TestMethod]
    public async Task AnAbbreviationIsAskedAboutWithTheSentenceThatBringsItIn()
    {
        // Its first use is in the contents, which says nothing of what it stands for; the sentence that writes it in brackets does.
        var model = new ScriptedModel((instructions, _) => new(
            instructions.StartsWith(ScriptedModel.Glossary, StringComparison.Ordinal) ? "- GDG"
          : instructions.StartsWith(ScriptedModel.GlossaryTranslation, StringComparison.Ordinal) ? "GDG => ODE"
          : string.Empty, DocumentReplyEnd.Finished));

        var glossary = await new DocumentGlossaryPass(model, new MemoryNotes()).GatherAsync(
            DocumentReading.Of("# Inhalt\n\n7.2 Lineare GDG\n\nGrob gesagt ist eine gewöhnliche Differentialgleichung (GDG) eine Gleichung\nfür eine Funktion. Jede GDG hat Lösungen.\n"),
            Gist, "English", CancellationToken.None);

        var asked = model.Of(ScriptedModel.GlossaryTranslation).Single();
        StringAssert.Contains(asked, "GDG\n  used in: Grob gesagt ist eine gewöhnliche Differentialgleichung (GDG) eine Gleichung für eine Funktion.",
            $"handed over beside what it stands for, its lines run together: {asked}");
        CollectionAssert.AreEqual(new[] { ("GDG", "ODE") }, glossary.Entries.ToArray());
    }

    [TestMethod]
    public async Task ManyTermsAreAskedAboutAsManyAtATimeAsFit_AndEveryOneIsGiven()
    {
        string[] trees = ["Ahorn", "Birke", "Buche", "Eiche", "Erle", "Esche", "Fichte", "Kiefer", "Linde", "Pappel", "Tanne", "Ulme"];
        var document = string.Concat(trees.Select(tree =>
            $"Die {tree} steht am Weg, und wer die {tree} sieht, erkennt sie an Rinde, Blatt und Wuchs, so wie es jedes Bestimmungsbuch für heimische Bäume lehrt.\n\n"));

        var model = new ScriptedModel((instructions, _) => new(
            instructions.StartsWith(ScriptedModel.Glossary, StringComparison.Ordinal) ? string.Join('\n', trees)
          : instructions.StartsWith(ScriptedModel.GlossaryTranslation, StringComparison.Ordinal) ? string.Join('\n', trees.Select(tree => $"{tree} => {tree}-tree"))
          : string.Empty, DocumentReplyEnd.Finished), context: 1024);

        var glossary = await new DocumentGlossaryPass(model, new MemoryNotes()).GatherAsync(DocumentReading.Of(document), Gist, "English", CancellationToken.None);

        var asks = model.Of(ScriptedModel.GlossaryTranslation);
        Assert.IsTrue(asks.Count > 1, $"more than one ask fits: {asks.Count}");
        CollectionAssert.AreEquivalent(trees.Select(tree => (tree, $"{tree}-tree")).ToArray(), glossary.Entries.ToArray(), "and each term given once");
        Assert.IsTrue(trees.All(tree => asks.Count(asked => asked.Contains($"{tree}\n  used in: Die {tree} steht am Weg")) == 1), "each asked about once, with its sentence");
    }

    [TestMethod]
    public void AnEntryIsUsedWhereItsTermIsAWord_NotWhereItsLettersAreInsideALongerOne()
    {
        var glossary = new DocumentGlossary([("Bild", "picture")]);

        Assert.AreEqual(0, glossary.In("Siehe die Abbildung 3.").Count, "a figure is not a picture");
        Assert.AreEqual(1, glossary.In("Das Bild zeigt es.").Count);
        Assert.AreEqual(1, glossary.In("BILD: siehe oben").Count, "in any case");
        Assert.AreEqual(1, glossary.In("(Bild)").Count, "beside what is not a letter");
    }

    [TestMethod]
    public void AndInAScriptThatRunsItsWordsTogether_WhereverItIsWritten()
    {
        // Chinese puts no space between words, so a term inside a sentence is still a word of it.
        Assert.AreEqual(1, new DocumentGlossary([("函数", "function")]).In("这个函数是连续的").Count);
        Assert.AreEqual(1, new DocumentGlossary([("ฟังก์ชัน", "function")]).In("นี่คือฟังก์ชันต่อเนื่อง").Count, "and Thai");
    }

    [TestMethod]
    public async Task ATermFoundOnlyInsideLongerWords_IsNoTerm_AndBothAsksWantTheMiddleOfASentence()
    {
        // "and" is inside behandeln and andere, never a word of its own; Bild is a word, twice, as well as inside Abbildung.
        var model = new ScriptedModel((instructions, _) => new(
            instructions.StartsWith(ScriptedModel.Glossary, StringComparison.Ordinal) ? "- And\n- Bild"
          : instructions.StartsWith(ScriptedModel.GlossaryTranslation, StringComparison.Ordinal) ? "And => And\nBild => picture"
          : string.Empty, DocumentReplyEnd.Finished));

        var glossary = await new DocumentGlossaryPass(model, new MemoryNotes()).GatherAsync(
            DocumentReading.Of("Wir behandeln andere Fälle. Das Bild zeigt die Abbildung. Ein Bild ist hier.\n"), Gist, "English", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { ("Bild", "picture") }, glossary.Entries.ToArray());
        Assert.IsTrue(model.Asked.Where(question => question.Instructions.StartsWith("You", StringComparison.Ordinal))
                                 .All(question => question.Instructions.Contains("middle of a sentence", StringComparison.Ordinal)),
            "terms listed and translations given as written mid-sentence, not with a capital the start of one gave them");
    }

    [TestMethod]
    public async Task ATermTheModelWrapsInATagOfItsOwn_IsAskedForAgain_AndNeverWrittenIntoTheDocument()
    {
        var tagged = true;
        var model = new ScriptedModel((instructions, asked) => new(
            instructions.StartsWith(ScriptedModel.Translation, StringComparison.Ordinal)
                ? ScriptedModel.Translated(asked, piece =>
                {
                    var said = ScriptedModel.Shout(piece);
                    if (!tagged) return said;

                    tagged = false;
                    return said.Replace("GDG", "<GDG>GDG</GDG>", StringComparison.Ordinal);
                })
                : ScriptedModel.Answer(instructions, asked), DocumentReplyEnd.Finished));

        var translated = await Translate(model, new MemoryNotes(), source: "Eine GDG ist eine Gleichung.\n");

        Assert.AreEqual("EINE GDG IST EINE GLEICHUNG.", translated.Trim(), "the tag it made up went back to it, and only its words came out");
        Assert.IsTrue(model.Of(ScriptedModel.Translation).Count > 1, "asked again");
    }

    // ── Answers taken piece by piece ─────────────────────────────────────────

    private const string ThreeParagraphs = "Dogs bark.\n\nCats purr.\n\nBirds sing.\n";

    [TestMethod]
    public async Task ATranslationWrittenOnTheSameLineAsItsNumber_IsTakenAsIt()
    {
        // How a model as often writes it: "@@ 1 DOGS BARK." rather than the number on a line of its own.
        var model = new ScriptedModel((instructions, asked) => new(
            instructions.StartsWith(ScriptedModel.Translation, StringComparison.Ordinal)
                ? string.Concat(ScriptedModel.Pieces(asked).Select((piece, at) => $"@@ {at + 1} {ScriptedModel.Shout(piece)}\n"))
                : ScriptedModel.Answer(instructions, asked), DocumentReplyEnd.Finished));

        var translated = await Translate(model, new MemoryNotes(), source: ThreeParagraphs);

        Assert.AreEqual("DOGS BARK.\n\nCATS PURR.\n\nBIRDS SING.", translated.Trim());
        Assert.AreEqual(1, model.Of(ScriptedModel.Translation).Count, "asked once, and taken");
    }

    [TestMethod]
    public async Task APieceLeftOutOfAnAnswer_IsAskedForAgainOnItsOwn_TheRestKept()
    {
        var asks = 0;
        var model = new ScriptedModel((instructions, asked) => new(
            instructions.StartsWith(ScriptedModel.Translation, StringComparison.Ordinal)
                ? asks++ == 0
                    ? Answered(ScriptedModel.Pieces(asked), skip: 1)
                    : ScriptedModel.Translated(asked, ScriptedModel.Shout)
                : ScriptedModel.Answer(instructions, asked), DocumentReplyEnd.Finished));

        var translated = await Translate(model, new MemoryNotes(), source: ThreeParagraphs);

        Assert.AreEqual("DOGS BARK.\n\nCATS PURR.\n\nBIRDS SING.", translated.Trim());

        var asked = model.Of(ScriptedModel.Translation);
        Assert.AreEqual(2, asked.Count);
        CollectionAssert.AreEqual(new[] { "Cats purr." }, ScriptedModel.Pieces(asked[1]), "only the piece it left out, asked for again");
    }

    [TestMethod]
    public async Task APieceAnsweredTwice_IsAnsweredNeither_AndAskedForAgain()
    {
        var asks = 0;
        var model = new ScriptedModel((instructions, asked) => new(
            instructions.StartsWith(ScriptedModel.Translation, StringComparison.Ordinal)
                ? asks++ == 0
                    ? ScriptedModel.Translated(asked, ScriptedModel.Shout) + "@@ 1\nSOMETHING ELSE\n"
                    : ScriptedModel.Translated(asked, ScriptedModel.Shout)
                : ScriptedModel.Answer(instructions, asked), DocumentReplyEnd.Finished));

        var translated = await Translate(model, new MemoryNotes(), source: ThreeParagraphs);

        StringAssert.StartsWith(translated, "DOGS BARK.", "which of the two was meant is not guessed");
        Assert.IsFalse(translated.Contains("SOMETHING ELSE", StringComparison.Ordinal));
        CollectionAssert.AreEqual(new[] { "Dogs bark." }, ScriptedModel.Pieces(model.Of(ScriptedModel.Translation)[1]));
    }

    [TestMethod]
    public async Task APieceGivenAsWrittenBeforeItsTranslation_IsAskedForAgain_AndNeverWrittenIntoTheDocument()
    {
        // A piece with no marks to be doubled, quoted on its number's line with its translation under it: the shape of the
        // passage before as the model is shown it, its text and then its translation.
        var asks = 0;
        var model = new ScriptedModel((instructions, asked) => new(
            instructions.StartsWith(ScriptedModel.Translation, StringComparison.Ordinal)
                ? asks++ == 0
                    ? "@@ 1\nDOGS BARK.\n\n@@ 2 Cats purr.\nCATS PURR.\n\n@@ 3\nBIRDS SING."
                    : ScriptedModel.Translated(asked, ScriptedModel.Shout)
                : ScriptedModel.Answer(instructions, asked), DocumentReplyEnd.Finished));

        var translated = await Translate(model, new MemoryNotes(), source: ThreeParagraphs);

        Assert.AreEqual("DOGS BARK.\n\nCATS PURR.\n\nBIRDS SING.", translated.Trim());

        var asked = model.Of(ScriptedModel.Translation);
        Assert.AreEqual(2, asked.Count);
        StringAssert.Contains(asked[0], "the translation alone, never the text it translates", "the first ask says so");
        CollectionAssert.AreEqual(new[] { "Cats purr." }, ScriptedModel.Pieces(asked[1]), "only the piece given as written, asked for again");
        StringAssert.Contains(asked[1], "give the translation alone", "saying what was wrong with it");
    }

    [TestMethod]
    public async Task APieceKeptFromAnEarlierRun_AsWrittenBeforeItsTranslation_IsAskedForAgain()
    {
        var notes = new MemoryNotes();
        await Translate(new ScriptedModel((_, asked) => new(ScriptedModel.Translated(asked, ScriptedModel.Shout), DocumentReplyEnd.Finished)), notes,
                        source: ThreeParagraphs);

        foreach (var (place, kept) in notes.Kept.Where(note => note.Value.Contains("CATS PURR.", StringComparison.Ordinal)).ToList())
            notes.Kept[place] = kept.Replace("CATS PURR.", "Cats purr.\nCATS PURR.", StringComparison.Ordinal);

        var again = new ScriptedModel((_, asked) => new(ScriptedModel.Translated(asked, ScriptedModel.Shout), DocumentReplyEnd.Finished));
        var translated = await Translate(again, notes, source: ThreeParagraphs);

        Assert.AreEqual("DOGS BARK.\n\nCATS PURR.\n\nBIRDS SING.", translated.Trim());
        Assert.IsTrue(again.Of(ScriptedModel.Translation).Any(asked => ScriptedModel.Pieces(asked).Contains("Cats purr.")),
                      "what was kept with the German still in it is asked for again, not written in");
    }

    /// <summary>An answer to every piece but the one at <paramref name="skip"/>, shouted, numbered as asked.</summary>
    private static string Answered(IReadOnlyList<string> pieces, int skip) =>
        string.Concat(pieces.Select((piece, at) => at == skip ? string.Empty : $"@@ {at + 1}\n{ScriptedModel.Shout(piece)}\n\n"));

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
    public async Task WhatAModelAnsweredOfAPassage_IsNeverAskedForAgain()
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
        var asks = model.Of(ScriptedModel.Translation).Select(ScriptedModel.Pieces).ToList();
        var leftOut = asks.Where(pieces => pieces.Count > 2).Select(pieces => pieces[^1]).ToHashSet();

        Assert.AreEqual(0, translation.Untranslated.Count, "everything translated");
        foreach (var again in asks.SelectMany(pieces => pieces).GroupBy(piece => piece).Where(piece => piece.Count() > 1))
        {
            Assert.IsTrue(leftOut.Contains(again.Key), $"only what was left out is asked for again, not '{again.Key}'");
            Assert.AreEqual(2, again.Count(), "and that once more");
        }
    }
}
