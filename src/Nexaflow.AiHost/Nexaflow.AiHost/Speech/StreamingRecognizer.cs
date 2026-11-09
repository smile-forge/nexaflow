using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Speech.Spectra;

namespace Nexaflow.AiHost.Speech;

/// <summary>
/// A cache-aware streaming transducer (Nemotron's speech models) on ONNX Runtime: audio in 560 ms chunks, words out as each
/// chunk is decoded — what shows as the speaker talks, and what confirms an interruption is speech. Each chunk's NeMo log-mel
/// features, behind the last frames of the chunk before, go through the encoder with the caches its last run handed back,
/// which stay where the model runs; its frames are then decoded greedily by the prediction network and the joint, up to a
/// limit of tokens a frame, as NeMo's greedy RNN-T decoding does. Each utterance gets its own stream; the model is shared.
/// Its sessions are on the GPU its spec names, so it runs on any of them.
/// </summary>
internal sealed partial class StreamingRecognizer : IHostModel
{
    private readonly NemotronExport _export;
    private readonly InferenceSession _encoder;
    private readonly InferenceSession _prediction;
    private readonly InferenceSession _joint;
    private readonly UnigramPieces _pieces;
    private readonly OrtMemoryInfo _onModel;

    public StreamingRecognizer(ModelSpec spec, NemotronExport export, InferenceSession encoder, InferenceSession prediction, InferenceSession joint,
                               UnigramPieces pieces)
    {
        Spec        = spec;
        _export     = export;
        _encoder    = encoder;
        _prediction = prediction;
        _joint      = joint;
        _pieces     = pieces;
        _onModel    = spec.Device is { } device
            ? new OrtMemoryInfo(OrtMemoryInfo.allocatorCUDA, OrtAllocatorType.DeviceAllocator, device, OrtMemType.Default)
            : OrtMemoryInfo.DefaultInstance;
    }

    public ModelSpec Spec { get; }

