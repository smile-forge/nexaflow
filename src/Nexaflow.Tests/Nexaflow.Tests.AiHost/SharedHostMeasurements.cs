using System.Diagnostics;
using System.Text;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// What sharing one AI host between apps costs and buys, measured on this machine's GPU with real models: Nexaflow's people-facing
/// model and voice beside the background thinking of an app like Aria, whose engine builds its state through one-shot calls below
/// people's priority. Each app holds its models as an app does, by the catalog's names for them, and the host places them. They report
/// numbers, asserting only what the host promises; each is inconclusive where its models are not named.
/// <list type="bullet">
/// <item>The test store (<see cref="TestStore"/>), holding the CUDA pack — every measurement is of the GPU — Gemma 4 26B A4B's QAT
/// build, Kokoro, and Gemma 4 E4B, the second people-facing model for three apps on one card.</item>
/// <item><c>NEXAFLOW_TEST_ARIA_MODEL</c>: the language model of the catalog the background app runs, by its id, in the test store.</item>
/// <item><c>NEXAFLOW_TEST_MEASUREMENTS</c>, optional: a file each measurement's lines are appended to.</item>
/// </list>
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("ai-host")]
public class SharedHostMeasurements
{
    /// <summary>The client's stall limit for a language model's request, as the app's own provider sets it.</summary>
    private static readonly TimeSpan StallLimit = TimeSpan.FromMinutes(2);

    public TestContext TestContext { get; set; } = null!;

    /// <summary>A host keeping the test store, on the CUDA pack there: every measurement is of the GPU.</summary>
    private static async Task<AiHostClient> HostOnTheCardAsync()
        => await TestStore.HostAsync() ?? throw new AssertInconclusiveException(TestStore.Missing());

    /// <summary>The background app's model, its chat part: the language model of the catalog <c>NEXAFLOW_TEST_ARIA_MODEL</c> names.</summary>
    private static ModelRef Aria()
        => Environment.GetEnvironmentVariable("NEXAFLOW_TEST_ARIA_MODEL") is { Length: > 0 } id
           && HostCatalog.Find(id) is HostedModel { Kind: HostedKind.Language } model
           && model.Parts.FirstOrDefault(p => p.Role == HostedRoles.Chat) is { } chat
            ? new ModelRef(id, chat.Name)
            : throw new AssertInconclusiveException("NEXAFLOW_TEST_ARIA_MODEL names no language model of the catalog for the background app.");

    /// <summary>Nexaflow's people-facing model: Gemma 4 26B A4B's QAT build, its chat part.</summary>
    private static readonly ModelRef People = new("gemma-4-26b-a4b-it", "chat");

    /// <summary>A second people-facing model, for three apps on one card: Gemma 4 E4B, its chat part.</summary>
    private static readonly ModelRef Second = new("gemma-4-e4b-it", "chat");

    /// <summary>Nexaflow's voice: Kokoro, speaking in the voice the catalog gives each language.</summary>
    private const string Voice = "kokoro-82m";

    /// <summary>Kokoro's parts, as Nexaflow holds them for a voice conversation.</summary>
    private static IReadOnlyList<ModelRef> VoiceParts => TestStore.HeldPartsOf(Voice, cuda: true);

    private void Report(string line)
    {
        TestContext.WriteLine(line);
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_MEASUREMENTS") is { Length: > 0 } file)
            File.AppendAllText(file, line + Environment.NewLine);
    }

    private static string Mib(long bytes) => $"{bytes >> 20:N0} MiB";

