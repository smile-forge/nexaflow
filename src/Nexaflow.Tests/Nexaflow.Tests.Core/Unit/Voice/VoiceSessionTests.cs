using System.Text.Json.Nodes;
using Nexaflow.Core.Services.Voice;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Tests.Core.Unit.Voice;

/// <summary>
/// A spoken conversation, end to end with the models and devices stood in for: turns answered aloud, interruptions
/// that keep only what was heard, acknowledgements that are not turns, actions announced and done unless stopped.
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
        public readonly FakeConfirmations Confirmations = new();
        public readonly FakeAgent Agent;
        public readonly VoiceSession Session;
    public readonly List<string> Blocks = [];
    public readonly List<string> Said = [];
        public readonly List<string> Heard = [];
        public readonly List<string> Taken = [];
        public string? EndedBecause = "still going";

        public Rig(Func<string, IReadOnlyList<IClientTool>, IAIResponseHandler, CancellationToken, Task> script)
        {
            Agent = new FakeAgent(script);
            Session = new VoiceSession(new VoiceSessionParts(Ears, Voice, Microphone, Speaker, Agent, Shown, Confirmations, Time), "en");
    Session.Shown += (_, md) => { lock (Blocks) Blocks.Add(md); };
    Session.Said  += (_, text) => { lock (Said) Said.Add(text); };
            Session.Heard += (_, text) => { lock (Heard) Heard.Add(text); };
            Session.TurnTaken += (_, text) => { lock (Taken) Taken.Add(text); };
            Session.Ended += (_, why) => EndedBecause = why;
        }

        public async Task<Rig> StartAsync()
        {
            await Session.StartAsync();
            await Until(() => Session.State == VoiceState.Listening);
            return this;
        }

        /// <summary>Plays whatever is queued until the turn is over — the voice may still be queueing when it is called.</summary>
        public Task PlayOutAsync() => Until(() =>
        {
            Speaker.Play();
            return Session.State == VoiceState.Listening;
        }, "the turn to be heard out");

        /// <summary>Plays the announcement until the window for stopping it is open.</summary>
        public Task HearAnnouncementAsync() => Until(() =>
        {
            Speaker.Play();
            return Time.Waiting > 0;
        }, "the stop window to open");

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

    // The fixed lines, read as the app reads them: this suite has no string table, so they come back as their keys.
    private static string Announcement => Str.Format("Shell.Voice.AnnounceFormat", "delete file test.zip");
    private static string Stopped => Str.Format("Shell.Voice.AbortStoppedFormat", "delete", "The file is unchanged.");
    private static string NotUnderstood => Str.Format("Shell.Voice.AbortUnclearFormat", "delete");

    private static readonly ToolCall DeleteTestZip = new("delete_file", new JsonObject { ["path"] = @"C:\temp\test.zip" },
        Announcement: new ToolCallAnnouncement("delete", "file test.zip", "File deleted.", "The file is unchanged."));

    /// <summary>The agent loop asked for one action that needs approval: the action put up, run if it goes ahead and its
    /// result reported — and after it the model would say more, were the turn not already over.</summary>
    private static Func<string, IReadOnlyList<IClientTool>, IAIResponseHandler, CancellationToken, Task> Delete(Action<bool> answered)
        => async (_, _, handler, ct) =>
        {
            bool proceed = await handler.RequestToolBatchApprovalAsync("Deleting it now.", [DeleteTestZip], ct);
            answered(proceed);
            if (proceed) handler.OnToolBatchFinished([new ToolRunResult(DeleteTestZip.Tool, ToolResult.Ok("Deleted test.zip."))]);
            ct.ThrowIfCancellationRequested();
            handler.ShowFinal("I have deleted test.zip for you.");
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
        await Until(() => rig.Speaker.IsPaused && rig.Time.Waiting == 1, "the pause, and the wait for words");
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
    public async Task AnAction_IsAnnounced_AndDone_WhenNothingIsSaidForThreeSecondsAfterItIsHeard()
    {
        bool? proceeded = null;
        await using var rig = await new Rig(Delete(p => proceeded = p)).StartAsync();

        rig.Ears.Hear(new TranscriptFinal("Delete the selected file."));
        await Until(() => rig.Confirmations.Showing, "the announcement");
        CollectionAssert.AreEqual(new[] { Announcement }, rig.Voice.Texts, "fixed words, not the model's");
        var shown = rig.Confirmations.Shown.Single();
        Assert.AreEqual((Announcement, "Delete", Str.Get("Shell.Voice.Stop")), (shown.Text, shown.ProceedLabel, shown.StopLabel));

        rig.Time.Advance(VoiceReply.StopWindow);
        await Task.Delay(50);
        Assert.IsNull(proceeded, "the window opens once the announcement has been heard, not when it is queued");

        await rig.HearAnnouncementAsync();
        Assert.IsNull(proceeded);
        rig.Time.Advance(VoiceReply.StopWindow);
        await Until(() => rig.Voice.Texts.Length == 2 && proceeded is not null, "the outcome");

        Assert.IsTrue(proceeded);
        Assert.AreEqual("File deleted.", rig.Voice.Texts[1], "said in fixed words, and the model's own account is not");
        Assert.AreEqual(1, rig.Confirmations.Withdrawn, "Stop is taken away once it is too late");
        await rig.PlayOutAsync();
        CollectionAssert.AreEqual(new[] { "Delete the selected file.", Announcement + " File deleted." },
                                  rig.Session.History.Select(m => m.Text).ToArray());
        CollectionAssert.AreEqual(rig.Voice.Texts, rig.Said.ToArray(), "the bar shows exactly what is read aloud");
    }

    [TestMethod]
    public async Task AnAnnouncedAction_GoesAheadAtOnce_OnAYes()
    {
        bool? proceeded = null;
        await using var rig = await new Rig(Delete(p => proceeded = p)).StartAsync();

        rig.Ears.Hear(new TranscriptFinal("Delete the selected file."));
        await Until(() => rig.Confirmations.Showing, "the announcement");
        rig.Ears.Hear(new TranscriptFinal("Yes, go ahead."));
        await Until(() => rig.Voice.Texts.Length == 2 && proceeded is not null, "the outcome");

        Assert.IsTrue(proceeded);
        Assert.AreEqual("File deleted.", rig.Voice.Texts[1]);
        Assert.AreEqual(1, rig.Agent.Count, "an answer is not a new turn");
    }

    [TestMethod]
    public async Task Stop_SaidOverTheAnnouncement_AbortsIt_AndSaysNothingChanged()
    {
        bool? proceeded = null;
        await using var rig = await new Rig(Delete(p => proceeded = p)).StartAsync();

        rig.Ears.Hear(new TranscriptFinal("Delete the selected file."));
        await Until(() => rig.Confirmations.Showing && rig.Speaker.QueuedSamples > 0, "the announcement");
        rig.Speaker.Play(FakeSpeaker.SamplesPerChar * 4);
        rig.Ears.Hear(new SpeechActivity(true));
        await Until(() => rig.Speaker.IsPaused && rig.Time.Waiting == 1, "the announcement to pause, and the wait for words");
        rig.Ears.Hear(new TranscriptPartial("stop"));
        await Until(() => rig.Time.Waiting == 0, "the words to decide it");
        rig.Time.Advance(VoiceSession.BargeInDecision);
        await Task.Delay(50);
        Assert.IsTrue(rig.Speaker.IsPaused, "words over the announcement are its answer: it waits for them, it does not carry on");
        rig.Ears.Hear(new TranscriptFinal("Stop."));
        await Until(() => rig.Voice.Texts.Length == 2 && proceeded is not null, "the abort");

        Assert.IsFalse(proceeded);
        Assert.AreEqual(Stopped, rig.Voice.Texts[1]);
        Assert.AreEqual(1, rig.Speaker.Stops, "the rest of the announcement is not said");
        Assert.AreEqual(1, rig.Agent.Count, "the answer is not a new turn");
        Assert.AreEqual(1, rig.Confirmations.Withdrawn);
        await rig.PlayOutAsync();
        Assert.AreEqual(Announcement + " " + Stopped, rig.Session.History[^1].Text);
    }

    [TestMethod]
    public async Task AnythingButAYes_AbortsTheAnnouncedAction_ForWantOfOne()
    {
        bool? proceeded = null;
        await using var rig = await new Rig(Delete(p => proceeded = p)).StartAsync();

        rig.Ears.Hear(new TranscriptFinal("Delete the selected file."));
        await rig.HearAnnouncementAsync();
        rig.Ears.Hear(new TranscriptFinal("Hmm, the blue one maybe."));
        await Until(() => rig.Voice.Texts.Length == 2 && proceeded is not null, "the abort");

        Assert.IsFalse(proceeded);
        Assert.AreEqual(NotUnderstood, rig.Voice.Texts[1]);
        Assert.AreEqual(1, rig.Agent.Count);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task TheBarsButtons_AnswerTheAnnouncedAction(bool proceed)
    {
        bool? proceeded = null;
        await using var rig = await new Rig(Delete(p => proceeded = p)).StartAsync();

        rig.Ears.Hear(new TranscriptFinal("Delete the selected file."));
        await Until(() => rig.Confirmations.Showing && rig.Speaker.QueuedSamples > 0, "the announcement");
        rig.Confirmations.Press(proceed);
        await Until(() => rig.Voice.Texts.Length == 2 && proceeded is not null, "the outcome");

        Assert.AreEqual(proceed, proceeded);
        Assert.AreEqual(proceed ? "File deleted." : Stopped, rig.Voice.Texts[1]);
    Assert.AreEqual(0, rig.Confirmations.Withdrawn, "answered, not taken away");
        Assert.AreEqual(1, rig.Speaker.Stops, "a click answers it as words would: the rest of the announcement is not said");
    }

    [TestMethod]
    public async Task SendingASensitivePage_WaitsForAYes_NotForSilence()
    {
        bool? released = null;
        await using var rig = await new Rig(async (_, _, handler, ct) =>
        {
            released = await handler.RequestContextReleaseAsync(["Claude"], ct);
            handler.ShowFinal("It says hello.");
        }).StartAsync();

        rig.Ears.Hear(new TranscriptFinal("What does this page say?"));
        await Until(() => rig.Confirmations.Showing, "the question");
        await Until(() =>
        {
            rig.Speaker.Play();
            return rig.Speaker.PlayedSamples > 0 && rig.Speaker.PlayedSamples == rig.Speaker.QueuedSamples;
        }, "the question to be heard");
        rig.Time.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(50);
        Assert.IsNull(released, "silence is not consent");

        rig.Ears.Hear(new TranscriptFinal("Yes."));
        await Until(() => released is not null, "the answer");

        Assert.IsTrue(released);
        await Until(() => rig.Voice.Texts.Contains("It says hello."), "the reply");
    }

    [TestMethod]
    public async Task ATypedTurn_IsAnsweredAloud_WithoutBeingHeard()
    {
        await using var rig = await new Rig(Answer("It is three o'clock.")).StartAsync();

        rig.Session.Submit("  What time is it?  ");
        await Until(() => rig.Voice.Texts.Length == 1, "the reply");

        Assert.AreEqual("What time is it?", rig.Agent.Turns.Single().Text);
        Assert.AreEqual("It is three o'clock.", rig.Voice.Texts[0]);
        CollectionAssert.AreEqual(new[] { "What time is it?" }, rig.Taken, "taken as a turn");
        Assert.AreEqual(0, rig.Heard.Count, "typed, so not put back in the box as if it had been heard");
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
        Assert.AreNotEqual(VoiceState.Ended, rig.Session.State, $"not before it is said (ended: {rig.EndedBecause})");
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
