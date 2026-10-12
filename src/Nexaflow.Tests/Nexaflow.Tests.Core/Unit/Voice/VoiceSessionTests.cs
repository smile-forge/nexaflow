using System.Text.Json.Nodes;
using Nexaflow.Core.Services.Voice;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.Voice;

/// <summary>
/// A spoken conversation, end to end with the models and devices stood in for: turns answered aloud, interruptions
/// that keep only what was heard, acknowledgements that are not turns, approvals answered by voice.
/// </summary>
[TestClass]
[CoversNode("voice-session")]
public class VoiceSessionTests
{
    private sealed class Rig : IAsyncDisposable
    {
        public readonly FakeListener Ears = new();
        public readonly FakeSpeaker Voice = new();
        public readonly FakeCapture Microphone = new();
        public readonly FakePlayback Speaker = new();
        public readonly ManualTime Time = new();
        public readonly ShownConversation Shown = new();
        public readonly FakeAgent Agent;
        public readonly VoiceSession Session;
        public readonly List<string> Blocks = [];
        public string? EndedBecause = "still going";

        public Rig(Func<string, IReadOnlyList<IClientTool>, IAIResponseHandler, CancellationToken, Task> script)
        {
            Agent = new FakeAgent(script);
            Session = new VoiceSession(new VoiceSessionParts(Ears, Voice, Microphone, Speaker, Agent, Shown, Time), "en");
            Session.Shown += (_, md) => { lock (Blocks) Blocks.Add(md); };
            Session.Ended += (_, why) => EndedBecause = why;
        }

        public async Task<Rig> StartAsync()
        {
            await Session.StartAsync();
            await Until(() => Session.State == VoiceState.Listening);
            return this;
        }

        public ValueTask DisposeAsync() => Session.DisposeAsync();
    }

    /// <summary>A reply written whole, as the agent writes a short answer.</summary>
    private static Func<string, IReadOnlyList<IClientTool>, IAIResponseHandler, CancellationToken, Task> Answer(string reply)
        => (_, _, handler, _) =>
        {
            handler.ShowReplyDraft(reply);
            handler.ShowFinal(reply);
            return Task.CompletedTask;
        };

    private static async Task Until(Func<bool> condition, string? what = null)
    {
        for (var deadline = DateTime.UtcNow.AddSeconds(5); DateTime.UtcNow < deadline; await Task.Delay(10))
            if (condition()) return;
        Assert.Fail($"timed out waiting for {what ?? "the condition"}");
    }

    [TestMethod]
    public async Task ATurn_IsAnsweredAloud_AndRemembered_OnceItHasBeenHeard()
    {
        await using var rig = await new Rig(Answer("It is three o'clock.")).StartAsync();
        Assert.AreEqual(SpeechSegmentation.Turns, rig.Ears.Options!.Segmentation, "a conversation listens for turns, not pauses");

        rig.Ears.Hear(new TranscriptFinal("What time is it?"));

        await Until(() => rig.Session.State == VoiceState.Speaking, "speech");
        CollectionAssert.AreEqual(new[] { "It is three o'clock." }, rig.Voice.Texts);
        rig.Speaker.Play();
        await Until(() => rig.Session.State == VoiceState.Listening, "the reply to finish");

        CollectionAssert.AreEqual(new[] { "What time is it?", "It is three o'clock." }, rig.Session.History.Select(m => m.Text).ToArray());
    }

    [TestMethod]
    public async Task WordsOverTheReply_StopIt_TheConversationKeepsWhatWasHeard_AndTheNextTurnWaitsForTheRestOfTheSentence()
    {
        await using var rig = await new Rig(Answer("First comes this sentence. Then comes a second one. And a third.")).StartAsync();
        rig.Ears.Hear(new TranscriptFinal("Tell me three things."));
        await Until(() => rig.Voice.Texts.Length == 3, "the reply to be synthesised");

        rig.Speaker.Play("First comes this sentence.".Length * FakeSpeaker.SamplesPerChar + 1);   // into the second sentence
        rig.Ears.Hear(new SpeechActivity(true));
        await Until(() => rig.Speaker.IsPaused, "the reply to pause");
        rig.Ears.Hear(new TranscriptPartial("wait stop"));
        await Until(() => rig.Speaker.Stops == 1, "the reply to stop");

        Assert.AreEqual("First comes this sentence. —", rig.Session.History[^1].Text, "what was heard, not what was written");

        rig.Ears.Hear(new TranscriptFinal("Wait, stop,"));
        rig.Ears.Hear(new SpeechActivity(true));
        rig.Ears.Hear(new TranscriptFinal("just give me one."));
        await Until(() => rig.Session.IsHoldingTurn && rig.Agent.Count == 1, "the turn to be held");
        rig.Time.Advance(VoiceSession.HoldAfterBargeIn);
        await Until(() => rig.Agent.Count == 2, "the next turn");

        Assert.AreEqual("Wait, stop, just give me one.", rig.Agent.Turns[1].Text);
        CollectionAssert.AreEqual(new[] { "Tell me three things.", "First comes this sentence. —" }, rig.Agent.Turns[1].History.Select(m => m.Text).ToArray());
    }

