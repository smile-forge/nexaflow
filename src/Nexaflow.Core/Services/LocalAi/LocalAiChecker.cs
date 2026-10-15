using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common.Audio;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.LocalAi;

/// <summary>
/// Checks the machine against a workspace's models of Nexaflow's own: each loaded where the setup places it and set its
/// work once — a reply, a sentence spoken and heard back, a picture drawn, a clip made — while what the host holds on
/// each GPU is sampled, and one report written, in Markdown, with the picture and the clip beside it.
/// </summary>
internal sealed class LocalAiChecker(HostModelSetup setup, IReadOnlyList<OwnedModelInUse> models, string reportsFolder)
{
    /// <summary>How often each GPU's memory is read while the check runs.</summary>
    private static readonly TimeSpan Sampling = TimeSpan.FromMilliseconds(250);

    private const string Question  = "Describe a lighthouse at dusk in three sentences.";
    private const string Sentence  = "Nexaflow is checking that this machine can speak and listen.";
    private const string Picture   = "A lighthouse at dusk, waves breaking on the rocks below, oil painting";
    private const string Clip      = "Waves break on the rocks below a lighthouse at dusk as its beam sweeps the sea";

    /// <summary>What one step of the check came to.</summary>
    private sealed record Step(string Name, bool Passed, string Detail);

    public async Task<LocalAiCheck> RunAsync(IProgress<LocalAiCheckProgress> progress, CancellationToken ct)
    {
        if (models.Count == 0) throw new InvalidOperationException("This workspace uses no model Nexaflow runs itself.");
        var stamp = DateTime.Now;
        Directory.CreateDirectory(reportsFolder);
        string name = $"local-ai-check-{stamp:yyyyMMdd-HHmmss}";

        progress.Report(new LocalAiCheckProgress(LocalAiCheckStage.Describing));
        var host = await DescribeAsync(ct);
        var steps = new List<Step>();
        using var sampling = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var peaks = host.Gpus.ToDictionary(g => g.Device, g => g.HeldBytes);
        var sampler = SampleAsync(peaks, sampling.Token);

        try
        {
            foreach (var model in models)
            {
                progress.Report(new LocalAiCheckProgress(LocalAiCheckStage.Loading, model.Name));
                steps.Add(await TimedAsync($"{model.Name} ready", async () =>
                {
                    await model.Provider.WarmupAsync(ct);
                    if (model.Catalog.LoadsOnDemand) return "it loads with each use";
                    var unplaced = model.Catalog.NeedsOf(model.ModelId).Where(n => !n.CpuOnly && setup.Current?.Of(n.Id) is null).Select(n => n.Id).ToList();
                    return unplaced.Count == 0 ? "" : throw new InvalidOperationException($"{string.Join(", ", unplaced)} found no room on this machine");
                }));
            }
            if (Serving<ILlmProvider>(ModelKind.Language) is { } chat)
            {
                progress.Report(new LocalAiCheckProgress(LocalAiCheckStage.Replying));
                steps.Add(await ReplyAsync(chat, ct));
            }
            if (Serving<ITextToSpeechProvider>(ModelKind.TextToSpeech) is { } voice)
            {
                progress.Report(new LocalAiCheckProgress(LocalAiCheckStage.Speaking));
                var (spoken, pcm) = await SpeakAsync(voice, ct);
                steps.Add(spoken);
                if (pcm is not null && Serving<ISpeechToTextProvider>(ModelKind.SpeechToText) is { } ears)
                {
                    progress.Report(new LocalAiCheckProgress(LocalAiCheckStage.Hearing));
                    steps.Add(await HearAsync(ears, pcm, ct));
                }
            }
            if (Serving<IImageGenerationProvider>(ModelKind.ImageGeneration) is { } painter)
            {
                progress.Report(new LocalAiCheckProgress(LocalAiCheckStage.Drawing));
                steps.Add(await DrawAsync(painter, Path.Combine(reportsFolder, name + ".png"), ct));
            }
            if (Serving<IVideoGenerationProvider>(ModelKind.VideoGeneration) is { } filmer)
            {
                progress.Report(new LocalAiCheckProgress(LocalAiCheckStage.Filming));
                steps.Add(await FilmAsync(filmer, Path.Combine(reportsFolder, name + ".mp4"), ct));
            }
        }
        finally
        {
            await sampling.CancelAsync();
            await sampler;
        }

        var after = await DescribeAsync(ct);
        var memory = Memory(host.Gpus, after.Gpus, peaks).ToList();
        bool passed = steps.All(s => s.Passed) && memory.All(m => m.Within);
        string summary = passed
            ? $"Every model did its work within its GPU's budget ({steps.Count} steps)."
            : string.Join(" ", steps.Where(s => !s.Passed).Select(s => $"{s.Name}: {s.Detail}.")
                                    .Concat(memory.Where(m => !m.Within).Select(m => $"GPU {m.Gpu.Device} held more than its models were expected to.")));

        string path = Path.Combine(reportsFolder, name + ".md");
        await File.WriteAllTextAsync(path, Report(stamp, host, steps, memory, passed, summary), ct);
        return new LocalAiCheck(path, passed);
    }

