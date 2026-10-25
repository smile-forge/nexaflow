namespace Nexaflow.AiHost.Contracts;

/// <summary>How a model draws: its family, and so the parts it has and the sampler that drives them.</summary>
public enum DrawingMethod
{
    /// <summary>Z-Image: a prompt encoder, a latent denoiser stepped by Euler, a VAE decoder.</summary>
    ZImage,

    /// <summary>HiDream-O1: one denoiser that draws in pixels, asked with the prompt and without at each UniPC step.</summary>
    HiDream,

    /// <summary>MOSS-Image: a prompt encoder on llama.cpp, a latent denoiser asked with the prompt and with the negative
    /// prompt at each Euler step, a VAE decoder; it takes a negative prompt and a LoRA.</summary>
    MossImage,
}

/// <summary>The parts an image model has, by role.</summary>
public static class DrawingParts
{
    public const string PromptEncoder = "promptEncoder";
    public const string Denoiser      = "denoiser";
    public const string Decoder       = "decoder";

    /// <summary>The parts a model <paramref name="method"/> draws with is made of.</summary>
    public static IReadOnlyList<string> Of(DrawingMethod method) => method switch
    {
        DrawingMethod.ZImage    => [PromptEncoder, Denoiser, Decoder],
        DrawingMethod.HiDream   => [Denoiser],
        DrawingMethod.MossImage => [PromptEncoder, Denoiser, Decoder],
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "no image model draws this way"),
    };
}

/// <summary>An image model's parts by role (<see cref="DrawingParts"/>), the method that draws with them, and how it samples.</summary>
/// <param name="Steps">How many denoising steps a picture takes.</param>
/// <param name="Shift">The flow-matching shift applied to the noise levels the steps run through.</param>
/// <param name="Guidance">How far each step steers away from the empty prompt; 0 for a model that does not.</param>
public sealed record DrawingModel(DrawingMethod Method, IReadOnlyDictionary<string, ModelSpec> Parts, int Steps, float Shift, float Guidance);

/// <summary>
/// Draws one picture from a prompt. Each step — reading the prompt, every denoising step, decoding — takes the GPU at
/// the request's priority, so speech and replies get in between; each denoising step is reported as a
/// <see cref="Progress"/> (stage "drawing", done of total). The picture comes back as one <see cref="Drawn"/>.
/// </summary>
/// <param name="Width">In pixels; with <see cref="Height"/>, a size the model draws at.</param>
/// <param name="Seed">The noise the picture starts from: the same seed and prompt draw the same picture.</param>
/// <param name="NegativePrompt">What to keep out of the picture, for a model that is told; null for none.</param>
/// <param name="Lora">The LoRA it is drawn with, for a model that takes one; null for none.</param>
public sealed record Draw(long RequestId, DrawingModel Model, string Prompt, int Width, int Height, long Seed,
                          RequestPriority Priority, string? NegativePrompt = null, DrawingLora? Lora = null) : HostRequest(RequestId);

/// <summary>
/// A LoRA a picture is drawn with: an ONNX Runtime adapter for its model's denoiser, applied unmerged at a strength, and
/// — for a distillation — the sampler it brings in place of its model's own.
/// </summary>
/// <param name="Adapter">The adapter file, on this machine.</param>
/// <param name="Strength">How much of it is added; 1 as its makers trained it.</param>
/// <param name="Sampler">A distillation's own sampler, its file on this machine; null for a LoRA drawn with its model's
/// sampler.</param>
public sealed record DrawingLora(string Adapter, float Strength, string? Sampler = null);

/// <summary>The picture, as a PNG.</summary>
public sealed record Drawn(long RequestId, byte[] Png) : HostReply(RequestId);

/// <summary>Resident models that made way on their GPU for this picture or clip — unloaded until the last one is done,
/// when they load again. Until then a request for one waits.</summary>
public sealed record MadeWay(long RequestId, IReadOnlyList<string> Models) : HostReply(RequestId);
