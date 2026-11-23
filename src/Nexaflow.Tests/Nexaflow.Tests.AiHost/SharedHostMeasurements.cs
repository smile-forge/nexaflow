using System.Diagnostics;
using System.Text;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// What sharing one AI host between apps costs and buys, measured on this machine's GPU with real models: Nexaflow's people-facing
/// model and voice beside the background thinking of an app like Aria, whose engine builds its state through one-shot calls below
/// people's priority. They report numbers, asserting only what the host promises; each is inconclusive where its models are not named.
/// <list type="bullet">
/// <item>The test store (<see cref="TestStore"/>), holding the CUDA pack — every measurement is of the GPU — Gemma 4 26B A4B's QAT
/// build, Kokoro, and Gemma 4 E4B, the second people-facing model for three apps on one card.</item>
/// <item><c>NEXAFLOW_TEST_ARIA_GGUF</c>: the GGUF the background app runs, a build of its own, by its path in the test store.</item>
/// <item><c>NEXAFLOW_TEST_MEASUREMENTS</c>, optional: a file each measurement's lines are appended to.</item>
/// </list>
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("ai-host")]
public class SharedHostMeasurements
{
    private const long GB = 1L << 30;

    /// <summary>The client's stall limit for a language model's request, as the app's own provider sets it.</summary>
    private static readonly TimeSpan StallLimit = TimeSpan.FromMinutes(2);

    public TestContext TestContext { get; set; } = null!;

    /// <summary>A host keeping the test store, on the CUDA pack there: every measurement is of the GPU.</summary>
    private static async Task<AiHostClient> HostOnTheCardAsync()
        => await TestStore.HostAsync() ?? throw new AssertInconclusiveException(TestStore.Missing());

    /// <summary>The GGUF the background app runs, by its path in the test store, which <c>NEXAFLOW_TEST_ARIA_GGUF</c> names.</summary>
    private static string AriaGguf()
        => Environment.GetEnvironmentVariable("NEXAFLOW_TEST_ARIA_GGUF")?.Replace('\\', '/') is { Length: > 0 } path && path.Contains('/')
           && TestStore.Root is not null && File.Exists(TestStore.PathOf(path)) ? path
           : throw new AssertInconclusiveException("NEXAFLOW_TEST_ARIA_GGUF names no background app's GGUF in a folder of the test store.");

    /// <summary>Nexaflow's people-facing model: Gemma 4 26B A4B's QAT build, wholly on the first GPU, as the catalog has it.</summary>
    private static ModelSpec People()
        => TestStore.Spec("gemma-4-26b-a4b-it", "chat", 0, 18_790_481_920L + ContextCache.Q8.BytesPerToken(25_600) * 32_768, 32_768)
           with { Projector = null, ContextCache = ContextCache.Q8 };

    /// <summary>A model named by its GGUF's path in the test store alone, on the first GPU: its file, what llama.cpp works in beside it,
    /// and its context.</summary>
    private static ModelSpec Gguf(string id, string gguf, int contextTokens, long bytesPerToken)
    {
        if (TestStore.Root is null) throw new AssertInconclusiveException(TestStore.Missing());
        int slash = gguf.LastIndexOf('/');
        return new(id, "llama-chat", gguf[..slash], gguf[(slash + 1)..], null,
                   new FileInfo(TestStore.PathOf(gguf)).Length + 3 * GB + ContextCache.Q8.BytesPerToken(bytesPerToken) * contextTokens, contextTokens,
                   ContextCache: ContextCache.Q8, Device: 0, Checkpoints: 8);
    }

    /// <summary>A second people-facing model, for three apps on one card: Gemma 4 E4B's GGUF, as the background app's is named.</summary>
    private static ModelSpec Second()
    {
        var chat = TestStore.Spec("gemma-4-e4b-it", "chat", 0);
        return Gguf("people/second", $"{chat.Folder}/{chat.File}", 8_192, 17_920);
    }

