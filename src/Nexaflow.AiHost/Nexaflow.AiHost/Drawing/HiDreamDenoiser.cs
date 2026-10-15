using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// HiDream-O1's denoiser: one call predicts the clean picture from a noisy one at a timestep, for one prompt. A prompt is
/// read on its first call, which hands back the keys and values every layer made of it; each later call takes those
/// instead, and reads only the timestep and the picture (<see cref="HiDreamPrompt"/>). Its tokenizer is Qwen's, its
/// tokenizer.json beside the graph.
/// </summary>
internal sealed class HiDreamDenoiser(ModelSpec spec, InferenceSession session, Tokenizer tokenizer) : IHostModel
{
    public const int Layers = 36, KeyHeads = 8, HeadWidth = 128;

    /// <summary>The prompt as HiDream writes it: Qwen's chat template for one user turn and the reply's start, then the
    /// begin-image token. The timestep token that follows is the denoiser's own.</summary>
    private const string Template = "<|im_start|>user\n{0}<|im_end|>\n<|im_start|>assistant\n<|boi_token|>";

    private static readonly string[] Inputs = ["input_ids", "image_tokens", "image_positions", "timestep", "past_keys", "past_values"];

    public ModelSpec Spec => spec;

    /// <summary><paramref name="prompt"/> ready for its first call.</summary>
    public HiDreamPrompt Read(string prompt) => new(PromptIds(tokenizer, prompt));

    /// <summary>The tokens <paramref name="prompt"/> is read as: the template around it, by Qwen's tokenizer.</summary>
    public static long[] PromptIds(Tokenizer tokenizer, string prompt) =>
        [.. tokenizer.EncodeToIds(string.Format(Template, prompt)).Select(id => (long)id)];

    /// <summary>
    /// The clean picture predicted from <paramref name="tokens"/> at <paramref name="timestep"/>, for
    /// <paramref name="prompt"/> — its tokens read on this call if they are not yet, its keys and values kept for the next.
    /// </summary>
    /// <param name="tokens">The picture, [patches, 3072], as <see cref="HiDreamSampler"/> lays it out.</param>
    /// <param name="imagePositions">The patches' positions, [3, patches] (<see cref="HiDreamSampler.ImagePositions"/>).</param>
    /// <param name="timestep">1 − t/1000 for the step's timestep t.</param>
    /// <param name="lastStep">The picture's last call: the session gives its scratch memory back after it.</param>
    public float[] Predict(HiDreamPrompt prompt, float[] tokens, long[] imagePositions, float timestep, bool lastStep)
    {
        int count = tokens.Length / HiDreamSampler.TokenWidth;
        long[] unread = prompt.IsRead ? [] : prompt.Ids;
        using var ids = Tensor(unread, [1, unread.Length]);
        using var image = OrtValue.CreateTensorValueFromMemory(tokens, [1, count, HiDreamSampler.TokenWidth]);
        using var positions = OrtValue.CreateTensorValueFromMemory(imagePositions, [3, count]);
        using var step = OrtValue.CreateTensorValueFromMemory(new[] { timestep }, [1]);
        long[] cacheShape = [Layers, KeyHeads, prompt.Cached, HeadWidth];
        using var keys = Tensor(prompt.Keys, cacheShape);
        using var values = Tensor(prompt.Values, cacheShape);
        using var run = lastStep ? GraphSessions.Releasing(Spec) : new RunOptions();
        using var outputs = session.Run(run, Inputs, [ids, image, positions, step, keys, values], session.OutputNames);
        if (!prompt.IsRead)
            prompt.Keep(outputs[1].GetTensorDataAsSpan<Float16>().ToArray(), outputs[2].GetTensorDataAsSpan<Float16>().ToArray(), unread.Length);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }

    /// <summary>A tensor over <paramref name="data"/>; one with nothing in it allocated, as there is no memory to point at.</summary>
    private static OrtValue Tensor<T>(T[] data, long[] shape) where T : unmanaged =>
        data.Length > 0
            ? OrtValue.CreateTensorValueFromMemory(data, shape)
            : OrtValue.CreateAllocatedTensorValue(OrtAllocator.DefaultInstance, TensorBase.GetTypeInfo(typeof(T)).ElementType, shape);

    public void Dispose() => session.Dispose();
}

/// <summary>
/// One prompt's place in HiDream's denoiser — the one a picture is drawn from, or the empty one guidance steers away
/// from: its tokens, and once the denoiser has read them, the keys and values each layer made of them, the same at every
/// step.
/// </summary>
internal sealed class HiDreamPrompt(long[] ids)
{
    public long[] Ids => ids;

    /// <summary>The prompt's keys, [36, 8, cached, 128]; none until it is read.</summary>
    public Float16[] Keys { get; private set; } = [];

    /// <summary>The prompt's values, laid out as <see cref="Keys"/>.</summary>
    public Float16[] Values { get; private set; } = [];

    /// <summary>How many of its tokens the keys and values hold.</summary>
    public int Cached { get; private set; }

    public bool IsRead => Cached > 0;

    public void Keep(Float16[] keys, Float16[] values, int tokens)
    {
        Keys   = keys;
        Values = values;
        Cached = tokens;
    }
}

internal sealed class HiDreamDenoiserRunner : IModelRunner
{
    public string Id => "hidream-denoiser";

    public IHostModel Load(ModelSpec spec)
    {
        string tokenizerPath = Path.Combine(spec.Folder, "tokenizer.json");
        if (!File.Exists(tokenizerPath)) throw new HostRequestException(HostErrorKind.NotFound, $"{spec.Id} has no tokenizer.json in {spec.Folder}.");
        var denoiser = new HiDreamDenoiser(spec, GraphSessions.Open(spec), QwenTokenizer.FromJson(File.ReadAllText(tokenizerPath)));
        try
        {
            // One call at the square size, so CUDA tunes its kernels at load rather than on somebody's first picture.
            const int side = 2048 / HiDreamSampler.Patch;
            denoiser.Predict(denoiser.Read(" "), new float[side * side * HiDreamSampler.TokenWidth], HiDreamSampler.ImagePositions(side, side),
                             0.5f, lastStep: true);
            return denoiser;
        }
        catch
        {
            denoiser.Dispose();
            throw;
        }
    }
}
