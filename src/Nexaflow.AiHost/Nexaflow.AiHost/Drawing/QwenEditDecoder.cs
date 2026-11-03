using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>Qwen-Image's VAE decoder: the tokens a picture ends as, [rows·cols, 64], to its pixels, interleaved 8-bit
/// RGB, 16·rows × 16·cols.</summary>
internal sealed class QwenEditDecoder(ModelSpec spec, InferenceSession session) : IHostModel
{
    public ModelSpec Spec => spec;

    public byte[] Decode(float[] tokens, int rows, int cols)
    {
        using var input = OrtValue.CreateTensorValueFromMemory(tokens, [rows, cols, QwenEditSampler.Channels]);
        using var run = GraphSessions.Releasing(Spec);
        using var outputs = session.Run(run, ["tokens"], [input], session.OutputNames);
        return outputs[0].GetTensorDataAsSpan<byte>().ToArray();
    }

    public void Dispose() => session.Dispose();
}

internal sealed class QwenEditDecoderRunner : IModelRunner
{
    public string Id => "qwen-edit-decoder";

    public IHostModel Load(ModelSpec spec)
    {
        var decoder = new QwenEditDecoder(spec, GraphSessions.Open(spec));
        try
        {
            decoder.Decode(new float[4 * 4 * QwenEditSampler.Channels], 4, 4);   // tune the kernels at load
            return decoder;
        }
        catch
        {
            decoder.Dispose();
            throw;
        }
    }
}
