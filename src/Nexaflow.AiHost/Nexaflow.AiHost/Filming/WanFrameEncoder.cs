using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Filming;

/// <summary>
/// Wan2.2's VAE encoder for the picture a clip starts from: interleaved 8-bit RGB in, at the clip's size; out, its latent
/// frame as the denoiser reads it, [48, 1, rows, cols] — the encoder's mean, normalised as the clip's latents are.
/// </summary>
internal sealed class WanFrameEncoder(ModelSpec spec, InferenceSession session) : IHostModel
{
    public ModelSpec Spec => spec;

    public float[] Encode(byte[] rgb, int width, int height)
    {
        if (rgb.Length != width * height * 3)
            throw new HostRequestException(HostErrorKind.Invalid, $"A {width}x{height} picture is {width * height * 3} bytes of RGB, not {rgb.Length}.");
        using var input = OrtValue.CreateTensorValueFromMemory(rgb, [height, width, 3]);
        using var run = GraphSessions.Releasing(Spec);
        using var outputs = session.Run(run, ["rgb"], [input], session.OutputNames);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }

    public void Dispose() => session.Dispose();
}

internal sealed class WanFrameEncoderRunner : IModelRunner
{
    public string Id => "wan-frame-encoder";

    public IHostModel Load(ModelSpec spec)
    {
        var encoder = new WanFrameEncoder(spec, GraphSessions.Open(spec));
        try
        {
            encoder.Encode(new byte[64 * 64 * 3], 64, 64);   // tune the kernels at load
            return encoder;
        }
        catch
        {
            encoder.Dispose();
            throw;
        }
    }
}
