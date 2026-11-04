using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Reads a prompt the way FLUX.2 [klein]'s pipeline does: wrapped in Qwen3's chat template as a user turn with thinking
/// off, tokenised by Qwen3's byte-level BPE (its tokenizer.json, beside the graph), padded or cut to 512 tokens, and run
/// through the encoder graph — Qwen3-4B up to its 27th layer — to [512, 7680]: the outputs of layers 9, 18 and 27 side
/// by side on each row.
/// </summary>
internal sealed class KleinPromptEncoder(ModelSpec spec, InferenceSession session, Tokenizer tokenizer) : IHostModel
{
    /// <summary>Qwen3's chat template for one user turn and the start of the reply, with thinking off.</summary>
    private const string Template = "<|im_start|>user\n{0}<|im_end|>\n<|im_start|>assistant\n<think>\n\n</think>\n\n";

    /// <summary>Qwen3's &lt;|endoftext|&gt;, which pads a prompt to its 512 tokens.</summary>
    private const long Padding = 151643;

    /// <summary>The width of each row it gives.</summary>
    public const int Width = 7680;

    public ModelSpec Spec => spec;

    /// <summary>The prompt's tokens, chat template included, padded or cut to <see cref="KleinSampler.PromptTokens"/>,
    /// and the attention mask that marks which are the prompt's.</summary>
    public (long[] Ids, long[] Mask) Tokenize(string prompt)
    {
        var ids  = new long[KleinSampler.PromptTokens];
        var mask = new long[KleinSampler.PromptTokens];
        int count = 0;
        foreach (int id in tokenizer.EncodeToIds(string.Format(Template, prompt)).Take(KleinSampler.PromptTokens))
        {
            ids[count]    = id;
            mask[count++] = 1;
        }
        Array.Fill(ids, Padding, count, ids.Length - count);
        return (ids, mask);
    }

    /// <summary>The encoder's reading of a prompt's tokens, [512, 7680] row-major.</summary>
    public float[] Encode((long[] Ids, long[] Mask) prompt)
    {
        using var ids  = OrtValue.CreateTensorValueFromMemory(prompt.Ids, [1, prompt.Ids.Length]);
        using var mask = OrtValue.CreateTensorValueFromMemory(prompt.Mask, [1, prompt.Mask.Length]);
        using var run = GraphSessions.Releasing(Spec);
        using var outputs = session.Run(run, ["input_ids", "attention_mask"], [ids, mask], session.OutputNames);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }

    public void Dispose() => session.Dispose();
}

internal sealed class KleinPromptEncoderRunner : IModelRunner
{
    public string Id => "klein-prompt-encoder";

    public IHostModel Load(ModelSpec spec)
    {
        string tokenizerPath = Path.Combine(spec.Folder, "tokenizer.json");
        if (!File.Exists(tokenizerPath)) throw new HostRequestException(HostErrorKind.NotFound, $"{spec.Id} has no tokenizer.json in {spec.Folder}.");
        var encoder = new KleinPromptEncoder(spec, GraphSessions.Open(spec), QwenTokenizer.FromJson(File.ReadAllText(tokenizerPath)));
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
