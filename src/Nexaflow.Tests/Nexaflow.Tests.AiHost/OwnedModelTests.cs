using System.Diagnostics;
using System.Text;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The real model in the real host — where a machine has it: Gemma 4 E4B, <c>gemma-4-e4b-it</c> from the test store
/// (<see cref="TestStore"/>), held where the host places it — on CUDA where the store holds the pack this machine runs, on the CPU
/// otherwise. What is
/// checked is behaviour — the context appended rather than re-read, media read where it comes, calls
/// made and what came of them read on, cancellation honoured — never wording; the media is the sample dataset's, whose
/// content is beside the point.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("ai-host")]
public class OwnedModelTests
{
    /// <summary>The model's chat part, as a request names it.</summary>
    private static readonly ModelRef Chat = new("gemma-4-e4b-it", "chat");

    private static AiHostClient? _client;

    /// <summary>The model held for the class's requests, as a conversation holds it.</summary>
    private static HostHold? _hold;

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
        _client = await TestStore.HostOnEitherAsync();
        if (_client is null) return;
        _hold = await TestStore.HoldPlacedAsync(_client, [Chat], RequestPriority.Interactive);
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_hold is not null) await _hold.DisposeAsync();
        if (_client is not null) await _client.DisposeAsync();
    }

    private static (AiHostClient Client, ModelRef Model) Model()
    {
        if (_client is null || _hold is null) Assert.Inconclusive(TestStore.Missing(gpu: false));
        return (_client!, Chat);
    }

    private sealed record Answer(string Text, GenerationEnded Ended, IReadOnlyList<ChatToolCall> Calls);

    /// <summary>Asks the model; <paramref name="maxNew"/> counts its reasoning too, so it is the app's own budget.</summary>
    private static async Task<Answer> AskAsync(List<ChatTurn> turns, int maxNew = 4096, IReadOnlyList<ChatTool>? tools = null, CancellationToken ct = default,
                                               TokenSampling? sampling = null)
    {
        var (client, model) = Model();
        var text = new StringBuilder();
        var calls = new List<ChatToolCall>();
        GenerationEnded? ended = null;
        await foreach (var reply in client.SendAsync(id => new Generate(id, model, turns, maxNew, RequestPriority.Interactive, tools, Sampling: sampling),
                                                     TimeSpan.FromMinutes(2), ct))
        {
            if (reply is TextDelta delta) text.Append(delta.Text);
            if (reply is ToolCallMade made) calls.Add(made.Call);
            if (reply is GenerationEnded end) ended = end;
        }
        turns.Add(new ChatTurn(TurnRole.Assistant, text.ToString()) { ToolCalls = calls.Count > 0 ? calls : null });
        return new Answer(text.ToString(), ended!, calls);
    }

    private static List<ChatTurn> Conversation(string first)
        => [new(TurnRole.System, "You are the assistant inside Nexaflow. Answer in one short sentence."), new(TurnRole.User, first)];

    [TestMethod]
    public async Task AConversation_IsReadOnce_AndEachFollowUpOnlyAppended()
    {
        var turns = Conversation("Name a primary colour.");

        var first = await AskAsync(turns);
        turns.Add(new ChatTurn(TurnRole.User, "Name another."));
        var second = await AskAsync(turns);

        Assert.AreEqual(StopReason.EndOfTurn, first.Ended.Reason);
        Assert.IsFalse(string.IsNullOrWhiteSpace(second.Text));
        Assert.AreEqual(first.Ended.PromptTokens, first.Ended.ReadTokens, $"the first turn reads the whole prompt ({first.Ended})");
        Assert.IsTrue(second.Ended.ReadTokens < second.Ended.PromptTokens / 2,
            $"the follow-up read {second.Ended.ReadTokens} of {second.Ended.PromptTokens} tokens: it should only append its own turn");
    }

    /// <summary>
    /// Sampling as Google recommends draws differently from seed to seed; one seed, asked the same in the same order on a
    /// fresh host, draws the same. Not within one host: what other conversations hold in the cache moves the arithmetic a
    /// little, enough to move a draw — and a greedy reply too.
    /// </summary>
    [TestMethod]
    public async Task AReplySampledAsGoogleRecommends_DiffersBySeed_AndOneSeedOnAFreshHostDrawsTheSame()
    {
        var (_, model) = Model();
        var gemma = new TokenSampling(1.0f, 64, 0.95f, Seed: 11);
        List<ChatTurn> Asked() => Conversation("Write two sentences about a lighthouse keeper.");

        async Task<(string[] Replies, PartPlacement Placed)> OnAFreshHostAsync()
        {
            // The same store on the same machine, beside the class's host: each fresh host places the model as the other did.
            await using var host = (await TestStore.HostOnEitherAsync())!;
            await using var hold = await TestStore.HoldPlacedAsync(host, [model], RequestPriority.Interactive);
            var replies = new List<string>();
            foreach (var seed in new uint[] { 11, 12, 13 })
            {
                var text = new StringBuilder();
                await foreach (var reply in host.SendAsync(id => new Generate(id, model, Asked(), 200, RequestPriority.Interactive, Sampling: gemma with { Seed = seed }),
                                                           TimeSpan.FromMinutes(2)))
                    if (reply is TextDelta delta) text.Append(delta.Text);
                replies.Add(text.ToString());
            }
            return ([.. replies], hold.PlacementOf(model)!);
        }

        var first = await OnAFreshHostAsync();
        var again = await OnAFreshHostAsync();

        Assert.AreEqual(first.Placed, again.Placed, "each fresh host runs the model where the other did");
        CollectionAssert.AreEqual(first.Replies, again.Replies, "the same requests in the same order draw the same");
        Assert.IsTrue(first.Replies.Distinct().Count() > 1, "it is sampled, not the likeliest token each time");
    }

    [TestMethod]
    public async Task AnImage_IsReadWithItsTurn()
    {
        var turns = Conversation("Describe this image in a few words.");
        turns[^1] = turns[^1] with { Media = [new ChatMedia(MediaKind.Image, SharedBytes.Of(File.ReadAllBytes(TestSampleData.Files("images").First(f => f.EndsWith(".png")))))] };
        var withImage = await AskAsync(turns);

        var plain = await AskAsync(Conversation("Describe this image in a few words."));

        Assert.IsFalse(string.IsNullOrWhiteSpace(withImage.Text));
        // Gemma 4 gives an image as many tokens as its size calls for, from about 70 for a small one.
        Assert.IsTrue(withImage.Ended.PromptTokens > plain.Ended.PromptTokens + 50,
            $"the image's tokens are in the prompt: {withImage.Ended.PromptTokens} with it, {plain.Ended.PromptTokens} without");
    }

    [TestMethod]
    public async Task AnImage_JoinsTheConversationTheModelHolds_WithoutItBeingReadAgain()
    {
        var turns = Conversation("Name a primary colour.");
        await AskAsync(turns);
        turns.Add(new ChatTurn(TurnRole.User, "Now describe this image in a few words.",
                               [new ChatMedia(MediaKind.Image, SharedBytes.Of(File.ReadAllBytes(TestSampleData.Files("images").First(f => f.EndsWith(".png")))))]));

        var answer = await AskAsync(turns);

        Assert.IsFalse(string.IsNullOrWhiteSpace(answer.Text));
        Assert.IsTrue(answer.Ended.ReadTokens > 50, $"the image's tokens are read ({answer.Ended})");
        Assert.IsTrue(answer.Ended.ReadTokens < answer.Ended.PromptTokens, $"what was read before is not read again ({answer.Ended})");
    }

    private static readonly ChatTool CurrentTime = new("get_current_time", "Gets the current local time.",
        "{\"type\":\"object\",\"properties\":{\"zone\":{\"type\":\"string\",\"description\":\"An IANA time zone.\"}},\"required\":[\"zone\"]}");

    [TestMethod]
    public async Task AToolCall_IsMadeInTheModelsSyntax_AndWhatCameOfIt_IsReadOnto()
    {
        var turns = Conversation("What time is it in Paris? Use the tool.");

        var asked = await AskAsync(turns, tools: [CurrentTime]);
        Assert.AreEqual(StopReason.ToolCalls, asked.Ended.Reason, $"it calls the tool ({asked.Text})");
        Assert.AreEqual("get_current_time", asked.Calls[0].Name);
        turns.Add(new ChatTurn(TurnRole.Tool, "14:05") { Answers = asked.Calls[0] });

        var answered = await AskAsync(turns, tools: [CurrentTime]);

        Assert.AreEqual(StopReason.EndOfTurn, answered.Ended.Reason);
        Assert.IsFalse(string.IsNullOrWhiteSpace(answered.Text));
        Assert.IsTrue(answered.Ended.ReadTokens < answered.Ended.PromptTokens / 2,
            $"what came of the call is read onto the conversation ({answered.Ended})");
    }

    [TestMethod]
    public async Task ARecording_IsReadWithItsTurn()
    {
        var turns = Conversation("What do you hear in this recording?");
        turns[^1] = turns[^1] with { Media = [new ChatMedia(MediaKind.Audio, SharedBytes.Of(File.ReadAllBytes(TestSampleData.Files("audio").First(f => f.EndsWith("tone_mono.wav")))))] };

        var answer = await AskAsync(turns);

        Assert.IsFalse(string.IsNullOrWhiteSpace(answer.Text));
        Assert.IsTrue(answer.Ended.PromptTokens > 40, "the recording's tokens are in the prompt");
    }

    [TestMethod]
    public async Task TheModelReasons_ButOnlyItsAnswerIsSent()
    {
        var answer = await AskAsync(Conversation("A train leaves at 14:10 and arrives at 16:45. How long is the journey?"));

        Assert.AreEqual(StopReason.EndOfTurn, answer.Ended.Reason);
        Assert.IsFalse(string.IsNullOrWhiteSpace(answer.Text));
        Assert.IsFalse(answer.Text.StartsWith("thought", StringComparison.OrdinalIgnoreCase) || answer.Text.Contains("<|channel>"),
                       $"the reasoning stays with the model: {answer.Text}");
        Assert.AreEqual(answer.Text.TrimStart(), answer.Text, "the answer starts at its first word");
    }

    [TestMethod]
    public async Task AReply_StopsAtTheRequestsLimit()
    {
        var answer = await AskAsync(Conversation("Count from one to one hundred, in words."), maxNew: 5);

        Assert.AreEqual((StopReason.MaxTokens, 5), (answer.Ended.Reason, answer.Ended.GeneratedTokens));
    }

    [TestMethod]
    public async Task CancellingAReply_StopsItAtOnce_AndTheHostServesTheNext()
    {
        var (client, model) = Model();
        using var cts = new CancellationTokenSource();
        var watch = new Stopwatch();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var reply in client.SendAsync(id => new Generate(id, model, Conversation("Write a long essay about rivers."), 2000, RequestPriority.Interactive),
                                                         TimeSpan.FromMinutes(2), cts.Token))
                if (reply is TextDelta && !watch.IsRunning) { watch.Start(); cts.Cancel(); }
        });
        watch.Stop();

        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(1), $"stopping took {watch.Elapsed.TotalMilliseconds:0} ms");
        Assert.IsFalse(string.IsNullOrWhiteSpace((await AskAsync(Conversation("Say hello."))).Text));
    }
}
