using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>The tokens a picture ends as, [rows·cols, 128], to its pixels, [3, 16·rows, 16·cols] in [0, 1].</summary>
internal sealed class KleinDecoder(ModelSpec spec, InferenceSession session) : IHostModel
{
    public ModelSpec Spec => spec;

    public float[] Decode(ReadOnlySpan<float> tokens, int rows, int cols)
    {
        using var input = OrtValue.CreateTensorValueFromMemory(KleinSampler.Planes(tokens, rows, cols), [1, KleinSampler.Channels, rows, cols]);
        using var run = GraphSessions.Releasing(Spec);
        using var outputs = session.Run(run, ["latents"], [input], session.OutputNames);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }

    public void Dispose() => session.Dispose();
}

internal sealed class KleinDecoderRunner : IModelRunner
{
    public string Id => "klein-decoder";

    public IHostModel Load(ModelSpec spec)
    {
        var decoder = new KleinDecoder(spec, GraphSessions.Open(spec));
        try
        {
            decoder.Decode(new float[KleinSampler.Channels * 4 * 4], 4, 4);   // tune the kernels at load
            return decoder;
        }
        catch
        {
            decoder.Dispose();
            throw;
        }
    }
}
