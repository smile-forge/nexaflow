using Nexaflow.Providers.Common;
using Nexaflow.Providers.OpenAI;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// OpenAI transcription without a connection: which models it offers, and what a session's server updates add up to.
/// </summary>
[TestClass]
public class OpenAISpeechToTextTests
{
    [TestMethod]
    [DataRow("gpt-4o-transcribe", true)]
    [DataRow("gpt-4o-mini-transcribe", true)]
    [DataRow("whisper-1", true)]
    [DataRow("gpt-4o", false)]
    [DataRow("gpt-4o-mini-tts", false)]
    public void OnlyTranscriptionModels_AreOffered(string model, bool offered)
        => Assert.AreEqual(offered, OpenAISpeechToTextProvider.IsSpeechToText(model));

    [TestMethod]
    public void AnUtterance_GrowsAsPartials_ThenArrivesFinal()
    {
        var tracker = new TranscriptionTracker();

        tracker.Committed("i1");
        var first  = tracker.Delta("i1", "Open the ");
        var second = tracker.Delta("i1", "build log");
        var final  = tracker.Completed("i1", " Open the build log. ");

        Assert.AreEqual(new TranscriptPartial("Open the"), first.Single());
        Assert.AreEqual(new TranscriptPartial("Open the build log"), second.Single(), "a partial is the utterance so far, not the change");
        Assert.AreEqual(new TranscriptFinal("Open the build log."), final.Single());
    }

    [TestMethod]
    public void ASession_IsFinished_OnlyWhenEveryUtteranceItTookHasItsText()
    {
        var tracker = new TranscriptionTracker();
        tracker.Committed("i1");
        tracker.CompletionRequested();
        Assert.IsFalse(tracker.IsFinished, "the final commit has not been answered");

        tracker.Committed("i2");
        Assert.IsFalse(tracker.IsFinished, "two utterances still owe their text");

        tracker.Completed("i1", "one");
        Assert.IsFalse(tracker.IsFinished);

        tracker.Completed("i2", "two");
        Assert.IsTrue(tracker.IsFinished);
    }

    [TestMethod]
    public void AFinalCommitWithNothingLeft_EndsTheSessionOnceTheRestIsIn()
    {
        var tracker = new TranscriptionTracker();
        tracker.Committed("i1");               // the server found the end of the only utterance itself
        tracker.CompletionRequested();
        tracker.NothingToCommit();             // so the closing commit had nothing to take

        Assert.IsFalse(tracker.IsFinished);
        tracker.Completed("i1", "done");
        Assert.IsTrue(tracker.IsFinished);
    }

    [TestMethod]
    public void AnEmptyTranscript_IsNoUtterance()
        => Assert.AreEqual(0, new TranscriptionTracker().Completed("i1", "  ").Count);
}
