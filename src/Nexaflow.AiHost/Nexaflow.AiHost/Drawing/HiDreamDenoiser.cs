using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// HiDream-O1's denoiser: one call predicts the clean picture from a noisy one at a timestep, for one prompt. A prompt is
/// read on its first call, which hands back the keys and values every layer made of it; each later call takes those
/// instead, and reads only the timestep and the pictures (<see cref="HiDreamPrompt"/>). An edit's prompt has the pictures
/// it works from in it, as the picture reader's tokens (<see cref="HiDreamPictureReader"/>); its tokenizer is Qwen's, its
/// tokenizer.json beside the graph.
/// </summary>
internal sealed class HiDreamDenoiser(ModelSpec spec, IGraph graph, Tokenizer tokenizer) : IHostModel
{
    public const int Layers = 36, KeyHeads = 8, HeadWidth = 128;

    /// <summary>The prompt as HiDream writes it: Qwen's chat template for one user turn and the reply's start, then the
    /// begin-image token — the turn's start, then any pictures, then the prompt and the rest. The timestep token that
    /// follows is the denoiser's own.</summary>
    private const string TurnStart = "<|im_start|>user\n", TurnEnd = "{0}<|im_end|>\n<|im_start|>assistant\n<|boi_token|>";

    private static readonly string[] Inputs =
        ["input_ids", "vision_embeds", "deepstack_embeds", "image_tokens", "prompt_positions", "image_positions", "timestep", "past_keys", "past_values"];

    public ModelSpec Spec => spec;

    /// <summary><paramref name="prompt"/> ready for its first call.</summary>
    public HiDreamPrompt Read(string prompt) => Read(prompt, []);

    /// <summary><paramref name="prompt"/> after <paramref name="pictures"/>, as an edit asks, ready for its first call.</summary>
    public HiDreamPrompt Read(string prompt, IReadOnlyList<HiDreamPictureTokens> pictures)
    {
        var ids = PromptIds(tokenizer, prompt, [.. pictures.Select(p => p.Count)]);
        return new(ids, HiDreamSampler.PromptPositions(ids, [.. pictures.Select(p => (p.Rows, p.Cols))]), pictures);
    }

    /// <summary>The tokens <paramref name="prompt"/> is read as: the template around it, by Qwen's tokenizer.</summary>
    public static long[] PromptIds(Tokenizer tokenizer, string prompt) => PromptIds(tokenizer, prompt, []);

    /// <summary>
    /// The tokens <paramref name="prompt"/> is read as after pictures, each <paramref name="pictures"/>' count of
    /// placeholders between a vision start and end — as Qwen3-VL's chat template and processor write a user's turn of
    /// pictures then text. The special tokens either side of the pictures part the text as the tokenizer would.
    /// </summary>
    public static long[] PromptIds(Tokenizer tokenizer, string prompt, IReadOnlyList<int> pictures)
    {
        if (pictures.Count == 0) return Encode(tokenizer, TurnStart + string.Format(TurnEnd, prompt));
        var ids = new List<long>(Encode(tokenizer, TurnStart));
        foreach (int count in pictures)
        {
            ids.Add(HiDreamSampler.VisionStart);
            ids.AddRange(Enumerable.Repeat(HiDreamSampler.ImageToken, count));
            ids.Add(HiDreamSampler.VisionEnd);
        }
        ids.AddRange(Encode(tokenizer, string.Format(TurnEnd, prompt)));
        return [.. ids];
    }

    private static long[] Encode(Tokenizer tokenizer, string text) => [.. tokenizer.EncodeToIds(text).Select(id => (long)id)];