    // ── Budgets ───────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A host holding Nexaflow's model leaves another on the card only what it does not hold. The other is the host's own parts in this
    /// process (<see cref="InProcessHost"/>), asked for a size no plan of its own would place there — past what the first leaves it — and
    /// refusing it before anything loads.
    /// </summary>
    [TestMethod]
    public async Task TwoHostsOnOneCard_EachLeavesTheOtherWhatItHolds()
    {
        await using var first = await HostOnTheCardAsync();
        using var second = InProcessHost.OnTheGpu();
        long secondBefore = second.Models.SpareOn(0);
        await using var held = await TestStore.HoldPlacedAsync(first, [People], RequestPriority.Interactive);
        var people = held.PlacementOf(People)!;

        // Within what the second host had when it started, past what the first leaves it now: refused before anything loads.
        var crowding = TestStore.Spec(People.Model, People.Part, 0) with { Id = "people/crowding", MemoryBytes = secondBefore - people.MemoryBytes / 2 };
        var refused = await Assert.ThrowsExactlyAsync<HostRequestException>(
            async () => { using var _ = await second.Models.AcquireAsync(crowding, resident: true, _ => Task.CompletedTask, CancellationToken.None); });
        Report($"the second host started with {Mib(secondBefore)} to budget; once the first held {Mib(people.MemoryBytes)}, "
               + $"it refused {Mib(crowding.MemoryBytes)}: {refused.Message}");
        Assert.AreEqual(HostErrorKind.Refused, refused.Kind);
    }

    // ── Starvation ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static List<ChatTurn> Story(int i)
        => [new(TurnRole.System, "You are a storyteller."), new(TurnRole.User, $"Tell a long story, number {i}, about a lighthouse keeper and a storm.")];

    /// <summary>Replies one after another on <paramref name="model"/> until <paramref name="stop"/>, as a person reading them would
    /// ask; what came of them.</summary>
    private static async Task<(int Replies, int Tokens, string? Lost)> PeopleAsync(AiHostClient host, ModelRef model, CancellationToken stop)
    {
        int replies = 0, tokens = 0;
        try
        {
            for (int i = 0; !stop.IsCancellationRequested; i++)
            {
                await foreach (var reply in host.SendAsync(id => new Generate(id, model, Story(i), 600, RequestPriority.Interactive, Thinks: false),
                                                           StallLimit, stop))
                    if (reply is GenerationEnded ended) tokens += ended.GeneratedTokens;
                replies++;
            }
        }
        catch (OperationCanceledException) { }
        catch (AiHostException ex) { return (replies, tokens, ex.Message); }
        return (replies, tokens, null);
    }

    [TestMethod]
    public async Task ThreeAppsOnOneCard_TwoPeopleReading_StillLeaveThinkingNobodyWatchesATurn()
    {
        var aria = Aria();

        await using var host = await HostOnTheCardAsync();
        await using var reading = await TestStore.HoldPlacedAsync(host, [People, Second], RequestPriority.Interactive);
        await using var thinking = await TestStore.HoldPlacedAsync(host, [aria], RequestPriority.Background);

        using var stop = new CancellationTokenSource(StallLimit + TimeSpan.FromSeconds(40));
        var readers = new[] { PeopleAsync(host, People, stop.Token), PeopleAsync(host, Second, stop.Token) };
        await Task.Delay(TimeSpan.FromSeconds(5));

        var watch = Stopwatch.StartNew();
        TimeSpan? firstWord = null, ended = null;
        string outcome;
        try
        {
            await foreach (var reply in host.SendAsync(id => new Generate(id, aria, AriaTurns(0, history: 4), 256, RequestPriority.Background, AriaTools),
                                                       StallLimit, stop.Token))
                firstWord ??= watch.Elapsed;
            ended = watch.Elapsed;
            outcome = "completed";
        }
        catch (OperationCanceledException) { outcome = "still waiting when the readers stopped"; }
        catch (AiHostException ex) { outcome = $"{ex.Failure}: {ex.Message}"; }

        stop.Cancel();
        var people1 = await readers[0];
        var people2 = await readers[1];
        Report($"thinking beside two readers: first reply after {(firstWord?.TotalSeconds.ToString("0.0") ?? "none")} s, "
               + $"ended after {(ended?.TotalSeconds.ToString("0.0") ?? "-")} s; {outcome}");
        Report($"readers meanwhile: {people1.Replies} replies / {people1.Tokens} tokens ({people1.Lost ?? "kept"}), "
               + $"{people2.Replies} replies / {people2.Tokens} tokens ({people2.Lost ?? "kept"})");
        Assert.AreEqual("completed", outcome, "work nobody watches has a turn in every few while two people read");
    }

