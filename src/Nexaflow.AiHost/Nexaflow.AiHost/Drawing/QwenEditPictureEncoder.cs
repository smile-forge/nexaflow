using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>Qwen-Image's VAE encoder for a picture to work from: interleaved 8-bit RGB in, at its read size; out, its
/// latents as the denoiser reads them, [rows·cols, 64] — the encoder's mean, normalised, in 2 × 2 patches.</summary>
internal sealed class QwenEditPictureEncoder(ModelSpec spec, InferenceSession session) : IHostModel
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

internal sealed class QwenEditPictureEncoderRunner : IModelRunner
{
    public string Id => "qwen-edit-picture-encoder";

    public IHostModel Load(ModelSpec spec)
    {
        var encoder = new QwenEditPictureEncoder(spec, GraphSessions.Open(spec));
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
