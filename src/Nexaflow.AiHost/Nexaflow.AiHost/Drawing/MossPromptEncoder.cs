using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Llama;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Reads a prompt the way MOSS-Image's pipeline does: in Qwen3.5's chat template as one user turn and the start of the
/// reply, thinking on, tokenised by the GGUF's own vocabulary, and read whole by Qwen3.5-9B on llama.cpp — its last
/// hidden state, every token's row, [tokens, 4096]. The GGUF is Qwen3.5-9B unchanged, so it is an existing llama.cpp
/// build of it rather than a graph of Nexaflow's (tools/ai-models/moss-image, Compare-MossEncoder.ps1).
/// </summary>
internal sealed class MossPromptEncoder(ModelSpec spec, LlamaModel model, LlamaHiddenStates states) : IHostModel
{
    /// <summary>The longest prompt read, in tokens, template included — the pipeline's own limit.</summary>
    public const int MaxTokens = 4096;

    /// <summary>Qwen3.5's chat template for one user turn and the start of the reply, thinking on, as the pipeline asks.</summary>
    private const string Template = "<|im_start|>user\n{0}<|im_end|>\n<|im_start|>assistant\n<think>\n";

    public ModelSpec Spec => spec;

    /// <summary>The prompt's tokens, chat template included, at most <see cref="MaxTokens"/>: a longer one is cut at its
    /// end, as the pipeline cuts it.</summary>
    public int[] Tokenize(string prompt)
    {
        var tokens = model.Tokenize(string.Format(Template, prompt));
        return tokens.Length > MaxTokens ? tokens[..MaxTokens] : tokens;
    }

    /// <summary>The model's last hidden state over the tokens: the rows, how many, and their width.</summary>
    public (float[] Caption, int Rows, int Width) Encode(int[] tokens) => (states.Read(tokens), tokens.Length, states.Width);

    public void Dispose()
    {
        states.Dispose();
        model.Dispose();
    }
}

internal sealed class MossPromptEncoderRunner : IModelRunner
{
    public string Id => "moss-image-prompt-encoder";

    public IHostModel Load(ModelSpec spec)
    {
        if (spec.File is null) throw new HostRequestException(HostErrorKind.Invalid, $"{spec.Id} names no GGUF to load.");
        IntPtr? device = spec.Device is { } number ? LlamaBackends.Gpus[number].Device : null;
        var model = LlamaModel.Load(Path.Combine(spec.Folder, spec.File), device);
        LlamaHiddenStates? states = null;
        try
        {
            states = new LlamaHiddenStates(model, MossPromptEncoder.MaxTokens);
            var encoder = new MossPromptEncoder(spec, model, states);
            encoder.Encode(encoder.Tokenize("warm up"));   // CUDA tunes its kernels here, not on somebody's first picture
            return encoder;
        }
        catch
        {
            states?.Dispose();
            model.Dispose();
            throw;
        }
    }
}