    // ── Voice beside thinking ─────────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly string[] Questions =
    [
        "What should I take on a day's walk in the hills?",
        "How do I keep basil alive indoors?",
        "Why is the sky blue?",
        "What is a good way to start learning the piano?",
        "How long should I boil an egg for a soft yolk?",
        "What is the difference between weather and climate?",
        "How can I sleep better after a late coffee?",
        "What makes sourdough bread rise?",
    ];

    private sealed record VoiceTurn(TimeSpan FirstWord, TimeSpan Sentence, TimeSpan Audio);

    /// <summary>One spoken turn: the reply's first sentence written, then its first audio, from the moment the person stopped.</summary>
    private static async Task<VoiceTurn> SpeakAsync(AiHostClient host, ModelRef model, string question)
    {
        var watch = Stopwatch.StartNew();
        var text = new StringBuilder();
        TimeSpan? firstWord = null, sentence = null;
        string? said = null;
        List<ChatTurn> turns = [new(TurnRole.System, "You are a voice assistant. Answer in two short spoken sentences."), new(TurnRole.User, question)];
        await foreach (var reply in host.SendAsync(id => new Generate(id, model, turns, 120, RequestPriority.Voice, Thinks: false, Conversation: "voice"), StallLimit))
        {
            if (reply is not TextDelta delta) continue;
            firstWord ??= watch.Elapsed;
            text.Append(delta.Text);
            if (FirstSentence(text.ToString()) is { } first)
            {
                sentence = watch.Elapsed;
                said = first;
                break;   // the rest is written while the first is spoken; leaving early stops it in the host
            }
        }
        said ??= text.ToString();
        sentence ??= watch.Elapsed;

        await using var speaking = await host.OpenStreamAsync(id => new Speak(id, Voice, 1f, RequestPriority.Voice), StallLimit);
        await speaking.WriteAsync(new TextToSpeak(speaking.RequestId, 0, said, "en"));
        await speaking.WriteAsync(new InputEnd(speaking.RequestId));
        TimeSpan? audio = null;
        await foreach (var reply in speaking.ReadAsync())
            if (reply is SpokenAudio) { audio = watch.Elapsed; break; }
        return new VoiceTurn(firstWord ?? watch.Elapsed, sentence.Value, audio ?? watch.Elapsed);
    }

    private static string? FirstSentence(string text)
    {
        int end = text.IndexOfAny(['.', '!', '?']);
        return end > 8 ? text[..(end + 1)] : null;
    }

    private sealed record Thinking(int Calls, int Tokens, int Lost, TimeSpan Ran);

    /// <summary>The background app's own work, one call at a time as its engine makes them, until <paramref name="stop"/>.</summary>
    private static async Task<Thinking> ThinkAsync(AiHostClient host, ModelRef model, CancellationToken stop)
    {
        var watch = Stopwatch.StartNew();
        int calls = 0, tokens = 0, lost = 0;
        for (int i = 0; !stop.IsCancellationRequested; i++)
        {
            var (turns, tools, most, thinks) = (i % 3) switch
            {
                0 => (Classify(i), (IReadOnlyList<ChatTool>?)null, 24, false),
                1 => (AriaTurns(i, history: i % 6), AriaTools, 256, true),
                _ => (Compact(i), (IReadOnlyList<ChatTool>?)null, 200, false),
            };
            try
            {
                await foreach (var reply in host.SendAsync(id => new Generate(id, model, turns, most, RequestPriority.Background, tools, thinks, Conversation: i % 3 == 1 ? "aria" : null), StallLimit, stop))
                    if (reply is GenerationEnded ended) tokens += ended.GeneratedTokens;
                calls++;
            }
            catch (OperationCanceledException) { break; }
            catch (AiHostException) { lost++; }
        }
        return new Thinking(calls, tokens, lost, watch.Elapsed);
    }

    private sealed record Setup(string Name, AiHostClient Voice, AiHostClient? Thinker, ModelRef VoiceModel, ModelRef? ThinkingModel);

