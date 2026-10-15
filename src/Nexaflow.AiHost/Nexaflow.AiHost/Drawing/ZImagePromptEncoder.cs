using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Reads a prompt the way Z-Image's pipeline does: wrapped in Qwen3's chat template as a user turn, tokenised by Qwen3's
/// byte-level BPE (its tokenizer.json, beside the graph), and run through the encoder graph — Qwen3-4B up to its 35th
/// layer — to [tokens, 2560].
/// </summary>
internal sealed class ZImagePromptEncoder(ModelSpec spec, InferenceSession session, Tokenizer tokenizer) : IHostModel
{
    /// <summary>The longest prompt read, in tokens — the pipeline's own limit.</summary>
    public const int MaxTokens = 512;

    /// <summary>Qwen3's chat template for one user turn and the start of the reply, thinking left on as the pipeline asks.</summary>
    private const string Template = "<|im_start|>user\n{0}<|im_end|>\n<|im_start|>assistant\n";

    public ModelSpec Spec => spec;

    /// <summary>The prompt's tokens, chat template included, at most <see cref="MaxTokens"/>.</summary>
    public long[] Tokenize(string prompt) =>
        [.. tokenizer.EncodeToIds(string.Format(Template, prompt)).Take(MaxTokens).Select(id => (long)id)];

    /// <summary>The encoder's reading of the tokens, [tokens, width] row-major.</summary>
    public (float[] Caption, int Rows, int Width) Encode(long[] ids)
    {
        using var input = OrtValue.CreateTensorValueFromMemory(ids, [1, ids.Length]);
        using var run = GraphSessions.Releasing(Spec);
        using var outputs = session.Run(run, ["input_ids"], [input], session.OutputNames);
        var shape = outputs[0].GetTensorTypeAndShape().Shape;
        return (outputs[0].GetTensorDataAsSpan<float>().ToArray(), (int)shape[1], (int)shape[2]);
    }

    public void Dispose() => session.Dispose();
}

internal sealed class ZImagePromptEncoderRunner : IModelRunner
{
    public string Id => "z-image-prompt-encoder";

    public IHostModel Load(ModelSpec spec)
    {
        string tokenizerPath = Path.Combine(spec.Folder, "tokenizer.json");
        if (!File.Exists(tokenizerPath)) throw new HostRequestException(HostErrorKind.NotFound, $"{spec.Id} has no tokenizer.json in {spec.Folder}.");
        var tokenizer = QwenTokenizer.FromJson(File.ReadAllText(tokenizerPath));
        var encoder = new ZImagePromptEncoder(spec, GraphSessions.Open(spec), tokenizer);
        try
        {
            encoder.Encode(encoder.Tokenize("warm up"));   // CUDA tunes its kernels here, not on somebody's first picture
            return encoder;
        }
        catch
        {
            encoder.Dispose();
            throw;
        }
    }
}
