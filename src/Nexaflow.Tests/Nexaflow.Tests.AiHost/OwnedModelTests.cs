using System.Diagnostics;
using System.Text;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The real model in the real host — where a machine has it. <c>NEXAFLOW_TEST_GEMMA</c> names a folder holding Gemma
/// 4 E4B's files as the catalog lays them out; <c>NEXAFLOW_TEST_RUNTIME_PACK</c>, when set, runs it on CUDA. What is
/// checked is behaviour — the context appended rather than re-read, media read, cancellation honoured — never wording;
/// the media is the sample dataset's, whose content is beside the point.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class OwnedModelTests
{
    private static AiHostClient? _client;
    private static ModelSpec? _model;

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_GEMMA") is not { Length: > 0 } folder) return;

        var aiDir = Directory.CreateTempSubdirectory("nexaflow-aihost-").FullName;
        _client = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe"), aiDir))
        {
            Environment = new AiHostEnvironment(Environment.GetEnvironmentVariable("NEXAFLOW_TEST_RUNTIME_PACK") is { Length: > 0 } pack ? pack : null),
        };
        _model = new ModelSpec("gemma-4-e4b-it", folder, "gemma4-onnx-community-q4f16", 8L << 30, 16_384);
        await foreach (var _ in _client.SendAsync(id => new LoadModel(id, _model), TimeSpan.FromMinutes(3))) { }
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private static (AiHostClient Client, ModelSpec Model) Model()
    {
        if (_client is null || _model is null) Assert.Inconclusive("NEXAFLOW_TEST_GEMMA names no Gemma 4 E4B folder on this machine.");
        return (_client!, _model!);
    }

    private sealed record Answer(string Text, GenerationEnded Ended);

    private static async Task<Answer> AskAsync(List<ChatTurn> turns, int maxNew = 200, CancellationToken ct = default)
    {
        var (client, model) = Model();
        var text = new StringBuilder();
        GenerationEnded? ended = null;
        await foreach (var reply in client.SendAsync(id => new Generate(id, model, turns, maxNew, RequestPriority.Interactive), TimeSpan.FromMinutes(2), ct))
        {
            if (reply is TextDelta delta) text.Append(delta.Text);
            if (reply is GenerationEnded end) ended = end;
        }
        turns.Add(new ChatTurn(TurnRole.Assistant, text.ToString()));
        return new Answer(text.ToString(), ended!);
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

    [TestMethod]
    public async Task AnImage_IsReadWithItsTurn()
    {
        var turns = Conversation("Describe this image in a few words.");
        turns[^1] = turns[^1] with { Media = [new ChatMedia(MediaKind.Image, TestSampleData.Files("images").First(f => f.EndsWith(".png")), null)] };
        var withImage = await AskAsync(turns);

        var plain = await AskAsync(Conversation("Describe this image in a few words."));

        Assert.IsFalse(string.IsNullOrWhiteSpace(withImage.Text));
        Assert.IsTrue(withImage.Ended.PromptTokens > plain.Ended.PromptTokens + 100, "the image's tokens are in the prompt");
    }

    [TestMethod]
    public async Task ARecording_IsReadWithItsTurn()
    {
        var turns = Conversation("What do you hear in this recording?");
        turns[^1] = turns[^1] with { Media = [new ChatMedia(MediaKind.Audio, null, File.ReadAllBytes(TestSampleData.Files("audio").First(f => f.EndsWith("tone_mono.wav"))))] };

        var answer = await AskAsync(turns);

        Assert.IsFalse(string.IsNullOrWhiteSpace(answer.Text));
        Assert.IsTrue(answer.Ended.PromptTokens > 40, "the recording's tokens are in the prompt");
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