    /// <summary>Measures the voice of <paramref name="setup"/> beside its thinking; what the thinking came to, where it has any.</summary>
    private async Task<Thinking?> MeasureAsync(Setup setup)
    {
        using var gpu = GpuMemoryWatch.Start();
        using var stop = new CancellationTokenSource();
        var thinking = setup.Thinker is { } thinker && setup.ThinkingModel is { } model ? ThinkAsync(thinker, model, stop.Token) : null;
        if (thinking is not null) await Task.Delay(TimeSpan.FromSeconds(10));

        var turns = new List<VoiceTurn>();
        await SpeakAsync(setup.Voice, setup.VoiceModel, "Say hello.");   // warms the voice path
        foreach (var question in Questions)
        {
            turns.Add(await SpeakAsync(setup.Voice, setup.VoiceModel, question));
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        stop.Cancel();
        var thought = thinking is null ? null : await thinking;

        static string Ms(IEnumerable<TimeSpan> spans)
        {
            var sorted = spans.Select(s => s.TotalMilliseconds).Order().ToArray();
            return $"median {sorted[sorted.Length / 2]:0} ms, worst {sorted[^1]:0} ms";
        }
        Report($"{setup.Name}: first word {Ms(turns.Select(t => t.FirstWord))}; first sentence {Ms(turns.Select(t => t.Sentence))}; "
               + $"first audio {Ms(turns.Select(t => t.Audio))}; GPU peak {Mib(gpu.PeakBytes)}");
        if (thought is not null)
            Report($"{setup.Name}: thinking made {thought.Calls} calls, {thought.Tokens} tokens in {thought.Ran.TotalSeconds:0} s, {thought.Lost} lost");
        return thought;
    }

    [TestMethod]
    public async Task Voice_BesideThinkingNobodyWatches_InSeparateHostsAndInOne()
    {
        var aria = Aria();

        await using (var alone = await HostOnTheCardAsync())
        {
            await using var people = await TestStore.HoldPlacedAsync(alone, [People], RequestPriority.Interactive);
            await using var voice = await TestStore.HoldPlacedAsync(alone, VoiceParts, RequestPriority.Voice);
            await MeasureAsync(new Setup("voice alone", alone, null, People, null));
        }

        await using (var nexaflow = await HostOnTheCardAsync())
        await using (var thinker = await HostOnTheCardAsync())
        {
            await using var people = await TestStore.HoldPlacedAsync(nexaflow, [People], RequestPriority.Interactive);
            await using var voice = await TestStore.HoldPlacedAsync(nexaflow, VoiceParts, RequestPriority.Voice);
            await using var thinking = await TestStore.HoldPlacedAsync(thinker, [aria], RequestPriority.Background);
            await MeasureAsync(new Setup("separate hosts, own models", nexaflow, thinker, People, aria));
        }

        await using (var shared = await HostOnTheCardAsync())
        {
            await using var people = await TestStore.HoldPlacedAsync(shared, [People], RequestPriority.Interactive);
            await using var voice = await TestStore.HoldPlacedAsync(shared, VoiceParts, RequestPriority.Voice);
            await using var thinking = await TestStore.HoldPlacedAsync(shared, [aria], RequestPriority.Background);
            await MeasureAsync(new Setup("one host, own models", shared, shared, People, aria));
        }

        await using (var shared = await HostOnTheCardAsync())
        {
            await using var people = await TestStore.HoldPlacedAsync(shared, [People], RequestPriority.Interactive);
            await using var voice = await TestStore.HoldPlacedAsync(shared, VoiceParts, RequestPriority.Voice);
            await MeasureAsync(new Setup("one host, one model", shared, shared, People, People));
        }
    }

    // ── What appending buys the background app ────────────────────────────────────────────────────────────────────────────────

    // ── One service, several apps ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The machine's service as apps share it, run in the test (<see cref="ServiceUnderTest"/>): its worker on the test
    /// store, on the CUDA pack there — every measurement is of the GPU.</summary>
    private static ServiceUnderTest ServiceOnTheCard()
        => TestStore.Root is { } root && TestStore.RuntimePackFolder is not null
            ? ServiceUnderTest.Start(root)
            : throw new AssertInconclusiveException(TestStore.Missing());

    /// <summary>
    /// Three apps on one service, each an app of its own to the host: Nexaflow holding its model and voice for a conversation, Aria
    /// thinking for itself below people's priority, and a third app asking of the model Nexaflow holds — one copy on the card for both.
    /// Each is served and none loses a request; the voice's times are reported beside the separate setups above.
    /// </summary>
    [TestMethod]
    public async Task ThreeAppsOnOneService_VoiceBesideAnotherAppsThinking_AndAThirdAsking()
    {
        var aria = Aria();
        await using var service = ServiceOnTheCard();
        await using var nexaflow = service.Join("Nexaflow");
        await using var thinker = service.Join("Aria");
        await using var asker = service.Join("Sample");

        await using var people = await TestStore.HoldPlacedAsync(nexaflow, [People], RequestPriority.Interactive);
        await using var voice = await TestStore.HoldPlacedAsync(nexaflow, VoiceParts, RequestPriority.Voice);
        await using var thinking = await TestStore.HoldPlacedAsync(thinker, [aria], RequestPriority.Background);
        await using var asking = await TestStore.HoldPlacedAsync(asker, [People], RequestPriority.Background);

        using var stop = new CancellationTokenSource();
        var third = ThinkAsync(asker, People, stop.Token);
        var thought = await MeasureAsync(new Setup("one service, three apps", nexaflow, thinker, People, aria));
        await stop.CancelAsync();
        var asked = await third;
        Report($"one service, three apps: the third app made {asked.Calls} calls, {asked.Tokens} tokens in {asked.Ran.TotalSeconds:0} s, {asked.Lost} lost");

        Assert.IsTrue(thought is { Calls: > 0, Lost: 0 }, "the thinking app is served beside the voice, losing nothing");
        Assert.IsTrue(asked is { Calls: > 0, Lost: 0 }, "and so is the third");
    }

    /// <summary>
    /// An app that asks and stops reading holds back only its own replies. Its replies fill its pipe and then the host's queue for it,
    /// and stand aside from the rounds they share with another app's on the same model; the other's replies come as fast as with no
    /// such app beside them. The app that stopped reading loses nothing: once it reads again, each of its requests ends, having gone on
    /// to generate far more than its pipe and queue held — it was held back, not finished, while the other app was measured.
    /// </summary>
    [TestMethod]
    public async Task AnAppThatStopsReading_HoldsBackOnlyItsOwnReplies()
    {
        const int Asked = 4, Most = 6000, Long = 1000;
        await using var service = ServiceOnTheCard();
        await using var reader = service.Join("Reader");
        await using var held = await TestStore.HoldPlacedAsync(reader, [People], RequestPriority.Interactive);

        async Task<double> TokensPerSecondAsync()
        {
            int tokens = 0;
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 3; i++)
                await foreach (var reply in reader.SendAsync(id => new Generate(id, People, Story(i), 400, RequestPriority.Interactive, Thinks: false), StallLimit))
                    if (reply is GenerationEnded ended) tokens += ended.GeneratedTokens;
            return tokens / watch.Elapsed.TotalSeconds;
        }

        await TokensPerSecondAsync();   // warms the model
        double alone = await TokensPerSecondAsync();

        await using var stalled = await UnreadClient.JoinAsync(service.PipeName, "Stalled");
        // Replies that run long — a model asked to count far cuts it short — so they are still being written when the other app is measured.
        List<ChatTurn> Saga(int i) => [new(TurnRole.User, $"Write a long saga, number {i}, of at least four thousand words, about a city that floods. Write it in full.")];
        var asked = new List<long>();
        for (int i = 0; i < Asked; i++)
        {
            int n = i;
            asked.Add(await stalled.AskAsync(id => new Generate(id, People, Saga(n), Most, RequestPriority.Interactive, Thinks: false)));
        }

        // Full once the host has stopped writing to its pipe: what the pipe holds no longer grows.
        long waiting = -1;
        var still = Stopwatch.StartNew();
        var filling = Stopwatch.StartNew();
        while (still.Elapsed < TimeSpan.FromSeconds(5) && filling.Elapsed < TimeSpan.FromMinutes(2))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            if (stalled.Waiting is var now && now != waiting)
            {
                waiting = now;
                still.Restart();
            }
        }

