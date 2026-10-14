using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntimeGenAI;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;
using Nexaflow.IO.Common.Audio;
using System.Text.Json;

namespace Nexaflow.AiHost.Speech;

/// <summary>
/// Whisper on onnxruntime-genai: a finished utterance transcribed whole, once — the text that counts, which hears the
/// short answers a streaming recogniser drops ("No.", "Nein.", "Stopp."). The language is forced through the prompt
/// when it is known: left to detect it, Whisper heard "Nein." as "9.". The encoder reads a 30 s window, so a longer
/// utterance is transcribed in pieces cut at its quietest moments.
/// </summary>
internal sealed partial class WhisperTranscriber : IHostModel
{
    private const int MaxTokens = 448;
    private const int Rate = ListenSession.SampleRate;
    /// <summary>A piece is at most this long, inside the encoder's 30 s window.</summary>
    private const int PieceSamples = 28 * Rate;
    /// <summary>A long utterance is cut at its quietest 100 ms in the last this-many samples of a piece.</summary>
    private const int CutSearchSamples = 8 * Rate;
    private const int CutWindowSamples = Rate / 10;

    /// <summary>The smallest vocabulary of a multilingual Whisper — Whisper's own test: an English-only model's is one
    /// token shorter, and it takes no language or task in its prompt.</summary>
    internal const int MultilingualVocabulary = 51865;

    private readonly Config _config;
    private readonly MultiModalProcessor _processor;
    private readonly SemaphoreSlim _processing = new(1, 1);
    private readonly bool _multilingual;

    public WhisperTranscriber(ModelSpec spec, Config config, Model model, bool multilingual)
    {
        Spec          = spec;
        _config       = config;
        Model         = model;
        _multilingual = multilingual;
        _processor    = new MultiModalProcessor(model);
    }

    public ModelSpec Spec { get; }
    public Model Model { get; }

    /// <summary>The decoder's start. A multilingual model has the language and task forced, or only the start token, to let
    /// it detect the language; an English-only one knows neither, and is told only to leave out timestamps.</summary>
    internal static string Prompt(string? language, bool multilingual) => !multilingual
        ? "<|startoftranscript|><|notimestamps|>"
        : language is { Length: 2 }
            ? $"<|startoftranscript|><|{language.ToLowerInvariant()}|><|transcribe|><|notimestamps|>"
            : "<|startoftranscript|>";

    [GeneratedRegex(@"<\|[^|>]*\|>")]
    private static partial Regex SpecialToken();

    internal static string Clean(string raw) => Regex.Replace(SpecialToken().Replace(raw, " "), @"\s+", " ").Trim();

    public IUtteranceTranscriber For(string? language, GpuScheduler gpu, RequestPriority priority) => new Scheduled(this, language, gpu, priority);

    public async Task<string> TranscribeAsync(float[] audio, string? language, GpuScheduler? gpu, RequestPriority priority, CancellationToken ct)
    {
        var pieces = new List<string>();
        foreach (var (start, length) in Pieces(audio))
            pieces.Add(await TranscribePieceAsync(audio.AsMemory(start, length), language, gpu, priority, ct));
        return string.Join(" ", pieces.Where(p => p.Length > 0));
    }

    private async Task<string> TranscribePieceAsync(ReadOnlyMemory<float> audio, string? language, GpuScheduler? gpu, RequestPriority priority,
                                                   CancellationToken ct)
    {
        using var audios = Audios.Load(WavAudio.Write(Pcm16.FromSamples(audio.Span), Rate));
        NamedTensors inputs;
        await _processing.WaitAsync(ct);
        try { inputs = _processor.ProcessAudios(Prompt(language, _multilingual), audios); }
        finally { _processing.Release(); }

        using (inputs)
        using (var parameters = new GeneratorParams(Model))
        {
            parameters.SetSearchOption("max_length", MaxTokens);
            parameters.SetSearchOption("do_sample", false);
            using var generator = new Generator(Model, parameters);
            using (await Step(gpu, priority, ct)) generator.SetInputs(inputs);   // the encoder, ~11 ms on CUDA
            while (!generator.IsDone())
                using (await Step(gpu, priority, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    generator.GenerateNextToken();
                }
            return Clean(_processor.Decode(generator.GetSequence(0)));
        }
    }

    private async ValueTask<IDisposable?> Step(GpuScheduler? gpu, RequestPriority priority, CancellationToken ct)
        => gpu is null ? null : await gpu.NextAsync(priority, Spec.Device, ct);

    /// <summary>Where a long utterance is cut: pieces of at most 28 s, each ending at the quietest 100 ms of its last 8 s.</summary>
    internal static IEnumerable<(int Start, int Length)> Pieces(float[] audio)
    {
        int start = 0;
        while (audio.Length - start > PieceSamples)
        {
            int end = start + PieceSamples, best = end;
            double quietest = double.MaxValue;
            for (int at = end - CutSearchSamples; at + CutWindowSamples <= end; at += CutWindowSamples / 2)
            {
                double energy = 0;
                for (int i = at; i < at + CutWindowSamples; i++) energy += audio[i] * audio[i];
                if (energy < quietest) { quietest = energy; best = at + CutWindowSamples / 2; }
            }
            yield return (start, best - start);
            start = best;
        }
        if (audio.Length > start) yield return (start, audio.Length - start);
    }

    public void Dispose()
    {
        _processor.Dispose();
        Model.Dispose();
        _config.Dispose();
        _processing.Dispose();
    }

    private sealed class Scheduled(WhisperTranscriber model, string? language, GpuScheduler gpu, RequestPriority priority) : IUtteranceTranscriber
    {
        public Task<string> TranscribeAsync(float[] audio, CancellationToken ct) => model.TranscribeAsync(audio, language, gpu, priority, ct);
    }
}

internal sealed class WhisperTranscriberRunner : IModelRunner
{
    public string Id => "genai-whisper";

    /// <summary>Loads it and transcribes a second of silence once: the encoder's first run costs most of a second,
    /// which belongs at load, not on somebody's first turn.</summary>
    public IHostModel Load(ModelSpec spec)
    {
        var (config, model) = GenAiConfigFile.Open(spec);
        var whisper = new WhisperTranscriber(spec, config, model, VocabularyOf(spec.Folder) >= WhisperTranscriber.MultilingualVocabulary);
        try
        {
            whisper.TranscribeAsync(new float[ListenSession.SampleRate], "en", gpu: null, RequestPriority.Background, CancellationToken.None)
                   .GetAwaiter().GetResult();
            return whisper;
        }
        catch
        {
            whisper.Dispose();
            throw;
        }
    }

    /// <summary>The model's vocabulary size, as its genai_config.json gives it.</summary>
    private static int VocabularyOf(string folder)
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "genai_config.json")));
        return json.RootElement.GetProperty("model").GetProperty("vocab_size").GetInt32();
    }
}