    private static VoiceSpec Voice() => TestStore.Voice("kokoro-82m", "en", 0, 1 * GB);

    private static async Task LoadAsync(AiHostClient host, ModelSpec model)
    {
        await foreach (var _ in host.SendAsync(id => new LoadModel(id, model), TimeSpan.FromMinutes(5))) { }
    }

    private static async Task<HostGpu> GpuOfAsync(AiHostClient host)
    {
        await foreach (var reply in host.SendAsync(id => new DescribeHost(id), TimeSpan.FromMinutes(1)))
            if (reply is HostDescription { Gpus: [var gpu, ..] }) return gpu;
        throw new AssertInconclusiveException("The host found no GPU.");
    }

    private void Report(string line)
    {
        TestContext.WriteLine(line);
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_MEASUREMENTS") is { Length: > 0 } file)
            File.AppendAllText(file, line + Environment.NewLine);
    }

    private static string Mib(long bytes) => $"{bytes >> 20:N0} MiB";

    // ── Budgets ───────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task TwoHostsOnOneCard_EachLeavesTheOtherWhatItHolds()
    {
        var people = People();

        await using var first = await HostOnTheCardAsync();
        await using var second = await HostOnTheCardAsync();
        var secondBefore = await GpuOfAsync(second);
        await LoadAsync(first, people);

        // Within what the second host had when it started, past what the first leaves it now: refused before anything loads.
        var crowding = people with { Id = "people/crowding", MemoryBytes = secondBefore.BudgetBytes - people.MemoryBytes / 2 };
        var refused = await Assert.ThrowsExactlyAsync<AiHostException>(() => LoadAsync(second, crowding));
        Report($"the second host started with {Mib(secondBefore.BudgetBytes)} to budget; once the first held {Mib(people.MemoryBytes)}, "
               + $"it refused {Mib(crowding.MemoryBytes)}: {refused.Message}");
        Assert.AreEqual(HostErrorKind.Refused, refused.Kind);
    }

    // ── Starvation ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static List<ChatTurn> Story(int i)
        => [new(TurnRole.System, "You are a storyteller."), new(TurnRole.User, $"Tell a long story, number {i}, about a lighthouse keeper and a storm.")];

    /// <summary>Replies one after another on <paramref name="model"/> until <paramref name="stop"/>, as a person reading them would
    /// ask; what came of them.</summary>
    private static async Task<(int Replies, int Tokens, string? Lost)> PeopleAsync(AiHostClient host, ModelSpec model, CancellationToken stop)
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
        var people = People();
        var second = Second();
        var aria = Gguf("aria/model", AriaGguf(), 16_384, 25_600);

        await using var host = await HostOnTheCardAsync();
        foreach (var model in new[] { people, second, aria }) await LoadAsync(host, model);

