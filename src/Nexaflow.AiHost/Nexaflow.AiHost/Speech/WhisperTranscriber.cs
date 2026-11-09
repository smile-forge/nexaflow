using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Speech.Spectra;

namespace Nexaflow.AiHost.Speech;

/// <summary>
/// Whisper on ONNX Runtime: a finished utterance transcribed whole, once — the text that counts, which hears the short
/// answers a streaming recogniser drops ("No.", "Nein.", "Stopp."). The language is forced through the prompt when it is
/// known: left to detect it, Whisper heard "Nein." as "9.". The encoder reads a 30 s window, so a longer utterance is
/// transcribed in pieces cut at its quietest moments. Each piece's log-mel spectrogram is encoded once into every decoder
/// layer's cross-attention keys and values, which stay where the model runs; the decoder then writes greedily, a token a
/// step, carrying its own keys and values there from step to step. Its sessions are on the GPU its spec names, so it runs on
/// any of them.
/// </summary>
internal sealed partial class WhisperTranscriber : IHostModel
{
    private const int Rate = ListenSession.SampleRate;
    /// <summary>A piece is at most this long, inside the encoder's 30 s window.</summary>
    private const int PieceSamples = 28 * Rate;
    /// <summary>A long utterance is cut at its quietest 100 ms in the last this-many samples of a piece.</summary>
    private const int CutSearchSamples = 8 * Rate;
    private const int CutWindowSamples = Rate / 10;

    /// <summary>The smallest vocabulary of a multilingual Whisper — Whisper's own test: an English-only model's is one
    /// token shorter, and it takes no language or task in its prompt.</summary>
    internal const int MultilingualVocabulary = 51865;

    private readonly WhisperExport _export;
    private readonly InferenceSession _encoder;
    private readonly InferenceSession _decoder;
    private readonly BpeTokenizer _tokenizer;
    private readonly WhisperLogMel _features;
    private readonly OrtMemoryInfo _onModel;
    private readonly bool _multilingual;

    public WhisperTranscriber(ModelSpec spec, WhisperExport export, InferenceSession encoder, InferenceSession decoder, BpeTokenizer tokenizer)
    {
        Spec          = spec;
        _export       = export;
        _encoder      = encoder;
        _decoder      = decoder;
        _tokenizer    = tokenizer;
        _features     = new WhisperLogMel((int)encoder.InputMetadata[export.AudioFeatures].Dimensions[1]);
        _multilingual = export.Vocabulary >= MultilingualVocabulary;
        _onModel      = spec.Device is { } device
            ? new OrtMemoryInfo(OrtMemoryInfo.allocatorCUDA, OrtAllocatorType.DeviceAllocator, device, OrtMemType.Default)
            : OrtMemoryInfo.DefaultInstance;
    }

    public ModelSpec Spec { get; }

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

    /// <summary>One piece: encoded once, then written a token a step until the end of text or the decoder's longest sequence.</summary>
    private async Task<string> TranscribePieceAsync(ReadOnlyMemory<float> audio, string? language, GpuScheduler? gpu, RequestPriority priority,
                                                   CancellationToken ct)
    {
        var features = _features.Compute(audio.Span);
        var prompt = _tokenizer.EncodeToIds(Prompt(language, _multilingual)).ToArray();
        var written = new List<int>();

        using var decoding = new Decoding(this);
        using (await Step(gpu, priority, ct)) decoding.Encode(features);   // the encoder, ~11 ms on CUDA
        int next;
        using (await Step(gpu, priority, ct)) next = decoding.Next(prompt);
        while (next != _export.EndOfText)
        {
            written.Add(next);
            if (prompt.Length + written.Count >= _export.MaxLength) break;
            using (await Step(gpu, priority, ct))
            {
                ct.ThrowIfCancellationRequested();
                next = decoding.Next([next]);
            }
        }
        return Clean(_tokenizer.Decode(written, considerSpecialTokens: false));
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
        _encoder.Dispose();
        _decoder.Dispose();
        if (!ReferenceEquals(_onModel, OrtMemoryInfo.DefaultInstance)) _onModel.Dispose();
    }

    /// <summary>
    /// One piece's decoding: the cross-attention keys and values its encoding gave every decoder layer, and the decoder's own
    /// keys and values so far — all held where the model runs, handed from one run to the next without leaving it.
    /// </summary>
    private sealed class Decoding(WhisperTranscriber model) : IDisposable
    {
        private readonly WhisperExport _export = model._export;
        private OrtValue[] _cross = [];
        private OrtValue[] _past = [];

