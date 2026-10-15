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
    public readonly List<string> Voiced = [];
        public readonly List<string> Heard = [];
        public readonly List<string> Taken = [];
        public string? EndedBecause = "still going";

        public Rig(Func<string, IReadOnlyList<IClientTool>, IAIResponseHandler, CancellationToken, Task> script)
        {
            Agent = new FakeAgent(script);
            Session = new VoiceSession(new VoiceSessionParts(Ears, Voice, Microphone, Speaker, Agent, Shown, Confirmations, Time), "en");
    Session.Shown += (_, md) => { lock (Blocks) Blocks.Add(md); };
        Session.Voiced += (_, text) => { lock (Voiced) Voiced.Add(text); };
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

    private static string HeldFirst => Str.Format("Shell.Voice.HeldFirstFormat", "Deleting file test.zip");

    private static readonly ToolCall DeleteTestZip = new("delete_file", new JsonObject { ["path"] = @"C:\temp\test.zip" },
        Announcement: new ToolCallAnnouncement("delete", "file test.zip", "Deleting file test.zip", "File deleted.", "The file is unchanged."));

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

    /// <summary>The agent loop asked to delete the file and then to go on: the delete put up, run if it goes ahead, and
    /// then whatever the user added meanwhile taken up, and summed up in the final answer.</summary>
    private static Func<string, IReadOnlyList<IClientTool>, IAIResponseHandler, CancellationToken, Task> DeleteThenCarryOn(Action<bool> answered)
        => async (_, _, handler, ct) =>
        {
            bool proceed = await handler.RequestToolBatchApprovalAsync("Deleting it now.", [DeleteTestZip], ct);
            answered(proceed);
            if (proceed)
            {
                handler.OnToolBatchStarting([DeleteTestZip]);
                handler.OnToolBatchFinished([new ToolRunResult(DeleteTestZip.Tool, ToolResult.Ok("Deleted test.zip."))]);
            }
            ct.ThrowIfCancellationRequested();
            var added = handler.TakeInterjections();
            handler.ShowFinal($"{(proceed ? "Deleted it" : "Kept it")} and did {added.Count} more.");
        };

    /// <summary>Speech over what is playing, with words: the reply pauses, then the words decide it.</summary>
    private static async Task SpeakOverAsync(Rig rig, string partial)
    {
        rig.Ears.Hear(new SpeechActivity(true));
        await Until(() => rig.Speaker.IsPaused && rig.Time.Waiting == 1, "the pause, and the wait for words");
        rig.Ears.Hear(new TranscriptPartial(partial));
        await Until(() => rig.Time.Waiting == 0, "the words to decide it");
    }

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
    public async Task TheReply_IsShownWordByWord_AsItIsHeard_NotAheadOfTheVoice()
    {
        const string first = "First comes this sentence.";
        await using var rig = await new Rig(Answer(first + " Then comes a second one.")).StartAsync();
        rig.Ears.Hear(new TranscriptFinal("Tell me two things."));
        await Until(() => rig.Voice.Texts.Length == 2 && rig.Speaker.QueuedSamples > 0, "the reply to be queued");

        rig.Time.Advance(VoiceSession.VoicedEvery);
        await Task.Delay(50);
        Assert.AreEqual(0, rig.Voiced.Count, "nothing is shown before it is heard");

        rig.Speaker.Play(first.Length * FakeSpeaker.SamplesPerChar);
        await Until(() =>
        {
            rig.Time.Advance(VoiceSession.VoicedEvery);
            lock (rig.Voiced) return rig.Voiced.Count > 0 && rig.Voiced[^1] == first;
        }, "the first sentence, once heard");

        await rig.PlayOutAsync();
        Assert.AreEqual(first + " Then comes a second one.", rig.Voiced[^1], "and the rest once it has been");
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
        await Until(() => rig.Session.History.Count == 2, "the turn to be remembered");

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

    private static readonly LlmToolCall DeleteOld = new("c1", "delete_file", new JsonObject { ["name"] = "old" });

    private static IReadOnlyList<LlmMessage> RanADeletion(string asked, string reply) =>
    [
        new(LlmRole.User, asked),
        new(LlmRole.Assistant, "") { ToolCalls = [DeleteOld] },
        new(LlmRole.Tool, "Sent 'old' to the Recycle Bin.") { Answers = DeleteOld },
        new(LlmRole.Assistant, reply),
    ];

    [TestMethod]
    public async Task ATurnThatActed_IsRememberedAsItRan_WhatWasCalledAndWhatCameOfIt()
    {
        await using var rig = await new Rig(Answer("I deleted old.")).StartAsync();
        rig.Agent.Runs = asked => RanADeletion(asked, "I deleted old.");

        rig.Ears.Hear(new TranscriptFinal("Delete old."));
        await Until(() => rig.Session.State == VoiceState.Speaking, "speech");
        rig.Speaker.Play();
        await Until(() => rig.Session.State == VoiceState.Listening, "the reply to finish");

        CollectionAssert.AreEqual(new[] { LlmRole.User, LlmRole.Assistant, LlmRole.Tool, LlmRole.Assistant }, rig.Session.History.Select(m => m.Role).ToArray());
        Assert.AreEqual("c1", rig.Session.History[1].ToolCalls!.Single().Id);
        Assert.AreEqual(("c1", "Sent 'old' to the Recycle Bin."), (rig.Session.History[2].Answers?.Id, rig.Session.History[2].Text));
        Assert.AreEqual("I deleted old.", rig.Session.History[3].Text);
    }

    [TestMethod]
    public async Task ATurnThatActed_AndWasCutInto_KeepsWhatItDid_AndItsReplyAsFarAsItWasHeard()
    {
        await using var rig = await new Rig(Answer("I deleted old. It is in the Recycle Bin.")).StartAsync();
        rig.Agent.Runs = asked => RanADeletion(asked, "I deleted old. It is in the Recycle Bin.");
        rig.Ears.Hear(new TranscriptFinal("Delete old."));
        await Until(() => rig.Voice.Texts.Length == 2, "the reply to be synthesised");

        rig.Speaker.Play("I deleted old.".Length * FakeSpeaker.SamplesPerChar + 1);
        rig.Ears.Hear(new SpeechActivity(true));
        await Until(() => rig.Speaker.IsPaused, "the reply to pause");
        rig.Ears.Hear(new TranscriptPartial("thanks that's all"));
        await Until(() => rig.Speaker.Stops == 1, "the reply to stop");
        for (int i = 0; i < 500 && rig.Session.History.Count < 4; i++) await Task.Delay(10);
        Assert.AreEqual(4, rig.Session.History.Count, string.Join(" | ", rig.Session.History.Select(m => $"{m.Role}:{m.Text}")));

        Assert.AreEqual(LlmRole.Tool, rig.Session.History[2].Role, "what it did is kept");
        Assert.AreEqual("I deleted old. —", rig.Session.History[^1].Text, "and what of the reply was heard");
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
        Assert.AreEqual(string.Join(' ', rig.Voice.Texts), rig.Voiced[^1], "the bar shows exactly what is read aloud");
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
        Assert.AreEqual(1, rig.Speaker.Stops, "words over the announcement are its answer: it is cut at the first, not carried on");
        rig.Ears.Hear(new TranscriptFinal("Stop."));
        await Until(() => rig.Voice.Texts.Length == 2 && proceeded is not null, "the abort");

        Assert.IsFalse(proceeded);
        Assert.AreEqual(Stopped, rig.Voice.Texts[1]);
    Assert.AreEqual(1, rig.Speaker.Stops, "cut once: the rest of the announcement is not said");
        Assert.AreEqual(0, rig.Agent.Judged.Count, "a plain stop needs no judging");
        Assert.AreEqual(1, rig.Agent.Count, "the answer is not a new turn");
        Assert.AreEqual(1, rig.Confirmations.Withdrawn);
        await rig.PlayOutAsync();
        Assert.AreEqual(Announcement + " " + Stopped, rig.Session.History[^1].Text);
    }

    [TestMethod]
    public async Task AnInstructionOverAnAnnouncedAction_IsAddedToTheWork_AndTheActionIsHeld_UntilTheUserIsQuiet()
    {
        bool? proceeded = null;
        await using var rig = await new Rig(DeleteThenCarryOn(p => proceeded = p)).StartAsync();
        rig.Agent.Judge = _ => AnswerIntent.Instruction;

        rig.Ears.Hear(new TranscriptFinal("Delete the selected file."));
        await Until(() => rig.Confirmations.Showing && rig.Speaker.QueuedSamples > 0, "the announcement");
        rig.Speaker.Play(FakeSpeaker.SamplesPerChar * 4);
        await SpeakOverAsync(rig, "and rename");
        rig.Ears.Hear(new TranscriptFinal("And rename notes.txt to report.txt."));
        await Until(() => rig.Session.State == VoiceState.Working && rig.Confirmations.Shown.Count == 2, "the work to go quiet");

        var held = rig.Confirmations.Shown[^1];
        Assert.IsTrue(held.IsHeld, "shown held, in red");
        Assert.AreEqual((HeldFirst, Str.Get("Shell.Voice.Abort")), (held.Text, held.StopLabel));
        Assert.AreEqual(1, rig.Speaker.Stops, "the announcement stops as the user speaks over it");
        Assert.IsNull(proceeded, "not while the user is still talking");

        rig.Ears.Hear(new SpeechActivity(false));
        await Until(() => rig.Time.Waiting == 1, "the quiet to be timed");
        rig.Ears.Hear(new SpeechActivity(true));
        await Until(() => rig.Time.Waiting == 0, "speech to start the quiet over");
        rig.Ears.Hear(new SpeechActivity(false));
        await Until(() => rig.Time.Waiting == 1, "the quiet to be timed again");
        rig.Time.Advance(VoiceReply.StopWindow);
        await Until(() => proceeded is not null && rig.Voice.Texts.Length == 2, "the summary");

        Assert.IsTrue(proceeded);
        CollectionAssert.AreEqual(new[] { Announcement, "Deleted it and did 1 more." }, rig.Voice.Texts,
                                  "the done line is held: the model's summary is all that is said");
        Assert.AreEqual(1, rig.Agent.Count, "added to the work, not a turn of its own");
        await rig.PlayOutAsync();
        CollectionAssert.AreEqual(new[] { "Delete the selected file. And rename notes.txt to report.txt.", "Deleted it and did 1 more." },
                                  rig.Session.History.Select(m => m.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "Delete the selected file.", "And rename notes.txt to report.txt." }, rig.Taken);
    }

    [TestMethod]
    public async Task Abort_OnAHeldAction_SkipsIt_AndTheWorkGoesOn()
    {
        bool? proceeded = null;
        await using var rig = await new Rig(DeleteThenCarryOn(p => proceeded = p)).StartAsync();
        rig.Agent.Judge = _ => AnswerIntent.Instruction;

        rig.Ears.Hear(new TranscriptFinal("Delete the selected file."));
        await Until(() => rig.Confirmations.Showing && rig.Speaker.QueuedSamples > 0, "the announcement");
        rig.Speaker.Play(FakeSpeaker.SamplesPerChar * 4);
        await SpeakOverAsync(rig, "and rename");
        rig.Ears.Hear(new TranscriptFinal("And rename notes.txt to report.txt."));
        await Until(() => rig.Confirmations.Shown.Count == 2 && rig.Confirmations.Showing, "the held action");

        rig.Confirmations.Press(false);
        await Until(() => proceeded is not null && rig.Voice.Texts.Length == 2, "the summary");

        Assert.IsFalse(proceeded);
        CollectionAssert.AreEqual(new[] { Announcement, "Kept it and did 1 more." }, rig.Voice.Texts,
                                  "no abort line: the summary says what was not done");
    }

    [TestMethod]
    [DataRow(AnswerIntent.Unclear, false, "unclear")]
    [DataRow(AnswerIntent.Cancel, false, "stopped")]
    [DataRow(AnswerIntent.Proceed, true, "done")]
    public async Task WordsThatAreNeitherYesNorNo_AreJudged_AndTheJudgementDecides(AnswerIntent intent, bool proceeds, string line)
    {
        bool? proceeded = null;
        await using var rig = await new Rig(Delete(p => proceeded = p)).StartAsync();
        rig.Agent.Judge = _ => intent;

        rig.Ears.Hear(new TranscriptFinal("Delete the selected file."));
        await rig.HearAnnouncementAsync();
        rig.Ears.Hear(new TranscriptFinal("Hmm, the blue one maybe."));
        await Until(() => proceeded is not null && rig.Voice.Texts.Length == 2, "the outcome");

        Assert.AreEqual(proceeds, proceeded);
        Assert.AreEqual(line switch { "unclear" => NotUnderstood, "stopped" => Stopped, _ => "File deleted." }, rig.Voice.Texts[1]);
        CollectionAssert.AreEqual(new[] { "Hmm, the blue one maybe." }, rig.Agent.Judged, "only what the words alone cannot tell");
        Assert.AreEqual(1, rig.Agent.Count);
    }

    [TestMethod]
    public async Task WordsOverAReplyAtWork_AreAddedToIt_ItWorksQuietly_AndSumsUpAtTheEnd()
    {
        var release = new TaskCompletionSource();
        await using var rig = await new Rig(async (_, _, handler, ct) =>
        {
            handler.ShowReplyDraft("Looking through the folder now. ");
            await release.Task.WaitAsync(ct);
            var added = handler.TakeInterjections();
            handler.ShowFinal($"Found it and did {added.Count} more.");
        }).StartAsync();

        rig.Ears.Hear(new TranscriptFinal("Find the report."));
        await Until(() => rig.Speaker.QueuedSamples > 0, "the acknowledgement");
        rig.Speaker.Play(FakeSpeaker.SamplesPerChar * 10);
        await SpeakOverAsync(rig, "also open");
        Assert.AreEqual(1, rig.Speaker.Stops, "the acknowledgement is cut at the first word");
        rig.Ears.Hear(new TranscriptFinal("Also open it."));
        await Until(() => rig.Session.State == VoiceState.Working, "the work to go quiet");
        Assert.AreEqual(1, rig.Agent.Count, "the work goes on: added to, not started again");

        release.SetResult();
        await Until(() => rig.Voice.Texts.Contains("Found it and did 1 more."), "the summary");
        await rig.PlayOutAsync();
        Assert.AreEqual("Find the report. Also open it.", rig.Session.History[^2].Text);
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
    public async Task AnAnnouncementCutShort_IsStillRemembered_AsHeard_WhenTheNextTurnInterruptsWhatFollows()
    {
        bool? proceeded = null;
        await using var rig = await new Rig(Delete(p => proceeded = p)).StartAsync();

        rig.Ears.Hear(new TranscriptFinal("Delete the selected file."));
        await Until(() => rig.Confirmations.Showing && rig.Speaker.QueuedSamples > 0, "the announcement");
        rig.Speaker.Play(rig.Speaker.QueuedSamples / 2);
        rig.Confirmations.Press(false);
        await Until(() => proceeded is not null && rig.Voice.Texts.Length == 2 && rig.Speaker.QueuedSamples > rig.Speaker.PlayedSamples,
                    "the abort line to be queued");
        rig.Speaker.Play(1);

        rig.Session.Submit("Something else.");
        await Until(() => rig.Agent.Count == 2 || rig.Session.IsHoldingTurn, "the next turn");

        Assert.AreEqual(Announcement + " —", rig.Session.History[^1].Text,
                        "the announcement was cut, not unheard: what followed it lines up after the part let go of — "
                        + string.Join(" | ", rig.Session.History.Select(m => $"{m.Role}:{m.Text}")) + $" played {rig.Speaker.PlayedSamples} of {rig.Speaker.QueuedSamples}");
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
        // The state turns to Ended before the closing is done and Ended is raised: the event is what is waited on.
        await Until(() => rig.Session.State == VoiceState.Ended && rig.EndedBecause != "still going", "the end");

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

    [TestMethod]
    public void AnAnnouncedCallThatGoesOnInTheBackground_IsSaidToHaveStarted_NeverToBeDone()
    {
        var picture = new ToolCallAnnouncement("draw", "a high-quality picture", "Drawing a high-quality picture", "Picture made.",
                                               "No picture was made.", "Drawing it in the background.");
        var delete  = new ToolCallAnnouncement("delete", "file a.txt", "Deleting file a.txt", "File deleted.", "The file is unchanged.");
        var actions = AnnouncedActions.Of([new ToolCall("generate_image", [], Announcement: picture), new ToolCall("delete", [], Announcement: delete)]);

        var started = ToolResult.Ok("started in the background") with { InBackground = true };
        Assert.AreEqual("Drawing it in the background. File deleted.",
                        actions.After([new ToolRunResult("generate_image", started), new ToolRunResult("delete", ToolResult.Ok("deleted"))]));
        Assert.AreEqual("Picture made. File deleted.",
                        actions.After([new ToolRunResult("generate_image", ToolResult.Ok("drawn")), new ToolRunResult("delete", ToolResult.Ok("deleted"))]),
                        "the same call done there and then is done");

        var unannounced = AnnouncedActions.Of([new ToolCall("make_video", [])]);
        Assert.AreEqual(Str.Get("Shell.Voice.CarriesOn"), unannounced.After([new ToolRunResult("make_video", started)]),
                        "a call with no words of its own for it is still not said to be done");
    }

    [TestMethod]
    public async Task WhileTheModelThatAnswersMakesWayForAPicture_ListeningIsPaused_AndTakenUpAgainWhenItIsBack()
    {
        await using var rig = await new Rig(Answer("Hello.")).StartAsync();
        byte[] pcm = new byte[320];

        rig.Microphone.Hears(pcm);
        await Until(() => rig.Ears.Appended == 1, "the microphone to be listened to");

        rig.Agent.Away(true);
        await Until(() => rig.Session.State == VoiceState.Paused, "listening to pause");
        rig.Microphone.Hears(pcm);
        await Task.Delay(100);
        Assert.AreEqual(1, rig.Ears.Appended, "nothing said now could be answered, so nothing is listened to");

        rig.Agent.Away(false);
        await Until(() => rig.Session.State == VoiceState.Listening, "listening to be taken up again");
        rig.Microphone.Hears(pcm);
        await Until(() => rig.Ears.Appended == 2, "the microphone to be listened to again");
    }

    [TestMethod]
    public async Task AReplyThatEndsWhileTheModelIsAway_LeavesTheConversationPaused()
    {
        await using var rig = await new Rig(Answer("Drawing it now.")).StartAsync();

        rig.Ears.Hear(new TranscriptFinal("Draw me a fox."));
        await Until(() => rig.Session.State is VoiceState.Thinking or VoiceState.Speaking, "the reply");
        rig.Agent.Away(true);
        Assert.AreNotEqual(VoiceState.Paused, rig.Session.State, "a reply under way carries on");

        await Until(() =>
        {
            rig.Speaker.Play();
            return rig.Session.State == VoiceState.Paused;
        }, "the conversation to pause once the reply is heard");
    }

    [TestMethod]
    public async Task AQuickTurn_IsAnsweredStraightAway_WithoutReasoning()
    {
        await using var rig = await new Rig(Answer("Hello there.")).StartAsync();

        rig.Ears.Hear(new TranscriptFinal("Hello."));
    await Until(() => rig.Agent.Count == 1, "the turn to be taken");
        await rig.PlayOutAsync();

        CollectionAssert.AreEqual(new[] { LlmThinking.Off }, rig.Agent.Thinking);
        CollectionAssert.DoesNotContain(rig.Voice.Texts, Str.Get("Shell.Voice.LetMeThink"));
    }

    [TestMethod]
    public async Task ATurnThatNeedsThought_IsToldSoAtOnce_AndReasonedOver()
    {
        await using var rig = await new Rig(Answer("It tracks a lot, and the tests are thin.")).StartAsync();
        rig.Agent.NeedsThought = _ => true;

        rig.Ears.Hear(new TranscriptFinal("What do you think of this product?"));
    await Until(() => rig.Agent.Count == 1, "the turn to be taken");
        await rig.PlayOutAsync();

        CollectionAssert.AreEqual(new[] { LlmThinking.Allowed }, rig.Agent.Thinking);
        Assert.AreEqual(Str.Get("Shell.Voice.LetMeThink"), rig.Voice.Texts[0], "something is said at once, while it thinks");
    }
}