        using var stop = new CancellationTokenSource(StallLimit + TimeSpan.FromSeconds(40));
        var readers = new[] { PeopleAsync(host, people, stop.Token), PeopleAsync(host, second, stop.Token) };
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
    private static async Task<VoiceTurn> SpeakAsync(AiHostClient host, ModelSpec model, VoiceSpec voice, string question)
    {
        var watch = Stopwatch.StartNew();
        var text = new StringBuilder();
        TimeSpan? firstWord = null, sentence = null;
        string? said = null;
        List<ChatTurn> turns = [new(TurnRole.System, "You are a voice assistant. Answer in two short spoken sentences."), new(TurnRole.User, question)];
        await foreach (var reply in host.SendAsync(id => new Generate(id, model, turns, 120, RequestPriority.Voice, Thinks: false), StallLimit))
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

        await using var speaking = await host.OpenStreamAsync(id => new Speak(id, [voice], 1f, RequestPriority.Voice), StallLimit);
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
    private static async Task<Thinking> ThinkAsync(AiHostClient host, ModelSpec model, CancellationToken stop)
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
                await foreach (var reply in host.SendAsync(id => new Generate(id, model, turns, most, RequestPriority.Background, tools, thinks), StallLimit, stop))
                    if (reply is GenerationEnded ended) tokens += ended.GeneratedTokens;
                calls++;
            }
            catch (OperationCanceledException) { break; }
            catch (AiHostException) { lost++; }
        }
        return new Thinking(calls, tokens, lost, watch.Elapsed);
    }

    private sealed record Setup(string Name, AiHostClient Voice, AiHostClient? Thinker, ModelSpec VoiceModel, ModelSpec? ThinkingModel);

    private async Task MeasureAsync(Setup setup, VoiceSpec voice)
    {
        using var gpu = GpuMemoryWatch.Start();
        using var stop = new CancellationTokenSource();
        var thinking = setup.Thinker is { } thinker && setup.ThinkingModel is { } model ? ThinkAsync(thinker, model, stop.Token) : null;
        if (thinking is not null) await Task.Delay(TimeSpan.FromSeconds(10));

        var turns = new List<VoiceTurn>();
        await SpeakAsync(setup.Voice, setup.VoiceModel, voice, "Say hello.");   // warms the voice path
        foreach (var question in Questions)
        {
            turns.Add(await SpeakAsync(setup.Voice, setup.VoiceModel, voice, question));
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
    }

    [TestMethod]
    public async Task Voice_BesideThinkingNobodyWatches_InSeparateHostsAndInOne()
    {
        var people = People();
        var aria = Gguf("aria/model", AriaGguf(), 16_384, 25_600);
        var voice = Voice();

        await using (var alone = await HostOnTheCardAsync())
        {
            await LoadAsync(alone, people);
            await LoadAsync(alone, voice.Model);
            await MeasureAsync(new Setup("voice alone", alone, null, people, null), voice);
        }

        await using (var nexaflow = await HostOnTheCardAsync())
        await using (var thinker = await HostOnTheCardAsync())
        {
            await LoadAsync(nexaflow, people);
            await LoadAsync(nexaflow, voice.Model);
            await LoadAsync(thinker, aria);
            await MeasureAsync(new Setup("separate hosts, own models", nexaflow, thinker, people, aria), voice);
        }

        await using (var shared = await HostOnTheCardAsync())
        {
            await LoadAsync(shared, people);
            await LoadAsync(shared, voice.Model);
            await LoadAsync(shared, aria);
            await MeasureAsync(new Setup("one host, own models", shared, shared, people, aria), voice);
        }

        await using (var shared = await HostOnTheCardAsync())
        {
            await LoadAsync(shared, people);
            await LoadAsync(shared, voice.Model);
            await MeasureAsync(new Setup("one host, one model", shared, shared, people, people), voice);
        }
    }

    // ── What appending buys the background app ────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ABackgroundAppsConversation_ReadAppended_AgainstReadWholeEachCall()
    {
        var aria = Gguf("aria/model", AriaGguf(), 16_384, 25_600);
        await using var host = await HostOnTheCardAsync();
        await LoadAsync(host, aria);

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
                await foreach (var reply in host.SendAsync(id => new Generate(id, aria, turns, 2048, RequestPriority.Background, AriaTools, Thinks: false), StallLimit))
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
        var aria = Gguf("aria/model", AriaGguf(), 16_384, 25_600);
        await using var host = await HostOnTheCardAsync();
        await LoadAsync(host, aria);

        var turns = new List<ChatTurn> { new(TurnRole.System, "Be brief."), new(TurnRole.User, "A train leaves at 14:10 and arrives at 16:45. How long is it?") };
        var (text, reasoning) = (new StringBuilder(), new StringBuilder());
        await foreach (var reply in host.SendAsync(id => new Generate(id, aria, turns, 2048, RequestPriority.Background, SendsReasoning: true), StallLimit))
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
        await foreach (var reply in host.SendAsync(id => new Generate(id, aria, turns, 2048, RequestPriority.Background), StallLimit))
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