    [TestMethod]
    public async Task ASoundWithNoWordsOverTheReply_PausesIt_ThenItCarriesOn()
    {
        await using var rig = await new Rig(Answer("A long answer that takes a while to say.")).StartAsync();
        rig.Ears.Hear(new TranscriptFinal("Explain."));
        await Until(() => rig.Session.State == VoiceState.Speaking);

        rig.Ears.Hear(new SpeechActivity(true));
        await Until(() => rig.Speaker.IsPaused, "the pause");
        rig.Time.Advance(VoiceSession.BargeInDecision);
        await Until(() => !rig.Speaker.IsPaused, "the reply to carry on");

        Assert.AreEqual(0, rig.Speaker.Stops);
    }

    [TestMethod]
    public async Task AnAcknowledgementWhileItTalks_IsNotATurn()
    {
        await using var rig = await new Rig(Answer("Here is a fairly long explanation of it.")).StartAsync();
        rig.Ears.Hear(new TranscriptFinal("Explain."));
        await Until(() => rig.Session.State == VoiceState.Speaking);

        rig.Ears.Hear(new TranscriptFinal("Mm-hmm."));
        await Task.Delay(50);

        Assert.AreEqual(1, rig.Agent.Count);
        Assert.AreEqual(0, rig.Speaker.Stops);
    }

    [TestMethod]
    public async Task AnApproval_IsAskedAloud_AndAYesAnswersIt()
    {
        bool? approved = null;
        await using var rig = await new Rig(async (_, _, handler, ct) =>
        {
            handler.ShowReplyDraft("I'll delete the temporary file.");
            approved = await handler.RequestToolBatchApprovalAsync("I'll delete the temporary file.", [], ct);
            handler.ShowFinal("Done.");
        }).StartAsync();

        rig.Ears.Hear(new TranscriptFinal("Delete the temp file."));
        await Until(() => rig.Voice.Texts.Contains("Shall I go ahead?"), "the question");
        rig.Ears.Hear(new TranscriptFinal("Something else entirely."));
        await Task.Delay(50);
        Assert.IsNull(approved, "what is neither yes nor no does not answer");

        rig.Ears.Hear(new TranscriptFinal("Yes, go ahead."));
        await Until(() => approved is not null, "the answer");

        Assert.IsTrue(approved);
        Assert.AreEqual(1, rig.Shown.ApprovalsCancelled, "the question shown is put away");
        Assert.AreEqual(1, rig.Agent.Count, "an answer is not a new turn");
    }

    [TestMethod]
    public async Task TheAssistant_EndsTheConversation_AfterItsGoodbyeIsHeard()
    {
        await using var rig = await new Rig(async (_, tools, handler, ct) =>
        {
            await tools.Single(t => t.Name == "end_voice_session").InvokeAsync(new JsonObject(), ct);
            handler.ShowFinal("Goodbye!");
        }).StartAsync();

        rig.Ears.Hear(new TranscriptFinal("That's all, thanks."));
        await Until(() => rig.Voice.Texts.Contains("Goodbye!"));
        Assert.AreNotEqual(VoiceState.Ended, rig.Session.State, "not before it is said");
        rig.Speaker.Play();
        await Until(() => rig.Session.State == VoiceState.Ended, "the end");

        Assert.IsNull(rig.EndedBecause);
        Assert.IsTrue(rig.Microphone.Disposed);
    }

    [TestMethod]
    public async Task ADiagram_IsShown_AndOnlyMentionedAloud()
    {
        const string reply = "Here is how it flows.\n```mermaid\nflowchart LR\n  a --> b\n```\n";
        await using var rig = await new Rig(Answer(reply)).StartAsync();

        rig.Ears.Hear(new TranscriptFinal("Draw it."));
        await Until(() => rig.Voice.Texts.Length == 2);

        CollectionAssert.AreEqual(new[] { "Here is how it flows.", "Here's the diagram." }, rig.Voice.Texts);
        StringAssert.StartsWith(rig.Blocks.Single(), "```mermaid");
    }

    [TestMethod]
    public async Task MoreSaidBeforeTheReplyIsHeard_JoinsTheTurn_AndItIsAnsweredAgain()
    {
        var release = new TaskCompletionSource();
        await using var rig = await new Rig(async (text, _, handler, ct) =>
        {
            if (text == "Find the report") await release.Task.WaitAsync(ct);
            handler.ShowFinal("Found it.");
        }).StartAsync();

        rig.Ears.Hear(new TranscriptFinal("Find the report"));
        await Until(() => rig.Agent.Count == 1);
        rig.Ears.Hear(new TranscriptFinal("from last week."));
        await Until(() => rig.Session.IsHoldingTurn, "the turn to be held");
        rig.Time.Advance(VoiceSession.HoldAfterBargeIn);
        await Until(() => rig.Agent.Count == 2, "the turn to be taken again");

        Assert.AreEqual("Find the report from last week.", rig.Agent.Turns[1].Text);
        Assert.AreEqual(0, rig.Agent.Turns[1].History.Count, "nothing of the first attempt was heard");
    }
}
