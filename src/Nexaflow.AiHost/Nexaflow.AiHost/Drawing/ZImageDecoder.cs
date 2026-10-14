using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>The latents a picture ends as, [16, h, w], to its pixels, [3, 8h, 8w] in [0, 1].</summary>
internal sealed class ZImageDecoder(ModelSpec spec, InferenceSession session) : IHostModel
{
    public ModelSpec Spec => spec;

    public float[] Decode(float[] latents, int latentHeight, int latentWidth)
    {
        using var input = OrtValue.CreateTensorValueFromMemory(latents, [1, ZImageSampler.Channels, latentHeight, latentWidth]);
        using var run = ImageSessions.Releasing(Spec);
        using var outputs = session.Run(run, ["latents"], [input], session.OutputNames);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }

    public void Dispose() => session.Dispose();
}

internal sealed class ZImageDecoderRunner : IModelRunner
{
    public string Id => "z-image-decoder";

    public IHostModel Load(ModelSpec spec)
    {
        var decoder = new ZImageDecoder(spec, ImageSessions.Open(spec));
        try
        {
            decoder.Decode(new float[ZImageSampler.Channels * 32 * 32], 32, 32);   // tune the kernels at load
            return decoder;
        }
        catch
        {
            decoder.Dispose();
            throw;
        }
    }
}
