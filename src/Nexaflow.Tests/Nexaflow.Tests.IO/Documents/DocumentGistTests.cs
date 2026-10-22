using Nexaflow.IO.Documents.Processing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Documents;

/// <summary>
/// What a document is, said first and from what fits: a few passages from all through it, whatever its length, each read
/// with what was said so far — and kept, so it is not said again until what was read changes.
/// </summary>
[TestClass]
[CoversNode("documents-gist")]
public class DocumentGistTests
{
    [TestMethod]
    public void WhatAModelSays_IsReadHoweverItMarksItUp()
    {
        var gist = DocumentGist.Read("Here you go:\n**GENRE**: fiction\n- TOPIC: a whale\nAUDIENCE: adults\nREGISTER: formal\nLANGUAGE: English\n");

        Assert.IsNotNull(gist);
        Assert.AreEqual("fiction", gist.Genre);
        Assert.AreEqual("a whale", gist.Topic);
        Assert.AreEqual("not said", gist.Tone);
    }

    [TestMethod]
    public void TooLittleSaid_IsNoGist() =>
        Assert.IsNull(DocumentGist.Read("GENRE: fiction\nTOPIC: a whale\n"), "no language, and too few lines to work from");

    [TestMethod]
    public async Task ALongDocument_IsSaidFromAFewPassagesSpreadThroughIt_EachWithWhatWasSaidSoFar()
    {
        var book = string.Join("\n\n", Enumerable.Range(1, 60).Select(n => $"# Chapter {n}\n\n{new string('x', 3000)}."));
        var model = ScriptedModel.Plain(context: 4096);

        var gist = await new DocumentGistPass(model, new MemoryNotes()).SayAsync(DocumentReading.Of(book), CancellationToken.None);

        var asked = model.Of(ScriptedModel.Gist);
        Assert.AreEqual(DocumentGistPass.MostPassages, asked.Count, "a book is read in a bounded number of questions");
        Assert.IsTrue(asked.Skip(1).All(question => question.Contains("What you have said of it so far")), "each with what was said before it");
        Assert.IsTrue(asked.All(question => question.Length < 4096 * 3), "and each sized to what the model reads");
        Assert.AreEqual("pets", gist.Topic);
    }

    [TestMethod]
    public async Task SayingItAgain_AsksNothing()
    {
        var notes = new MemoryNotes();
        var reading = DocumentReading.Of("# Pets\n\nDogs bark.\n");

        await new DocumentGistPass(ScriptedModel.Plain(), notes).SayAsync(reading, CancellationToken.None);
        var again = ScriptedModel.Plain();
        await new DocumentGistPass(again, notes).SayAsync(reading, CancellationToken.None);

        Assert.AreEqual(0, again.Asked.Count);
    }

    [TestMethod]
    public async Task AModelThatNeverSaysItInTheLinesAskedFor_StopsTheJob()
    {
        var model = new ScriptedModel((_, _) => new DocumentReply("It is a nice document.", DocumentReplyEnd.Finished));

        await Assert.ThrowsExactlyAsync<DocumentWorkException>(() => new DocumentGistPass(model, new MemoryNotes()).SayAsync(DocumentReading.Of("Words.\n"), CancellationToken.None));
        Assert.AreEqual(2, model.Asked.Count, "asked once more, saying how to answer, before giving up");
    }
}
