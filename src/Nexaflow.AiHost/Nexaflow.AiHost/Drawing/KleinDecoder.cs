using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Tiling;

namespace Nexaflow.AiHost.Drawing;

/// <summary>The tokens a picture ends as, [rows·cols, 128], to its pixels, [3, 16·rows, 16·cols] in [0, 1].</summary>
internal sealed class KleinDecoder(ModelSpec spec, InferenceSession session) : IHostModel
{
    public ModelSpec Spec => spec;

    public float[] Decode(ReadOnlySpan<float> tokens, int rows, int cols)
    {
        var planes = KleinSampler.Planes(tokens, rows, cols);
        return Spec.TileCells == 0 ? Run(planes, rows, cols, last: true)
             : LatentTiling.Decode(rows, cols, Scale, Spec.TileCells,
                                   (tile, last) => Run(LatentTiling.Planar(planes, KleinSampler.Channels, rows, cols, tile), tile.Height, tile.Width, last));
    }

    /// <summary>Pixels a latent cell decodes to, along each side.</summary>
    private const int Scale = 16;

    /// <param name="last">The picture's last run: the session gives its scratch memory back after it.</param>
    private float[] Run(float[] planes, int rows, int cols, bool last)
    {
        using var input = OrtValue.CreateTensorValueFromMemory(planes, [1, KleinSampler.Channels, rows, cols]);
        using var run = last ? GraphSessions.Releasing(Spec) : new RunOptions();
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