        double beside = await TokensPerSecondAsync();
        using var drained = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var (ends, generated) = await stalled.ReadUntilEndedAsync(asked, drained.Token);

        Report($"replies alone: {alone:0} tok/s; beside {Asked} requests of an app that stopped reading, its pipe full at {waiting >> 10} KiB "
               + $"after {filling.Elapsed.TotalSeconds - 5:0} s: {beside:0} tok/s; read again, its requests generated {generated:N0} tokens");
        // Its pipe and its queue hold a few hundred replies: requests that went on well past them were held back, not finished.
        Assert.IsTrue(generated > Asked * Long, $"its requests generated {generated:N0} tokens: they ended before the other app was measured, not held back");
        Assert.IsTrue(beside >= alone * 0.6, $"replies beside an app that stopped reading came at {beside:0} tok/s, against {alone:0} alone");
        Assert.AreEqual(Asked, ends.Values.Count(e => e is Completed), "read again, every one of its requests ended, none lost");
    }

    /// <summary>
    /// A picture one app draws on a card too full for it beside another app's voice conversation makes that conversation's model wait:
    /// Nexaflow's hold is told its model is away while the picture is drawn, and back after, and the picture is drawn. The card is made
    /// tight before the service starts (<see cref="GpuBallast"/>): it leaves room for the conversation and half the picture.
    /// </summary>
    [TestMethod]
    public async Task APictureOneAppDraws_MakesAnotherAppsConversationWait_AndGivesItBack()
    {
        const string painting = "z-image-turbo";
        static long Bytes(ModelRef part) => HostCatalog.Find(part.Model)!.PartsFor(HostCatalog.Cuda).First(p => p.Name == part.Part).MemoryBytes;
        long conversationBytes = Bytes(People) + VoiceParts.Sum(Bytes) + (4L << 30);   // and room for the model's context
        long picture = HostCatalog.Find(painting)!.PartsFor(HostCatalog.Cuda).Sum(p => p.MemoryBytes);
        using var ballast = GpuBallast.Leaving(conversationBytes + picture / 2);

        await using var service = ServiceOnTheCard();
        await using var nexaflow = service.Join("Nexaflow");
        await using var painter = service.Join("Painter");
        await using var conversation = await TestStore.HoldPlacedAsync(nexaflow, [People, .. VoiceParts], RequestPriority.Voice);
        int away = 0;
        conversation.Changed += (_, _) =>
        {
            if (conversation.AnyAway) Interlocked.Increment(ref away);
        };

        var watch = Stopwatch.StartNew();
        bool drawn = false;
        await foreach (var reply in painter.SendAsync(id => new Draw(id, painting, "A lighthouse in a storm, at dusk.", 1024, 1024, 7, RequestPriority.Job),
                                                      TimeSpan.FromMinutes(5)))
        {
            if (reply is not Drawn picturePixels) continue;
            drawn = true;
            picturePixels.Rgb.Dispose();
        }
        var drawing = watch.Elapsed;
        var back = Stopwatch.StartNew();
        while (conversation.AnyAway && back.Elapsed < TimeSpan.FromMinutes(2)) await Task.Delay(50);

        Report($"held {Mib(ballast.Bytes)} to leave the card room for the conversation and half the picture; the picture took "
               + $"{drawing.TotalSeconds:0.0} s, the conversation's model was back {back.Elapsed.TotalSeconds:0.0} s after");
        Assert.IsTrue(drawn, "the picture was drawn");
        Assert.IsTrue(away > 0, "the conversation's hold was told its model made way");
        Assert.IsFalse(conversation.AnyAway, "and told it was back");
    }

    [TestMethod]
    public async Task ABackgroundAppsConversation_ReadAppended_AgainstReadWholeEachCall()
    {
        var aria = Aria();
        await using var host = await HostOnTheCardAsync();
        await using var thinking = await TestStore.HoldPlacedAsync(host, [aria], RequestPriority.Background);

        foreach (bool clockInSystemTurn in new[] { false, true })
        {
            var history = new List<ChatTurn>();
            double readRate = 0;
            long read = 0, prompt = 0;
            TimeSpan waited = TimeSpan.Zero, wholeEstimate = TimeSpan.Zero;
            for (int step = 0; step < 6; step++)
            {
                var turns = new List<ChatTurn> { new(TurnRole.System, AriaSystem(clockInSystemTurn ? DateTime.UtcNow.AddMinutes(step) : null)) };
                turns.AddRange(history);
                turns.Add(new ChatTurn(TurnRole.User, UserMessages[step]));

                var watch = Stopwatch.StartNew();
                TimeSpan? firstWord = null;
                var text = new StringBuilder();
                GenerationEnded? ended = null;
                var calls = new List<ChatToolCall>();
                await foreach (var reply in host.SendAsync(id => new Generate(id, aria, turns, 2048, RequestPriority.Background, AriaTools, Thinks: false, Conversation: "aria"), StallLimit))
                {
                    if (reply is TextDelta delta) { firstWord ??= watch.Elapsed; text.Append(delta.Text); }
                    if (reply is ToolCallMade made) { firstWord ??= watch.Elapsed; calls.Add(made.Call); }
                    if (reply is GenerationEnded end) ended = end;
                }
                Report($"  call {step}: read {ended!.ReadTokens} of {ended.PromptTokens}, stopped by {ended.Reason}, {calls.Count} tool call(s)");
                var tillFirst = firstWord ?? watch.Elapsed;
                if (step == 0) readRate = ended!.ReadTokens / tillFirst.TotalSeconds;
                read += ended!.ReadTokens;
                prompt += ended.PromptTokens;
                waited += tillFirst;
                wholeEstimate += TimeSpan.FromSeconds(ended.PromptTokens / readRate);
                history.Add(new ChatTurn(TurnRole.User, UserMessages[step]));
                history.Add(new ChatTurn(TurnRole.Assistant, text.ToString()) { ToolCalls = calls.Count > 0 ? calls : null });
                foreach (var call in calls) history.Add(new ChatTurn(TurnRole.Tool, "{\"result\":\"done\"}") { Answers = call });
            }
            Report($"{(clockInSystemTurn ? "clock in the system turn" : "stable system turn")}: read {read:N0} of {prompt:N0} prompt tokens "
                   + $"over 6 calls; first words after {waited.TotalSeconds:0.0} s in all, against about {wholeEstimate.TotalSeconds:0.0} s "
                   + $"read whole each call (prompt read at {readRate:0} tokens/s)");
        }
    }

    // ── The background app's prompts ──────────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ABackgroundApp_IsSentItsModelsReasoning_WhenItAsks_AndCanGiveItBack()
    {
        var aria = Aria();
        await using var host = await HostOnTheCardAsync();
        await using var thinking = await TestStore.HoldPlacedAsync(host, [aria], RequestPriority.Background);

        var turns = new List<ChatTurn> { new(TurnRole.System, "Be brief."), new(TurnRole.User, "A train leaves at 14:10 and arrives at 16:45. How long is it?") };
        var (text, reasoning) = (new StringBuilder(), new StringBuilder());
        await foreach (var reply in host.SendAsync(id => new Generate(id, aria, turns, 2048, RequestPriority.Background, SendsReasoning: true, Conversation: "reasoning given back"), StallLimit))
        {
            if (reply is TextDelta delta) text.Append(delta.Text);
            if (reply is ReasoningDelta thought) reasoning.Append(thought.Text);
        }
        Report($"reasoning sent: {reasoning.Length} characters, beginning \"{reasoning.ToString()[..Math.Min(60, reasoning.Length)].ReplaceLineEndings(" ")}\"");

        Assert.IsTrue(reasoning.Length > 0, "the model's reasoning is sent");
        Assert.IsFalse(reasoning.ToString().StartsWith("thought", StringComparison.Ordinal) || reasoning.ToString().Contains("<|channel>"),
                       "the reasoning is sent without the channel it is written in");
        Assert.IsFalse(text.ToString().Contains(reasoning.ToString()[..Math.Min(40, reasoning.Length)]), "the reasoning is not part of the reply");

        turns.Add(new ChatTurn(TurnRole.Assistant, text.ToString()) { Reasoning = reasoning.ToString() });
        turns.Add(new ChatTurn(TurnRole.User, "And in minutes?"));
        var again = new StringBuilder();
        await foreach (var reply in host.SendAsync(id => new Generate(id, aria, turns, 2048, RequestPriority.Background, Conversation: "reasoning given back"), StallLimit))
            if (reply is TextDelta delta) again.Append(delta.Text);
        Assert.IsFalse(string.IsNullOrWhiteSpace(again.ToString()), "a conversation given its reasoning back goes on");
    }

    private static readonly string[] UserMessages =
    [
        "Good morning. What did we decide about the garden last week?",
        "Remind me what the weather is meant to be on Saturday.",
        "Can you look up when the library closes today?",
        "Add buying compost to my list for the weekend.",
        "What else is on that list?",
        "Thanks. Let's talk again this evening.",
    ];

    /// <summary>A persona, who it is speaking with, and what it knows of them, as such an engine writes its system turn; with the
    /// time in it where <paramref name="now"/> is given, as some do.</summary>
    private static string AriaSystem(DateTime? now)
    {
        var text = new StringBuilder("You are a companion that lives on this machine. You are curious, warm and direct, and you keep "
                                     + "track of what matters to the people you talk with.\n\n## Current user\nName: Sam\nPreferred language: English\n"
                                     + "Home location: Geneva\n");
        if (now is { } time) text.Append($"Current time: {time:yyyy-MM-dd HH:mm} UTC\n");
        text.Append("\n## Channels\nCurrent message received via: NamedPipe\nAvailable channels:\n- Telegram (online)\n- NamedPipe (online)\n\n");
        text.Append(string.Concat(Enumerable.Repeat("Keep answers short when the person is busy, and say so when you are unsure. "
                                                    + "Remember what was agreed, and bring it up when it becomes relevant again. ", 20)));
        return text.ToString();
    }

    private static List<ChatTurn> AriaTurns(int i, int history)
    {
        var turns = new List<ChatTurn> { new(TurnRole.System, AriaSystem(null)) };
        for (int h = 0; h < history; h++)
        {
            turns.Add(new ChatTurn(TurnRole.User, UserMessages[h % UserMessages.Length]));
            turns.Add(new ChatTurn(TurnRole.Assistant, "I have noted that, and I will bring it up again when it matters."));
        }
        turns.Add(new ChatTurn(TurnRole.User, $"{UserMessages[(i + history) % UserMessages.Length]} ({i})"));
        return turns;
    }

    private static List<ChatTurn> Classify(int i)
        => [new(TurnRole.System, "Classify the intent of the message. Reply with comma-separated labels only: "
                                + "RequestingInformation, ProvidingInformation, DirectCommand, ConditionalCommand, EndingConversation."),
            new(TurnRole.User, $"{UserMessages[i % UserMessages.Length]} ({i})")];

    private static List<ChatTurn> Compact(int i)
        => [new(TurnRole.System, "Summarise the conversation below in three sentences, keeping every decision and date."),
            new(TurnRole.User, string.Join("\n", Enumerable.Range(0, 12).Select(h => $"{(h % 2 == 0 ? "Sam" : "Companion")}: {UserMessages[(i + h) % UserMessages.Length]}")))];

    private static readonly IReadOnlyList<ChatTool> AriaTools =
    [
        .. new[]
        {
            ("get_weather", "Gets the forecast for a place and day."), ("search_web", "Searches the web."),
            ("read_page", "Reads a web page."), ("add_to_list", "Adds an item to one of the person's lists."),
            ("read_list", "Reads one of the person's lists."), ("send_message", "Sends a message to a person on a channel."),
            ("get_time", "Gets the current time in a time zone."), ("remember", "Stores something worth remembering."),
            ("recall", "Finds what was remembered about a subject."), ("find_tool", "Finds a tool for a task."),
            ("get_calendar", "Reads the person's calendar for a day."), ("convert_units", "Converts a quantity between units."),
        }.Select(t => new ChatTool(t.Item1, t.Item2,
            "{\"type\":\"object\",\"properties\":{\"subject\":{\"type\":\"string\",\"description\":\"What it is about.\"},"
            + "\"when\":{\"type\":\"string\",\"description\":\"A day or a time, where it matters.\"}},\"required\":[\"subject\"]}")),
    ];
}