    /// <summary>Nemotron's encoder takes the language as a one-hot prompt; the export carries no table, so these are
    /// the indices from NVIDIA's processor config. Anything else is left to the model to detect.</summary>
    private static readonly Dictionary<string, int> LanguageIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = 0,
        ["fr"] = 8,
        ["de"] = 9,
    };

    private const int AnyLanguage = 101;

    public IRecognizerStream Open(string? language, GpuScheduler gpu, RequestPriority priority)
        => new Stream(this, language is not null && LanguageIds.TryGetValue(language, out int id) ? id : AnyLanguage, gpu, priority);

    /// <summary>The model tags each sentence with its language (<c>&lt;de-DE&gt;</c>) — for English and German, not
    /// French, so the tags are dropped rather than trusted.</summary>
    [GeneratedRegex(@"\s*<[a-z]{2}-[A-Z]{2}>\s*")]
    private static partial Regex LanguageTag();

    internal static string Clean(string raw) => LanguageTag().Replace(raw, " ").Trim();

    public void Dispose()
    {
        _encoder.Dispose();
        _prediction.Dispose();
        _joint.Dispose();
        if (!ReferenceEquals(_onModel, OrtMemoryInfo.DefaultInstance)) _onModel.Dispose();
    }

    /// <summary>
    /// One utterance: its features' state, the last frames of the chunk before, the encoder's caches as its last run left
    /// them, and the prediction network's state after the last token it wrote.
    /// </summary>
    private sealed class Stream : IRecognizerStream
    {
        private readonly StreamingRecognizer _model;
        private readonly NemotronExport _export;
        private readonly GpuScheduler _gpu;
        private readonly RequestPriority _priority;
        private readonly NemoStreamingMel _mel;
        private readonly float[] _melCache;
        private readonly List<float> _pending = [];
        private readonly long[] _language;
        private OrtValue _cacheChannel;
        private OrtValue _cacheTime;
        private OrtValue _cacheLength;
        private float[] _hidden;
        private float[] _cell;
        private int _lastToken;

        /// <summary>The prediction network's output, hidden state and cell for <see cref="_lastToken"/> — null until it is run
        /// for that token: a frame that writes nothing leaves its state as it was, and so its output too.</summary>
        private (float[] Output, float[] Hidden, float[] Cell)? _predicted;

        private readonly StringBuilder _raw = new();
        private string _text = "";

        public Stream(StreamingRecognizer model, int languageId, GpuScheduler gpu, RequestPriority priority)
        {
            _model    = model;
            _export   = model._export;
            _gpu      = gpu;
            _priority = priority;
            var e = _export;
            _mel      = new NemoStreamingMel(e.Mels, e.FftSize, e.Hop, e.WindowLength, e.SampleRate, e.Preemphasis, e.LogGuard);
            _melCache = new float[e.CacheFrames * e.Mels];
            _language = [languageId];

            var encoder = e.Encoder;
            _cacheChannel = OrtValue.CreateTensorValueFromMemory(new float[encoder.Layers * encoder.LeftContext * encoder.Hidden],
                                                                 [1, encoder.Layers, encoder.LeftContext, encoder.Hidden]);
            _cacheTime    = OrtValue.CreateTensorValueFromMemory(new float[encoder.Layers * encoder.Hidden * encoder.ConvContext],
                                                                 [1, encoder.Layers, encoder.Hidden, encoder.ConvContext]);
            _cacheLength  = OrtValue.CreateTensorValueFromMemory(new long[1], [1]);
            _hidden       = new float[e.Prediction.Layers * e.Prediction.Hidden];
            _cell         = new float[e.Prediction.Layers * e.Prediction.Hidden];
            _lastToken    = e.Blank;
        }

        public async ValueTask<string?> FeedAsync(float[] samples, CancellationToken ct)
        {
            _pending.AddRange(samples);
            if (_pending.Count < _export.ChunkSamples) return null;
            while (_pending.Count >= _export.ChunkSamples)
            {
                var chunk = _pending.GetRange(0, _export.ChunkSamples).ToArray();
                _pending.RemoveRange(0, _export.ChunkSamples);
                await DecodeAsync(chunk, ct);
            }
            return Changed();
        }

        /// <summary>What is left, padded to a whole chunk with silence.</summary>
        public async ValueTask<string?> FlushAsync(CancellationToken ct)
        {
            if (_pending.Count == 0) return null;
            var chunk = new float[_export.ChunkSamples];
            _pending.CopyTo(chunk);
            _pending.Clear();
            await DecodeAsync(chunk, ct);
            return Changed();
        }

        private string? Changed()
        {
            string text = Clean(_raw.ToString());
            if (text == _text) return null;
            _text = text;
            return text;
        }

        /// <summary>One chunk on the GPU, a step of its own.</summary>
        private async Task DecodeAsync(float[] chunk, CancellationToken ct)
        {
            using (await _gpu.NextAsync(_priority, _model.Spec.Device, ct))
            {
                ct.ThrowIfCancellationRequested();
                using var encoded = Encode(chunk, out long frames);
                var hidden = _export.Encoder.Hidden;
                var all = encoded.GetTensorDataAsSpan<float>();
                for (int t = 0; t < frames; t++) DecodeFrame(all.Slice(t * hidden, hidden).ToArray());
            }
        }

        /// <summary>Runs the encoder on the chunk behind the last frames of the one before, keeping the caches it hands back.</summary>
        /// <returns>Its frames, [1, frames, hidden], on this machine.</returns>
        private OrtValue Encode(float[] chunk, out long frames)
        {
            var (features, count) = _mel.Process(chunk);
            int mels = _mel.Mels, cached = _export.CacheFrames, total = cached + count;
            var signal = new float[total * mels];
            _melCache.CopyTo(signal, 0);
            features.CopyTo(signal, _melCache.Length);
            if (count >= cached) Array.Copy(features, (count - cached) * mels, _melCache, 0, _melCache.Length);
            else
            {
                Array.Copy(_melCache, count * mels, _melCache, 0, (cached - count) * mels);
                Array.Copy(features, 0, _melCache, (cached - count) * mels, count * mels);
            }

            var e = _export.Encoder;
            var session = _model._encoder;
            using var audio = OrtValue.CreateTensorValueFromMemory(signal, [1, total, mels]);
            using var length = OrtValue.CreateTensorValueFromMemory(new long[] { total }, [1]);
            using var language = OrtValue.CreateTensorValueFromMemory(_language, [1]);
            using var binding = session.CreateIoBinding();
            binding.BindInput(e.AudioSignal, audio);
            if (e.Length is { } lengthName) binding.BindInput(lengthName, length);
            binding.BindInput(e.CacheChannel, _cacheChannel);
            binding.BindInput(e.CacheTime, _cacheTime);
            binding.BindInput(e.CacheLength, _cacheLength);
            if (e.Language is { } languageName) binding.BindInput(languageName, language);
            binding.BindOutputToDevice(e.Encoded, OrtMemoryInfo.DefaultInstance);
            binding.BindOutputToDevice(e.EncodedLength, OrtMemoryInfo.DefaultInstance);
            binding.BindOutputToDevice(e.CacheChannelNext, _model._onModel);
            binding.BindOutputToDevice(e.CacheTimeNext, _model._onModel);
            binding.BindOutputToDevice(e.CacheLengthNext, OrtMemoryInfo.DefaultInstance);
            using (var run = new RunOptions()) session.RunWithBinding(run, binding);

            OrtValue[] outputs = [.. binding.GetOutputValues()];
            _cacheChannel.Dispose();
            _cacheTime.Dispose();
            _cacheLength.Dispose();
            (_cacheChannel, _cacheTime, _cacheLength) = (outputs[2], outputs[3], outputs[4]);
            using (var encodedLength = outputs[1])
                frames = Math.Min(outputs[0].GetTensorTypeAndShape().Shape[1], encodedLength.GetTensorDataAsSpan<long>()[0]);
            return outputs[0];
        }

        /// <summary>Writes what one frame of the encoder says: tokens until the joint favours none, or the frame's limit.</summary>
        private void DecodeFrame(float[] frame)
        {
            for (int symbols = 0; symbols < _export.MaxSymbols; symbols++)
            {
                var predicted = _predicted ??= Predict();
                int token = Join(frame, predicted.Output);
                if (token == _export.Blank) return;
                (_hidden, _cell, _lastToken, _predicted) = (predicted.Hidden, predicted.Cell, token, null);
                _raw.Append(_model._pieces.Decode(token));
            }
        }

        /// <summary>The prediction network on the last token written and its state then.</summary>
        private (float[] Output, float[] Hidden, float[] Cell) Predict()
        {
            var p = _export.Prediction;
            using var targets = OrtValue.CreateTensorValueFromMemory(new long[] { _lastToken }, [1, 1]);
            using var hidden  = OrtValue.CreateTensorValueFromMemory(_hidden, [p.Layers, 1, p.Hidden]);
            using var cell    = OrtValue.CreateTensorValueFromMemory(_cell, [p.Layers, 1, p.Hidden]);
            using var run = new RunOptions();
            using var outputs = _model._prediction.Run(run, [p.Targets, p.HiddenIn, p.CellIn], [targets, hidden, cell],
                                                       [p.Output, p.HiddenOut, p.CellOut]);
            return (outputs[0].GetTensorDataAsSpan<float>().ToArray(), outputs[1].GetTensorDataAsSpan<float>().ToArray(),
                    outputs[2].GetTensorDataAsSpan<float>().ToArray());
        }

        /// <summary>The token the joint favours for an encoder frame and a prediction — the blank less its penalty.</summary>
        private int Join(float[] frame, float[] prediction)
        {
            var j = _export.Joint;
            using var encoder = OrtValue.CreateTensorValueFromMemory(frame, [1, 1, frame.Length]);
            using var decoder = OrtValue.CreateTensorValueFromMemory(prediction, [1, 1, prediction.Length]);
            using var run = new RunOptions();
            using var outputs = _model._joint.Run(run, [j.EncoderIn, j.DecoderIn], [encoder, decoder], [j.Logits]);
            var logits = outputs[0].GetTensorDataAsSpan<float>();
            int best = 0;
            float top = float.NegativeInfinity;
            for (int i = 0; i < logits.Length; i++)
            {
                float score = i == _export.Blank ? logits[i] - _export.BlankPenalty : logits[i];
                if (score > top) { top = score; best = i; }
            }
            return best;
        }

        public void Dispose()
        {
            _cacheChannel.Dispose();
            _cacheTime.Dispose();
            _cacheLength.Dispose();
        }
    }
}

