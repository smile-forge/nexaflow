using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Tiling;

namespace Nexaflow.AiHost.Drawing;

/// <summary>Qwen-Image's VAE decoder: the tokens a picture ends as, [rows·cols, 64], to its pixels, interleaved 8-bit
/// RGB, 16·rows × 16·cols.</summary>
internal sealed class QwenEditDecoder(ModelSpec spec, InferenceSession session) : IHostModel
{
    public ModelSpec Spec => spec;

    public SharedBytes Decode(float[] tokens, int rows, int cols)
    {
        var picture = RequestSections.Create(rows * Scale * cols * Scale * 3);
        if (Spec.TileCells == 0)
            Run(tokens, rows, cols, last: true, picture.Span);
        else
            LatentTiling.RgbOf(LatentTiling.Decode(rows, cols, Scale, Spec.TileCells, (tile, last) =>
            {
                var rgb = new byte[tile.Height * Scale * tile.Width * Scale * 3];
                Run(LatentTiling.Interleaved(tokens, QwenEditSampler.Channels, cols, tile), tile.Height, tile.Width, last, rgb);
                return LatentTiling.PlanarOf(rgb);
            }), picture.Span);
        return picture;
    }

    /// <summary>Pixels a latent cell decodes to, along each side.</summary>
    private const int Scale = 16;

    /// <param name="last">The picture's last run: the session gives its scratch memory back after it.</param>
    /// <param name="into">Where its pixels go: 16·rows × 16·cols, interleaved 8-bit RGB.</param>
    private void Run(float[] tokens, int rows, int cols, bool last, Span<byte> into)
    {
        using var input = OrtValue.CreateTensorValueFromMemory(tokens, [rows, cols, QwenEditSampler.Channels]);
        using var run = last ? GraphSessions.Releasing(Spec) : new RunOptions();
        using var outputs = session.Run(run, ["tokens"], [input], session.OutputNames);
        outputs[0].GetTensorDataAsSpan<byte>().CopyTo(into);
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
