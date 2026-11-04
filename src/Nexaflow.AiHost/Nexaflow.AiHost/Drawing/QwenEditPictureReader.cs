using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Qwen-Image-Edit's picture reader, Qwen2.5-VL's vision tower: a picture to work from in, at its processor size
/// (<see cref="QwenEditSampler.ProcessorSize"/>); out, the tokens that stand in the prompt where its placeholders are,
/// [tokens, 3584], one per 2 × 2 group of its patches.
/// </summary>
internal sealed class QwenEditPictureReader(ModelSpec spec, InferenceSession session) : IHostModel
{
    /// <summary>The width of a vision token: the language model's.</summary>
    public const int TokenWidth = 3584;

    private static readonly string[] Inputs = ["patches", "patch_positions", "window_order", "windows"];

    public ModelSpec Spec => spec;

    /// <summary>The picture's vision tokens, and how many there are.</summary>
    public (float[] Embeds, int Count) Read(byte[] rgb, int width, int height)
    {
        int rows = height / QwenEditSampler.ReaderPatch, cols = width / QwenEditSampler.ReaderPatch, count = rows * cols;
        var patches = QwenEditSampler.ReaderPatches(rgb, width, height);
        var (positions, order, windows) = QwenEditSampler.ReaderLayout(rows, cols);
        using var patchValue = OrtValue.CreateTensorValueFromMemory(patches, [count, QwenEditSampler.ReaderPatchWidth]);
        using var positionValue = OrtValue.CreateTensorValueFromMemory(positions, [count, 2]);
        using var orderValue = OrtValue.CreateTensorValueFromMemory(order, [order.Length]);
        using var windowValue = OrtValue.CreateTensorValueFromMemory(windows, [count]);
        using var run = GraphSessions.Releasing(Spec);
        using var outputs = session.Run(run, Inputs, [patchValue, positionValue, orderValue, windowValue], session.OutputNames);
        return (outputs[0].GetTensorDataAsSpan<float>().ToArray(), order.Length);
    }

    public void Dispose() => session.Dispose();
}

internal sealed class QwenEditPictureReaderRunner : IModelRunner
{
    public string Id => "qwen-edit-picture-reader";

    public IHostModel Load(ModelSpec spec)
    {
        var reader = new QwenEditPictureReader(spec, GraphSessions.Open(spec));
        try
        {
            reader.Read(new byte[392 * 392 * 3], 392, 392);   // tune the kernels at load
            return reader;
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }
}