/// <summary>A SentencePiece unigram vocabulary as a <c>tokenizer.json</c> lists it, read back into text: each piece as
/// written, its word marker (▁) a space.</summary>
internal sealed class UnigramPieces(IReadOnlyList<string> pieces)
{
    /// <exception cref="HostRequestException">The file is not a unigram tokenizer.</exception>
    public static UnigramPieces Read(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        var model = json.RootElement.GetProperty("model");
        if (model.GetProperty("type").GetString() != "Unigram")
            throw new HostRequestException(HostErrorKind.Invalid, $"{path} is not a unigram tokenizer.");
        return new UnigramPieces([.. model.GetProperty("vocab").EnumerateArray().Select(entry => entry[0].GetString()!)]);
    }

    public string Decode(int id) => pieces[id].Replace('▁', ' ');
}

/// <summary>
/// What a Nemotron streaming export says of itself in the config it is published with (<c>genai_config.native.json</c>,
/// onnxruntime-genai's format): its features, its chunks, its three graphs and the names of their inputs and outputs, and how
/// its frames are decoded.
/// </summary>
internal sealed record NemotronExport(int Mels, int FftSize, int Hop, int WindowLength, int SampleRate, float Preemphasis, float LogGuard,
                                      int CacheFrames, int ChunkSamples, int Blank, int MaxSymbols, float BlankPenalty,
                                      NemotronExport.EncoderGraph Encoder, NemotronExport.PredictionGraph Prediction, NemotronExport.JointGraph Joint)
{
    public const string ConfigFile = "genai_config.native.json";

    /// <param name="Length">The input the feature frames' count goes to; null where the graph takes none.</param>
    /// <param name="Language">The input the language's index goes to; null for a model that takes none.</param>
    public sealed record EncoderGraph(string File, int Layers, int Hidden, int LeftContext, int ConvContext,
                                      string AudioSignal, string? Length, string CacheChannel, string CacheTime, string CacheLength, string? Language,
                                      string Encoded, string EncodedLength, string CacheChannelNext, string CacheTimeNext, string CacheLengthNext);

    public sealed record PredictionGraph(string File, int Layers, int Hidden, string Targets, string HiddenIn, string CellIn,
                                         string Output, string HiddenOut, string CellOut);

    public sealed record JointGraph(string File, string EncoderIn, string DecoderIn, string Logits);

    /// <exception cref="HostRequestException">The folder has no config, or it leaves out what the recogniser reads.</exception>
    public static NemotronExport Read(string folder)
    {
        string path = Path.Combine(folder, ConfigFile);
        if (!File.Exists(path)) throw new HostRequestException(HostErrorKind.NotFound, $"The speech export in {folder} has no {ConfigFile}.");
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = json.RootElement;
            var model = root.GetProperty("model");
            var encoder = model.GetProperty("encoder");
            var decoder = model.GetProperty("decoder");
            var joiner  = model.GetProperty("joiner");
            int Int(JsonElement e, string key) => e.GetProperty(key).GetInt32();
            float Float(JsonElement e, string key) => e.GetProperty(key).GetSingle();
            string Name(JsonElement e, string group, string key) => e.GetProperty(group).GetProperty(key).GetString()!;
            string? Optional(JsonElement e, string group, string key)
                => e.GetProperty(group).TryGetProperty(key, out var value) && value.GetString() is { Length: > 0 } name ? name : null;
            float penalty = root.TryGetProperty("search", out var search) && search.TryGetProperty("blank_penalty", out var p) ? p.GetSingle() : 0f;

            return new NemotronExport(
                Int(model, "num_mels"), Int(model, "fft_size"), Int(model, "hop_length"), Int(model, "win_length"), Int(model, "sample_rate"),
                Float(model, "preemph"), Float(model, "log_eps"), Int(model, "pre_encode_cache_size"), Int(model, "chunk_samples"),
                Int(model, "blank_id"), Int(model, "max_symbols_per_step"), penalty,
                new EncoderGraph(encoder.GetProperty("filename").GetString()!, Int(encoder, "num_hidden_layers"), Int(encoder, "hidden_size"),
                                 Int(model, "left_context"), Int(model, "conv_context"),
                                 Name(encoder, "inputs", "audio_features"), Optional(encoder, "inputs", "input_lengths"),
                                 Name(encoder, "inputs", "cache_last_channel"), Name(encoder, "inputs", "cache_last_time"),
                                 Name(encoder, "inputs", "cache_last_channel_len"), Optional(encoder, "inputs", "lang_id"),
                                 Name(encoder, "outputs", "encoder_outputs"), Name(encoder, "outputs", "output_lengths"),
                                 Name(encoder, "outputs", "cache_last_channel_next"), Name(encoder, "outputs", "cache_last_time_next"),
                                 Name(encoder, "outputs", "cache_last_channel_len_next")),
                new PredictionGraph(decoder.GetProperty("filename").GetString()!, Int(decoder, "num_hidden_layers"), Int(decoder, "hidden_size"),
                                    Name(decoder, "inputs", "targets"), Name(decoder, "inputs", "lstm_hidden_state"), Name(decoder, "inputs", "lstm_cell_state"),
                                    Name(decoder, "outputs", "outputs"), Name(decoder, "outputs", "lstm_hidden_state"), Name(decoder, "outputs", "lstm_cell_state")),
                new JointGraph(joiner.GetProperty("filename").GetString()!, Name(joiner, "inputs", "encoder_outputs"),
                               Name(joiner, "inputs", "decoder_outputs"), Name(joiner, "outputs", "logits")));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or JsonException)
        {
            throw new HostRequestException(HostErrorKind.Invalid, $"{path} does not describe a streaming speech export this host runs: {ex.Message}");
        }
    }
}

internal sealed class StreamingRecognizerRunner : IModelRunner
{
    public string Id => "nemotron-streaming-asr";

    public IHostModel Load(ModelSpec spec)
    {
        var export = NemotronExport.Read(spec.Folder);
        var pieces = UnigramPieces.Read(Path.Combine(spec.Folder, "tokenizer.json"));
        var sessions = new List<InferenceSession>();
        try
        {
            foreach (var file in new[] { export.Encoder.File, export.Prediction.File, export.Joint.File })
                sessions.Add(GraphSessions.Open(spec with { File = file }));
            if (sessions[0].InputMetadata[export.Encoder.AudioSignal].ElementDataType != TensorElementType.Float)
                throw new HostRequestException(HostErrorKind.Invalid, $"{spec.Id}'s encoder does not read 32-bit features, which this host gives it.");
            return new StreamingRecognizer(spec, export, sessions[0], sessions[1], sessions[2], pieces);
        }
        catch
        {
            foreach (var session in sessions) session.Dispose();
            throw;
        }
    }
}