        public void Encode(float[] features)
        {
            var encoder = model._encoder;
            using var input = Tensor(encoder.InputMetadata[_export.AudioFeatures].ElementDataType, features, [1, model._features.Mels, WhisperLogMel.Frames]);
            using var binding = encoder.CreateIoBinding();
            binding.BindInput(_export.AudioFeatures, input);
            foreach (var name in _export.CrossOutputs) binding.BindOutputToDevice(name, model._onModel);
            using (var run = new RunOptions()) encoder.RunWithBinding(run, binding);
            _cross = Owned(binding);

            // The decoder's own keys and values start empty: [1, heads, 0, head size] each.
            _past = [.. _export.SelfPasts.Select(name => model._decoder.InputMetadata[name])
                                         .Select(meta => OrtValue.CreateAllocatedTensorValue(OrtAllocator.DefaultInstance, meta.ElementDataType,
                                                                                             [1, meta.Dimensions[1], 0, meta.Dimensions[3]]))];
        }

        /// <summary>Runs the decoder on <paramref name="tokens"/> after what it has read, and gives the token it writes next.</summary>
        public int Next(int[] tokens)
        {
            var decoder = model._decoder;
            using var ids = Ids(decoder.InputMetadata[_export.InputIds].ElementDataType, tokens);
            using var binding = decoder.CreateIoBinding();
            binding.BindInput(_export.InputIds, ids);
            for (int i = 0; i < _past.Length; i++) binding.BindInput(_export.SelfPasts[i], _past[i]);
            for (int i = 0; i < _cross.Length; i++) binding.BindInput(_export.CrossPasts[i], _cross[i]);
            binding.BindOutputToDevice(_export.Logits, OrtMemoryInfo.DefaultInstance);
            foreach (var name in _export.SelfPresents) binding.BindOutputToDevice(name, model._onModel);
            using (var run = new RunOptions()) decoder.RunWithBinding(run, binding);

            var outputs = Owned(binding);
            using (var logits = outputs[0])
            {
                foreach (var value in _past) value.Dispose();
                _past = outputs[1..];
                return LastArgMax(logits);
            }
        }

        /// <summary>The binding's outputs, each now this decoding's to dispose.</summary>
        private static OrtValue[] Owned(OrtIoBinding binding) => [.. binding.GetOutputValues()];

        /// <summary>The token the last position's logits favour.</summary>
        private static int LastArgMax(OrtValue logits)
        {
            var shape = logits.GetTensorTypeAndShape();
            int vocabulary = (int)shape.Shape[^1];
            int last = (int)(shape.ElementCount - vocabulary);
            int best = 0;
            if (shape.ElementDataType == TensorElementType.Float16)
            {
                var row = logits.GetTensorDataAsSpan<Float16>().Slice(last, vocabulary);
                float top = float.NegativeInfinity;
                for (int i = 0; i < row.Length; i++)
                    if ((float)row[i] > top) { top = (float)row[i]; best = i; }
            }
            else
            {
                var row = logits.GetTensorDataAsSpan<float>().Slice(last, vocabulary);
                float top = float.NegativeInfinity;
                for (int i = 0; i < row.Length; i++)
                    if (row[i] > top) { top = row[i]; best = i; }
            }
            return best;
        }

        private static OrtValue Tensor(TensorElementType type, float[] values, long[] shape) => type switch
        {
            TensorElementType.Float   => OrtValue.CreateTensorValueFromMemory(values, shape),
            TensorElementType.Float16 => OrtValue.CreateTensorValueFromMemory(Array.ConvertAll(values, v => (Float16)v), shape),
            _ => throw new HostRequestException(HostErrorKind.Invalid, $"A Whisper encoder reading {type} features is not one this host runs."),
        };

        private static OrtValue Ids(TensorElementType type, int[] tokens) => type switch
        {
            TensorElementType.Int32 => OrtValue.CreateTensorValueFromMemory(tokens, [1, tokens.Length]),
            TensorElementType.Int64 => OrtValue.CreateTensorValueFromMemory(Array.ConvertAll(tokens, t => (long)t), [1, tokens.Length]),
            _ => throw new HostRequestException(HostErrorKind.Invalid, $"A Whisper decoder reading {type} token ids is not one this host runs."),
        };

        public void Dispose()
        {
            foreach (var value in _cross) value.Dispose();
            foreach (var value in _past) value.Dispose();
        }
    }