    /// <exception cref="InvalidOperationException">The host could not be reached.</exception>
    private async Task<HostDescription> DescribeAsync(CancellationToken ct)
    {
        try
        {
            return await setup.DescribeAsync(ct);
        }
        catch (AiHostException ex)
        {
            throw new InvalidOperationException($"The AI host could not be reached: {ex.Message}", ex);
        }
    }

    /// <summary>The provider a workspace model of <paramref name="kind"/> runs through, as the service it is.</summary>
    private T? Serving<T>(ModelKind kind) where T : class, IAiProvider
        => models.FirstOrDefault(m => m.Kind == kind)?.Provider as T;

    private static async Task<Step> TimedAsync(string name, Func<Task<string>> work)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            string detail = await work();
            return new Step(name, true, $"{Seconds(watch.Elapsed)}{(detail.Length > 0 ? " — " + detail : "")}");
        }
        catch (Exception ex) when (ex is LlmProviderException or AiHostException or InvalidOperationException)
        {
            return new Step(name, false, ex.Message);
        }
    }

    private static async Task<Step> ReplyAsync(ILlmProvider chat, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        TimeSpan? first = null;
        var text = new StringBuilder();
        try
        {
            await foreach (var e in chat.StreamAsync([new LlmMessage(LlmRole.User, Question)], ct))
                if (e is LlmTextDelta delta)
                {
                    first ??= watch.Elapsed;
                    text.Append(delta.Text);
                }
        }
        catch (LlmProviderException ex) { return new Step("Reply", false, ex.Message); }

        if (first is not { } firstWord) return new Step("Reply", false, "the model answered nothing");
        var writing = watch.Elapsed - firstWord;
        int words = text.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return new Step("Reply", true,
            $"first word after {Seconds(firstWord)} (its reasoning before it), then {words} words in {Seconds(writing)}"
            + (writing.TotalSeconds > 0 ? $" — {words / writing.TotalSeconds:0} words a second" : ""));
    }

    private static async Task<(Step Step, byte[]? Pcm)> SpeakAsync(ITextToSpeechProvider voice, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        TimeSpan? first = null;
        using var audio = new MemoryStream();
        try
        {
            await using var session = await voice.StartSessionAsync(new TextToSpeechOptions(), ct);
            await session.SpeakAsync(Sentence, ct: ct);
            await session.CompleteAsync(ct);
            await foreach (var e in session.ReadAudioAsync(ct))
                if (e is SpeechAudioChunk chunk)
                {
                    first ??= watch.Elapsed;
                    audio.Write(chunk.Pcm);
                }
        }
        catch (LlmProviderException ex) { return (new Step("Speaking", false, ex.Message), null); }

        if (first is not { } firstSound) return (new Step("Speaking", false, "no audio came"), null);
        double seconds = audio.Length / 2.0 / ITextToSpeechSession.SampleRate;
        return (new Step("Speaking", true,
            $"first sound after {Seconds(firstSound)}; {seconds:0.0} s of speech made in {Seconds(watch.Elapsed)}"), audio.ToArray());
    }

    private static async Task<Step> HearAsync(ISpeechToTextProvider ears, byte[] spoken, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var heard = new StringBuilder();
        try
        {
            await using var session = await ears.StartSessionAsync(new SpeechToTextOptions("en"), ct);
            await session.AppendAudioAsync(new PcmResampler(ITextToSpeechSession.SampleRate, ISpeechToTextSession.SampleRate).Convert(spoken), ct);
            await session.CompleteAsync(ct);
            await foreach (var e in session.ReadEventsAsync(ct))
                if (e is TranscriptFinal final) heard.Append(' ').Append(final.Text);
        }
        catch (LlmProviderException ex) { return new Step("Hearing it back", false, ex.Message); }

        static string[] Words(string text) => [.. text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                                                  .Select(w => new string([.. w.Where(char.IsLetterOrDigit)])).Where(w => w.Length > 0)];
        var said = Words(Sentence);
        var got = Words(heard.ToString()).ToHashSet();
        double share = said.Count(got.Contains) / (double)said.Length;
        return new Step("Hearing it back", share >= 0.8,
            $"\"{heard.ToString().Trim()}\" in {Seconds(watch.Elapsed)} — {share:P0} of the words");
    }

    private static async Task<Step> DrawAsync(IImageGenerationProvider painter, string path, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var made = await painter.GenerateImageAsync(new ImageGenerationRequest(Picture), ct);
            await File.WriteAllBytesAsync(path, made.Media.Bytes, ct);
        }
        catch (LlmProviderException ex) { return new Step("Picture", false, ex.Message); }
        return new Step("Picture", true, $"drawn in {Seconds(watch.Elapsed)} — ![the picture]({Path.GetFileName(path)})");
    }

    /// <summary>A clip at the shortest length the model makes, saved beside the report and linked from it.</summary>
    private static async Task<Step> FilmAsync(IVideoGenerationProvider filmer, string path, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var made = await filmer.GenerateVideoAsync(new VideoGenerationRequest(Clip, MediaShape.Landscape, filmer.Lengths[0]), ct);
            await File.WriteAllBytesAsync(path, made.Media.Bytes, ct);
        }
        catch (LlmProviderException ex) { return new Step("Clip", false, ex.Message); }
        return new Step("Clip", true, $"made in {Seconds(watch.Elapsed)} — [the clip]({Path.GetFileName(path)})");
    }

    /// <summary>Reads what the host holds on each GPU until <paramref name="ct"/> ends, keeping the most.</summary>
    private async Task SampleAsync(Dictionary<int, long> peaks, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                foreach (var gpu in (await setup.DescribeAsync(ct)).Gpus)
                    peaks[gpu.Device] = Math.Max(peaks.GetValueOrDefault(gpu.Device), gpu.HeldBytes);
                await Task.Delay(Sampling, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (AiHostException) { }   // a host lost mid-check is reported by the step it failed
    }

    /// <summary>Each GPU's memory: what the setup expects its models to hold, and the most the host held.</summary>
    private sealed record GpuMemory(HostGpu Gpu, long Expected, long Peak, long HeldAfter)
    {
        /// <summary>The host never held more than its models' estimates and the runtime's own share.</summary>
        public bool Within => Peak <= Expected + HostGpu.ReservedBytes;
    }

    private IEnumerable<GpuMemory> Memory(IReadOnlyList<HostGpu> before, IReadOnlyList<HostGpu> after, Dictionary<int, long> peaks)
    {
        var placement = setup.Current;
        var onDemand = models.Where(m => m.Catalog.LoadsOnDemand).SelectMany(m => m.Catalog.NeedsOf(m.ModelId)).ToList();
        foreach (var gpu in before)
        {
            long resident = placement?.Placed.Where(s => s.Device == gpu.Device).Sum(s => s.MemoryBytes) ?? 0;
            // A picture's largest part loads beside the residents, or in their place where they make way.
            long largest = onDemand.Select(n => placement is null ? null : ModelPlacement.PlaceOnDemand(placement, n))
                                   .Where(s => s?.Device == gpu.Device).Select(s => s!.MemoryBytes).DefaultIfEmpty(0).Max();
            // Where it makes the residents give way, they give no more than it needs: the GPU holds its budget at most.
            long expected = Math.Min(resident + largest, Math.Max(resident, gpu.BudgetBytes));
            yield return new GpuMemory(gpu, expected, peaks.GetValueOrDefault(gpu.Device), after.First(g => g.Device == gpu.Device).HeldBytes);
        }
    }

    private string Report(DateTime stamp, HostDescription host, IReadOnlyList<Step> steps, IReadOnlyList<GpuMemory> memory, bool passed, string summary)
    {
        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture, $"# Local AI check — {Environment.MachineName}");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"{stamp:yyyy-MM-dd HH:mm} · {(passed ? "**Passed**" : "**Not passed**")} — {summary}");
        md.AppendLine();
        md.AppendLine("## Machine");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"- AI host runtime: ONNX Runtime {host.RuntimeVersion}, {(host.RuntimePack is null ? "CPU only" : "CUDA pack")}");
        if (host.Gpus.Count == 0) md.AppendLine("- No GPU: every model runs on the CPU.");
        foreach (var gpu in host.Gpus)
            md.AppendLine(CultureInfo.InvariantCulture,
                $"- GPU {gpu.Device}: {gpu.Name}{(gpu.BusId is { } bus ? $" ({bus})" : "")} — {Gb(gpu.TotalBytes)}, {Gb(gpu.BudgetBytes + HostGpu.ReservedBytes)} free when the host started, a budget of {Gb(gpu.BudgetBytes)} for models");
        md.AppendLine();
        md.AppendLine("## Setup");
        md.AppendLine();
        md.AppendLine("| Model | Runs on | Holds | Context |");
        md.AppendLine("|---|---|---|---|");
        var placement = setup.Current;
        foreach (var model in models)
        {
            var parts = model.Catalog.NeedsOf(model.ModelId).Where(n => !n.CpuOnly).ToList();
            foreach (var part in parts)
            {
                var spec = model.Catalog.LoadsOnDemand
                    ? (placement is null ? null : ModelPlacement.PlaceOnDemand(placement, part))
                    : placement?.Of(part.Id);
                string where = spec is null ? "does not fit" : spec.Device is { } d ? $"GPU {d}" : "CPU";
                if (model.Catalog.LoadsOnDemand && spec is not null && placement!.Displaces(spec)) where += ", others making way";
                md.AppendLine(CultureInfo.InvariantCulture,
                    $"| {model.NameOf(part, parts.Count)} | {where} | {(spec is { Device: not null } ? Gb(spec.MemoryBytes) : "—")} | {(spec is { ContextTokens: > 0 } ? $"{spec.ContextTokens:N0} tokens" : "—")} |");
            }
        }
        md.AppendLine();
        md.AppendLine("## Work");
        md.AppendLine();
        foreach (var step in steps)
            md.AppendLine(CultureInfo.InvariantCulture, $"- {(step.Passed ? "✅" : "❌")} **{step.Name}** — {step.Detail}");
        md.AppendLine();
        md.AppendLine("## Memory");
        md.AppendLine();
        if (memory.Count == 0) md.AppendLine("Nothing runs on a GPU.");
        else
        {
            md.AppendLine("| GPU | Expected | Most held | Held after | |");
            md.AppendLine("|---|---|---|---|---|");
            foreach (var m in memory)
                md.AppendLine(CultureInfo.InvariantCulture,
                    $"| {m.Gpu.Device} | {Gb(m.Expected)} | {Gb(m.Peak)} | {Gb(m.HeldAfter)} | {(m.Within ? "within" : "over")} |");
            md.AppendLine();
            md.AppendLine(CultureInfo.InvariantCulture,
                $"*Expected* is what the setup plans its models to hold; the host's own runtime keeps up to {Gb(HostGpu.ReservedBytes)} more on each GPU it uses.");
        }
        return md.ToString();
    }

    private static string Seconds(TimeSpan span) => $"{span.TotalSeconds:0.0} s";

    private static string Gb(long bytes) => $"{bytes / (double)(1L << 30):0.0} GB";
}
