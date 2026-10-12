using Nexaflow.Core.Services.Voice;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.Voice;

/// <summary>A reply cut into what is said, as it is written.</summary>
[TestClass]
[CoversNode("voice-session")]
public class ReplySegmenterTests
{
    private static string[] Said(IEnumerable<SpokenPart> parts) => [.. parts.Select(p => p switch
    {
        SpokenSegment s => s.Text,
        ShownBlock b    => "[shown] " + b.Reference.Text,
        _               => "?",
    })];

    [TestMethod]
    public void TheFirstPiece_EndsAtAClause_TheRestAtSentences_AsTheDraftGrows()
    {
        var segmenter = new ReplySegmenter("en");

        CollectionAssert.AreEqual(Array.Empty<string>(), Said(segmenter.Update("A process is an independent")));
        CollectionAssert.AreEqual(new[] { "A process is an independent program," },
            Said(segmenter.Update("A process is an independent program, while a thread")));
        CollectionAssert.AreEqual(new[] { "while a thread shares its memory." },
            Said(segmenter.Update("A process is an independent program, while a thread shares its memory. Think of, say")));
        CollectionAssert.AreEqual(new[] { "Think of, say, a tab." },
            Said(segmenter.Finish("A process is an independent program, while a thread shares its memory. Think of, say, a tab.")));
    }

    [TestMethod]
    public void AShortOpening_WaitsForItsSentence_RatherThanCuttingAtAnEarlyComma()
        => CollectionAssert.AreEqual(new[] { "Yes, it is." }, Said(new ReplySegmenter("en").Update("Yes, it is. And")));

    [TestMethod]
    public void AbbreviationsAndTitles_DoNotEndASentence()
        => CollectionAssert.AreEqual(new[] { "Ask Dr. Smith about it, e.g. tomorrow at 3.30 in room 2." },
            Said(new ReplySegmenter("en").Finish("Ask Dr. Smith about it, e.g. tomorrow at 3.30 in room 2.")));

    [TestMethod]
    public void ADiagram_IsShown_AndOnlyReferredTo_OnceItsFenceCloses()
    {
        var segmenter = new ReplySegmenter("en");
        const string start = "Here is how it flows.\n```mermaid\nflowchart LR\n  a --> b\n";

        CollectionAssert.AreEqual(new[] { "Here is how it flows." }, Said(segmenter.Update(start)));
        var parts = segmenter.Update(start + "```\nThat is all.");
        CollectionAssert.AreEqual(new[] { "[shown] Here's the diagram." }, Said(parts));
        StringAssert.StartsWith(((ShownBlock)parts[0]).Markdown, "```mermaid");
        CollectionAssert.AreEqual(new[] { "That is all." }, Said(segmenter.Finish(start + "```\nThat is all.")));
    }

    [TestMethod]
    public void ATable_IsShown_InTheReplysLanguage()
    {
        var parts = new ReplySegmenter("fr").Finish("Voici les chiffres de la semaine.\n| jour | ventes |\n|---|---|\n| lundi | 3 |\n");

        CollectionAssert.AreEqual(new[] { "Voici les chiffres de la semaine.", "[shown] Voici le tableau." }, Said(parts));
    }

    [TestMethod]
    public void Formatting_IsNotRead()
        => CollectionAssert.AreEqual(new[] { "Open the settings file and set debug to true." },
            Said(new ReplySegmenter("en").Finish("Open the **settings** file and set `debug` to [true](https://x.test) 🎉.")));

    [TestMethod]
    public void EachSegment_CarriesTheLanguageItIsWrittenIn()
    {
        var parts = new ReplySegmenter("en").Finish("Here is the French. Je ne sais pas ce que vous voulez dire. And back.").Cast<SpokenSegment>().ToList();

        CollectionAssert.AreEqual(new[] { "en", "fr", "en" }, parts.Select(p => p.Language).ToArray());
    }

    [TestMethod]
    public void ANewStepsDraft_StartsAfresh()
    {
        var segmenter = new ReplySegmenter("en");
        segmenter.Update("Let me look at the logs. ");
        segmenter.Restart();

        CollectionAssert.AreEqual(new[] { "The build failed on a missing file." }, Said(segmenter.Finish("The build failed on a missing file.")));
    }
}