    private sealed class Scheduled(WhisperTranscriber model, string? language, GpuScheduler gpu, RequestPriority priority) : IUtteranceTranscriber
    {
        public Task<string> TranscribeAsync(float[] audio, CancellationToken ct) => model.TranscribeAsync(audio, language, gpu, priority, ct);
    }
}

/// <summary>
/// What a Whisper export says of itself in the config it is published with (<c>genai_config.native.json</c>, onnxruntime-genai's
/// format): its graphs, the names of their inputs and outputs layer by layer, its vocabulary and end of text, and the longest
/// sequence its decoder writes.
/// </summary>
internal sealed record WhisperExport(string EncoderFile, string DecoderFile, string AudioFeatures, string InputIds, string Logits,
                                     IReadOnlyList<string> CrossOutputs, IReadOnlyList<string> CrossPasts,
                                     IReadOnlyList<string> SelfPasts, IReadOnlyList<string> SelfPresents,
                                     int Vocabulary, int EndOfText, int MaxLength)
{
    public const string ConfigFile = "genai_config.native.json";

    /// <exception cref="HostRequestException">The folder has no config, or it leaves out what the transcriber reads.</exception>
    public static WhisperExport Read(string folder)
    {
        string path = Path.Combine(folder, ConfigFile);
        if (!File.Exists(path)) throw new HostRequestException(HostErrorKind.NotFound, $"The Whisper export in {folder} has no {ConfigFile}.");
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            var model   = json.RootElement.GetProperty("model");
            var encoder = model.GetProperty("encoder");
            var decoder = model.GetProperty("decoder");
            int layers  = decoder.GetProperty("num_hidden_layers").GetInt32();
            string Name(JsonElement part, string group, string key) => part.GetProperty(group).GetProperty(key).GetString()!;
            IReadOnlyList<string> PerLayer(JsonElement part, string group, string key)
                => [.. Enumerable.Range(0, layers).Select(i => Name(part, group, key).Replace("%d", i.ToString(System.Globalization.CultureInfo.InvariantCulture)))];
            IReadOnlyList<string> KeysThenValues(JsonElement part, string group, string keys, string values)
                => [.. PerLayer(part, group, keys), .. PerLayer(part, group, values)];

            return new WhisperExport(
                encoder.GetProperty("filename").GetString()!,
                decoder.GetProperty("filename").GetString()!,
                Name(encoder, "inputs", "audio_features"),
                Name(decoder, "inputs", "input_ids"),
                Name(decoder, "outputs", "logits"),
                KeysThenValues(encoder, "outputs", "cross_present_key_names", "cross_present_value_names"),
                KeysThenValues(decoder, "inputs", "cross_past_key_names", "cross_past_value_names"),
                KeysThenValues(decoder, "inputs", "past_key_names", "past_value_names"),
                KeysThenValues(decoder, "outputs", "present_key_names", "present_value_names"),
                model.GetProperty("vocab_size").GetInt32(),
                model.GetProperty("eos_token_id").GetInt32(),
                json.RootElement.GetProperty("search").GetProperty("max_length").GetInt32());
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or JsonException)
        {
            throw new HostRequestException(HostErrorKind.Invalid, $"{path} does not describe a Whisper export this host runs: {ex.Message}");
        }
    }
}

internal sealed class WhisperTranscriberRunner : IModelRunner
{
    public string Id => "whisper";

    /// <summary>Loads it and transcribes a second of silence once: the encoder's first run costs most of a second,
    /// which belongs at load, not on somebody's first turn.</summary>
    public IHostModel Load(ModelSpec spec)
    {
        var export = WhisperExport.Read(spec.Folder);
        var tokenizer = ByteLevelBpe.FromJson(File.ReadAllText(Path.Combine(spec.Folder, "tokenizer.json")));
        InferenceSession? encoder = null, decoder = null;
        WhisperTranscriber? whisper = null;
        try
        {
            encoder = GraphSessions.Open(spec with { File = export.EncoderFile });
            decoder = GraphSessions.Open(spec with { File = export.DecoderFile });
            whisper = new WhisperTranscriber(spec, export, encoder, decoder, tokenizer);
            whisper.TranscribeAsync(new float[ListenSession.SampleRate], "en", gpu: null, RequestPriority.Background, CancellationToken.None)
                   .GetAwaiter().GetResult();
            return whisper;
        }
        catch
        {
            if (whisper is not null) whisper.Dispose();
            else
            {
                encoder?.Dispose();
                decoder?.Dispose();
            }
            throw;
        }
    }
}
