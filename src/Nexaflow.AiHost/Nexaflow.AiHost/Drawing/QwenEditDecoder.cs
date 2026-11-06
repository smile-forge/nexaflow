using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Tiling;

namespace Nexaflow.AiHost.Drawing;

/// <summary>Qwen-Image's VAE decoder: the tokens a picture ends as, [rows·cols, 64], to its pixels, interleaved 8-bit
/// RGB, 16·rows × 16·cols.</summary>
internal sealed class QwenEditDecoder(ModelSpec spec, InferenceSession session) : IHostModel
{
    public ModelSpec Spec => spec;

    public byte[] Decode(float[] tokens, int rows, int cols)
        => Spec.TileCells == 0 ? Run(tokens, rows, cols, last: true)
           : LatentTiling.RgbOf(LatentTiling.Decode(rows, cols, Scale, Spec.TileCells,
                 (tile, last) => LatentTiling.PlanarOf(Run(LatentTiling.Interleaved(tokens, QwenEditSampler.Channels, cols, tile), tile.Height, tile.Width, last))));

    /// <summary>Pixels a latent cell decodes to, along each side.</summary>
    private const int Scale = 16;

    /// <param name="last">The picture's last run: the session gives its scratch memory back after it.</param>
    private byte[] Run(float[] tokens, int rows, int cols, bool last)
    {
        using var input = OrtValue.CreateTensorValueFromMemory(tokens, [rows, cols, QwenEditSampler.Channels]);
        using var run = last ? GraphSessions.Releasing(Spec) : new RunOptions();
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
