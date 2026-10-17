using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Erasing;

/// <summary>
/// big-lama: planar RGB from 0 to 1 and a mask, 1 where filled, both a multiple of 8 each way, in; the picture with the
/// mask filled from what is round it out — LaMa's predict around its generator.
/// </summary>
internal sealed class PictureInpainter(ModelSpec spec, InferenceSession session) : IHostModel
{
    public ModelSpec Spec => spec;

    /// <param name="lastCall">The picture's last region: the session gives its scratch back after it.</param>
    public float[] Inpaint(float[] rgb, float[] mask, int width, int height, bool lastCall)
    {
        if (width % 8 != 0 || height % 8 != 0)
            throw new HostRequestException(HostErrorKind.Invalid, $"The inpainter takes multiples of 8 each way, not {width}x{height}.");
        if (rgb.Length != 3 * width * height || mask.Length != width * height)
            throw new HostRequestException(HostErrorKind.Invalid, $"A {width}x{height} region is {3 * width * height} values of planar RGB and {width * height} of mask.");
        using var picture = OrtValue.CreateTensorValueFromMemory(rgb, [1, 3, height, width]);
        using var hole = OrtValue.CreateTensorValueFromMemory(mask, [1, 1, height, width]);
        using var run = lastCall ? GraphSessions.Releasing(Spec) : new RunOptions();
        using var outputs = session.Run(run, ["rgb", "mask"], [picture, hole], session.OutputNames);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }

    public void Dispose() => session.Dispose();
}

internal sealed class PictureInpainterRunner : IModelRunner
{
    public string Id => "lama-inpainter";

    public IHostModel Load(ModelSpec spec)
    {
        var session = GraphSessions.Open(spec);
        var inpainter = new PictureInpainter(spec, session);
        try
        {
            // A 64-pixel square, half of it filled, tunes the kernels at load.
            var mask = new float[64 * 64];
            Array.Fill(mask, 1f, 0, mask.Length / 2);
            inpainter.Inpaint(new float[3 * 64 * 64], mask, 64, 64, lastCall: true);
            return inpainter;
        }
        catch
        {
            inpainter.Dispose();
            throw;
        }
    }
}
