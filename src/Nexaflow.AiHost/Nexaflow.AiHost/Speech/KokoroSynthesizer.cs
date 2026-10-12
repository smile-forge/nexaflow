using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Speech;

/// <summary>
/// Kokoro (82M, Apache-2.0): phonemes in, 24 kHz speech out, in the voice whose style table it is given — 20 times
/// faster than real time on CUDA, 11 on the CPU. A voice is a table of styles, one row per input length; a fine-tune
/// for another language is another model with the same inputs.
/// </summary>
internal sealed class KokoroSynthesizer(ModelSpec spec, InferenceSession session, IReadOnlyDictionary<string, long> vocabulary) : IHostModel
{
    public const int SampleRate = 24_000;

    /// <summary>The longest input the model reads, less the pad either side.</summary>
    internal const int MaxTokens = 510;

    private const int StyleWidth = 256;

    private readonly ConcurrentDictionary<string, float[]> _voices = new(StringComparer.OrdinalIgnoreCase);

    public ModelSpec Spec { get; } = spec;

    /// <summary>A voice's style table, read once.</summary>
    /// <exception cref="HostRequestException">The voice's file is not there.</exception>
    public float[] Voice(string file) => _voices.GetOrAdd(file, f =>
    {
        string path = Path.Combine(Spec.Folder, f.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) throw new HostRequestException(HostErrorKind.NotFound, $"The voice {f} is not there: {path}.");
        byte[] bytes = File.ReadAllBytes(path);
        var table = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, table, 0, table.Length * sizeof(float));
        return table;
    });

    /// <summary>
    /// The phonemes as the model's input ids, in pieces it can read whole: cut at the last pause — punctuation, then a
    /// space — before <see cref="MaxTokens"/>. Symbols outside the vocabulary are dropped.
    /// </summary>
    public IReadOnlyList<long[]> Tokenize(string phonemes)
    {
        var ids = new List<long>();
        var pauses = new List<(int After, bool Punctuation)>();
        foreach (var rune in phonemes.EnumerateRunes())
        {
            if (!vocabulary.TryGetValue(rune.ToString(), out long id)) continue;
            ids.Add(id);
            if (rune.Value == ' ') pauses.Add((ids.Count, false));
            else if (rune.Value is ',' or '.' or ';' or ':' or '!' or '?' or '—') pauses.Add((ids.Count, true));
        }

        var pieces = new List<long[]>();
        int start = 0;
        while (ids.Count - start > MaxTokens)
        {
            int limit = start + MaxTokens;
            var within = pauses.Where(p => p.After > start && p.After <= limit).ToList();
            int end = within.LastOrDefault(p => p.Punctuation).After is > 0 and var strong ? strong
                    : within.Count > 0 ? within[^1].After
                    : limit;
            pieces.Add([.. ids.GetRange(start, end - start)]);
            start = end;
        }
        if (ids.Count > start) pieces.Add([.. ids.GetRange(start, ids.Count - start)]);
        return pieces;
    }
    /// <summary>One piece, spoken. Thread-safe: concurrent requests share the session.</summary>
    public float[] Synthesize(long[] ids, float[] voice, float speed)
    {
        var input = new long[ids.Length + 2];   // the pad token, 0, either side
        ids.CopyTo(input, 1);
        int row = Math.Min(ids.Length, voice.Length / StyleWidth - 1);
        var style = voice.AsSpan(row * StyleWidth, StyleWidth).ToArray();
        using var results = session.Run(
        [
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(input, [1, input.Length])),
            NamedOnnxValue.CreateFromTensor("style", new DenseTensor<float>(style, [1, StyleWidth])),
            NamedOnnxValue.CreateFromTensor("speed", new DenseTensor<float>(new[] { speed }, [1])),
        ]);
        return results.First(r => r.Name == "waveform").AsTensor<float>().ToArray();
    }

    public void Dispose() => session.Dispose();
}

internal sealed class KokoroSynthesizerRunner : IModelRunner
{
    public string Id => "kokoro";

    /// <summary>Kokoro on the GPU where the host runs models there — it shares the CUDA stack — else on the CPU.</summary>
    public IHostModel Load(ModelSpec spec, string executionProvider)
    {
        string path = Path.Combine(spec.Folder, spec.File ?? throw new HostRequestException(HostErrorKind.Invalid, $"{spec.Id} does not name its file."));
        string tokenizer = Path.Combine(spec.Folder, "tokenizer.json");
        if (!File.Exists(path) || !File.Exists(tokenizer))
            throw new HostRequestException(HostErrorKind.NotFound, $"{spec.Id}'s files are not all there in {spec.Folder}.");

        using var vocab = JsonDocument.Parse(File.ReadAllText(tokenizer));
        var vocabulary = vocab.RootElement.GetProperty("model").GetProperty("vocab").EnumerateObject()
                              .ToDictionary(p => p.Name, p => p.Value.GetInt64(), StringComparer.Ordinal);

        using var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel       = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };
        if (executionProvider == ExecutionRuntime.Cuda)
        {
            using var cuda = new OrtCUDAProviderOptions();
            cuda.UpdateOptions(new Dictionary<string, string> { ["device_id"] = "0", ["arena_extend_strategy"] = "kSameAsRequested" });
            options.AppendExecutionProvider_CUDA(cuda);
        }
        else
        {
            options.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2);
        }

        var synthesizer = new KokoroSynthesizer(spec, new InferenceSession(path, options), vocabulary);
        try
        {
            // The first run on CUDA tunes its kernels — a cost for load, not for somebody's first sentence.
            synthesizer.Synthesize([.. synthesizer.Tokenize("hɛlˈoʊ").SelectMany(p => p)], new float[(KokoroSynthesizer.MaxTokens + 1) * 256], 1f);
            return synthesizer;
        }
        catch
        {
            synthesizer.Dispose();
            throw;
        }
    }
}
