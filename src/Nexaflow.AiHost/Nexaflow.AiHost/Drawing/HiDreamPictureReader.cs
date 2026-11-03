using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// HiDream-O1's picture reader, Qwen3-VL's vision tower: a picture to work from in, at a size whole groups of its patches
/// (<see cref="HiDreamSampler.ConditionSize"/>); out, the tokens that stand in the prompt where the picture's
/// placeholders are, with the DeepStack features the denoiser adds to them after its first layers.
/// </summary>
internal sealed class HiDreamPictureReader(ModelSpec spec, InferenceSession session) : IHostModel
{
    /// <summary>The denoiser's layers each vision token's DeepStack features are added after.</summary>
    public const int DeepStackLayers = 3;

    /// <summary>The width of a vision token: the denoiser's.</summary>
    public const int TokenWidth = 4096;

    private static readonly string[] Inputs = ["patches", "embedding_index", "embedding_weights", "patch_positions"];

    public ModelSpec Spec => spec;

    public HiDreamPictureTokens Read(byte[] rgb, int width, int height)
    {
        var patches = HiDreamSampler.ReaderPatches(rgb, width, height);
        int rows = height / HiDreamSampler.ReaderPatch, cols = width / HiDreamSampler.ReaderPatch, count = rows * cols;
        var (index, weights, positions) = HiDreamSampler.ReaderSampling(rows, cols);
        using var patchValue = OrtValue.CreateTensorValueFromMemory(patches, [count, HiDreamSampler.ReaderPatchWidth]);
        using var indexValue = OrtValue.CreateTensorValueFromMemory(index, [4, count]);
        using var weightValue = OrtValue.CreateTensorValueFromMemory(weights, [4, count]);
        using var positionValue = OrtValue.CreateTensorValueFromMemory(positions, [count, 2]);
        using var run = GraphSessions.Releasing(Spec);
        using var outputs = session.Run(run, Inputs, [patchValue, indexValue, weightValue, positionValue], session.OutputNames);
        int merge = HiDreamSampler.ReaderMerge;
        return new(rows / merge, cols / merge, outputs[0].GetTensorDataAsSpan<float>().ToArray(), outputs[1].GetTensorDataAsSpan<float>().ToArray());
    }

    public void Dispose() => session.Dispose();
}

/// <summary>
/// One picture as HiDream-O1's prompt reads it: a vision token for each group of its patches, rows × cols of them, row by
/// row — their values, [tokens, 4096], and their DeepStack features, [3, tokens, 4096].
/// </summary>
internal sealed record HiDreamPictureTokens(int Rows, int Cols, float[] Embeds, float[] DeepStack)
{
    public int Count => Rows * Cols;
}

internal sealed class HiDreamPictureReaderRunner : IModelRunner
{
    public string Id => "hidream-picture-reader";

    public IHostModel Load(ModelSpec spec)
    {
        var reader = new HiDreamPictureReader(spec, GraphSessions.Open(spec));
        try
        {
            reader.Read(new byte[HiDreamSampler.ConditionSide * HiDreamSampler.ConditionSide * 3], HiDreamSampler.ConditionSide, HiDreamSampler.ConditionSide);   // tune the kernels at load
            return reader;
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }
}
