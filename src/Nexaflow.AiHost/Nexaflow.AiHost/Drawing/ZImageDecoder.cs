using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Tiling;

namespace Nexaflow.AiHost.Drawing;

/// <summary>The latents a picture ends as, [16, h, w], to its pixels, [3, 8h, 8w] in [0, 1].</summary>
internal sealed class ZImageDecoder(ModelSpec spec, InferenceSession session) : IHostModel
{
    public ModelSpec Spec => spec;

    public float[] Decode(float[] latents, int latentHeight, int latentWidth)
        => Spec.TileCells == 0 ? Run(latents, latentHeight, latentWidth, last: true)
           : LatentTiling.Decode(latentHeight, latentWidth, Scale, Spec.TileCells,
                                 (tile, last) => Run(LatentTiling.Planar(latents, ZImageSampler.Channels, latentHeight, latentWidth, tile), tile.Height, tile.Width, last));

    /// <summary>Pixels a latent cell decodes to, along each side.</summary>
    private const int Scale = 8;

    /// <param name="last">The picture's last run: the session gives its scratch memory back after it.</param>
    private float[] Run(float[] latents, int latentHeight, int latentWidth, bool last)
    {
        using var input = OrtValue.CreateTensorValueFromMemory(latents, [1, ZImageSampler.Channels, latentHeight, latentWidth]);
        using var run = last ? GraphSessions.Releasing(Spec) : new RunOptions();
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
        var decoder = new ZImageDecoder(spec, GraphSessions.Open(spec));
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
