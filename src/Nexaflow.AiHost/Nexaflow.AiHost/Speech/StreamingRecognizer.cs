using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntimeGenAI;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Speech;

/// <summary>
/// A streaming recogniser onnxruntime-genai runs (Nemotron's speech models): audio in 560 ms chunks, words out as each
/// chunk is decoded — what shows as the speaker talks, and what confirms an interruption is speech. Each utterance
/// gets its own stream; the model is shared.
/// </summary>
internal sealed partial class StreamingRecognizer : IHostModel
{
    private readonly Config _config;

    public StreamingRecognizer(ModelSpec spec, Config config, Model model)
    {
        Spec    = spec;
        _config = config;
        Model   = model;
        Tokenizer = new Tokenizer(model);
    }

    public ModelSpec Spec { get; }
    public Model Model { get; }
    public Tokenizer Tokenizer { get; }

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
        Tokenizer.Dispose();
        Model.Dispose();
        _config.Dispose();
    }

    private sealed class Stream : IRecognizerStream
    {
        private readonly StreamingRecognizer _model;
        private readonly GpuScheduler _gpu;
        private readonly RequestPriority _priority;
        private readonly StreamingProcessor _processor;
        private readonly TokenizerStream _decoder;
        private readonly GeneratorParams _params;
        private readonly Generator _generator;
        private readonly StringBuilder _raw = new();
        private string _text = "";

        public Stream(StreamingRecognizer model, int languageId, GpuScheduler gpu, RequestPriority priority)
        {
            _model     = model;
            _gpu       = gpu;
            _priority  = priority;
            _processor = new StreamingProcessor(model.Model);
            _processor.SetOption("use_vad", "false");   // the session's own detector decides where speech is
            _decoder   = model.Tokenizer.CreateStream();
            _params    = new GeneratorParams(model.Model);
            _generator = new Generator(model.Model, _params);
            _generator.SetRuntimeOption("lang_id", languageId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public async ValueTask<string?> FeedAsync(float[] samples, CancellationToken ct)
        {
            using var inputs = _processor.Process(samples);
            return inputs is null ? null : await DecodeAsync(inputs, ct);
        }

        public async ValueTask<string?> FlushAsync(CancellationToken ct)
        {
            using var inputs = _processor.Flush();
            return inputs is null ? null : await DecodeAsync(inputs, ct);
        }

        /// <summary>One chunk on the GPU, a step of its own.</summary>
        private async Task<string?> DecodeAsync(NamedTensors inputs, CancellationToken ct)
        {
            using (await _gpu.NextAsync(_priority, _model.Spec.Device, ct))
            {
                ct.ThrowIfCancellationRequested();
                _generator.SetInputs(inputs);
                while (!_generator.IsDone())
                {
                    _generator.GenerateNextToken();
                    var tokens = _generator.GetNextTokens();
                    if (tokens.Length > 0) _raw.Append(_decoder.Decode(tokens[0]));
                }
            }
            string text = Clean(_raw.ToString());
            if (text == _text) return null;
            _text = text;
            return text;
        }

        public void Dispose()
        {
            _generator.Dispose();
            _params.Dispose();
            _decoder.Dispose();
            _processor.Dispose();
        }
    }
}

internal sealed class StreamingRecognizerRunner : IModelRunner
{
    public string Id => "genai-streaming-asr";

    public IHostModel Load(ModelSpec spec)
    {
        var (config, model) = GenAiConfigFile.Open(spec);
        return new StreamingRecognizer(spec, config, model);
    }
}