    /// <summary>
    /// The clean pictures predicted from <paramref name="tokens"/> at <paramref name="timestep"/>, for
    /// <paramref name="prompt"/> — its tokens and pictures read on this call if they are not yet, its keys and values kept
    /// for the next. The picture drawn's are the first; the rest are the pictures worked from, which it reads clean.
    /// </summary>
    /// <param name="tokens">The picture drawn, then each picture worked from, [patches, 3072], as <see cref="HiDreamSampler"/>
    /// lays them out.</param>
    /// <param name="imagePositions">The patches' positions, [3, patches] (<see cref="HiDreamSampler.ImagePositions(int, int)"/>).</param>
    /// <param name="timestep">1 − t/1000 for the step's timestep t.</param>
    /// <param name="lastStep">The picture's last call: the session gives its scratch memory back after it.</param>
    public float[] Predict(HiDreamPrompt prompt, float[] tokens, long[] imagePositions, float timestep, bool lastStep)
    {
        int count = tokens.Length / HiDreamSampler.TokenWidth;
        bool reading = !prompt.IsRead;
        long[] unread = reading ? prompt.Ids : [];
        long[] promptPositions = reading ? prompt.Positions : prompt.TimestepPosition;
        int vision = reading ? prompt.VisionTokens : 0;
        using var ids = Tensor(unread, [1, unread.Length]);
        using var embeds = Tensor(reading ? prompt.Embeds : [], [vision, HiDreamPictureReader.TokenWidth]);
        using var deepStack = Tensor(reading ? prompt.DeepStack : [], [HiDreamPictureReader.DeepStackLayers, vision, HiDreamPictureReader.TokenWidth]);
        using var image = OrtValue.CreateTensorValueFromMemory(tokens, [1, count, HiDreamSampler.TokenWidth]);
        using var promptAt = OrtValue.CreateTensorValueFromMemory(promptPositions, [3, unread.Length + 1]);
        using var imageAt = OrtValue.CreateTensorValueFromMemory(imagePositions, [3, count]);
        using var step = OrtValue.CreateTensorValueFromMemory(new[] { timestep }, [1]);
        long[] cacheShape = [Layers, KeyHeads, prompt.Cached, HeadWidth];
        using var keys = Tensor(prompt.Keys, cacheShape);
        using var values = Tensor(prompt.Values, cacheShape);
        using var run = lastStep ? GraphSessions.Releasing(Spec) : new RunOptions();
        using var outputs = graph.Run(run, Inputs, [ids, embeds, deepStack, image, promptAt, imageAt, step, keys, values], graph.OutputNames);
        if (reading)
            prompt.Keep(outputs[1].GetTensorDataAsSpan<Float16>().ToArray(), outputs[2].GetTensorDataAsSpan<Float16>().ToArray(), unread.Length);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }

    /// <summary>A tensor over <paramref name="data"/>; one with nothing in it allocated, as there is no memory to point at.</summary>
    private static OrtValue Tensor<T>(T[] data, long[] shape) where T : unmanaged =>
        data.Length > 0
            ? OrtValue.CreateTensorValueFromMemory(data, shape)
            : OrtValue.CreateAllocatedTensorValue(OrtAllocator.DefaultInstance, TensorBase.GetTypeInfo(typeof(T)).ElementType, shape);

    public void Dispose() => graph.Dispose();
}

/// <summary>
/// One prompt's place in HiDream's denoiser — the one a picture is drawn from, or the empty one guidance steers away
/// from: its tokens and their positions, the tokens of any pictures in it, and once the denoiser has read them, the keys
/// and values each layer made of them, the same at every step. The pictures' tokens are let go once read.
/// </summary>
internal sealed class HiDreamPrompt
{
    public HiDreamPrompt(long[] ids, long[] positions, IReadOnlyList<HiDreamPictureTokens> pictures)
    {
        Ids = ids;
        Positions = positions;
        int at = ids.Length, width = ids.Length + 1;
        TimestepPosition = [positions[at], positions[width + at], positions[2 * width + at]];
        VisionTokens = pictures.Sum(p => p.Count);
        Embeds = [.. pictures.SelectMany(p => p.Embeds)];

        // [3, tokens, 4096]: each layer's features for every picture in turn.
        int token = HiDreamPictureReader.TokenWidth;
        DeepStack = new float[HiDreamPictureReader.DeepStackLayers * VisionTokens * token];
        for (int layer = 0, into = 0; layer < HiDreamPictureReader.DeepStackLayers; layer++)
            foreach (var picture in pictures)
            {
                int length = picture.Count * token;
                picture.DeepStack.AsSpan(layer * length, length).CopyTo(DeepStack.AsSpan(into));
                into += length;
            }
    }

    public long[] Ids { get; }

    /// <summary>The tokens' rotary positions and the timestep token's after them, [3, tokens + 1].</summary>
    public long[] Positions { get; }

    /// <summary>The timestep token's position alone, [3, 1], for a call after the prompt is read.</summary>
    public long[] TimestepPosition { get; }

    /// <summary>The pictures' vision tokens, [tokens, 4096], in the prompt's order; none once read.</summary>
    public float[] Embeds { get; private set; }

    /// <summary>Their DeepStack features, [3, tokens, 4096]; none once read.</summary>
    public float[] DeepStack { get; private set; }

    public int VisionTokens { get; }

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
        Embeds = DeepStack = [];
    }
}

internal sealed class HiDreamDenoiserRunner : IModelRunner
{
    public string Id => "hidream-denoiser";

    public IHostModel Load(ModelSpec spec)
    {
        string tokenizerPath = Path.Combine(spec.Folder, "tokenizer.json");
        if (!File.Exists(tokenizerPath)) throw new HostRequestException(HostErrorKind.NotFound, $"{spec.Id} has no tokenizer.json in {spec.Folder}.");
        var denoiser = new HiDreamDenoiser(spec, GraphSessions.OpenGraph(spec), QwenTokenizer.FromJson(File.ReadAllText(tokenizerPath)));
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
