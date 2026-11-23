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

    /// <summary>FLUX.2 [klein]: a prompt encoder, a latent denoiser stepped by Euler that reads the pictures it works from
    /// beside its latents, a picture encoder for those pictures, a VAE decoder; a mask keeps the first picture where it
    /// does not cover it.</summary>
    Flux2Klein,

    /// <summary>Qwen-Image-Edit: a picture reader and a prompt encoder that read the pictures it works from into the
    /// prompt, a picture encoder for their latents, a latent denoiser asked with the prompt and without at each Euler
    /// step, a VAE decoder; it edits, drawing nothing from a prompt alone, and a mask keeps the first picture where it
    /// does not cover it.</summary>
    QwenImageEdit,
}

/// <summary>The parts an image model has, by role.</summary>
public static class DrawingParts
{
    public const string PromptEncoder = "promptEncoder";
    public const string Denoiser      = "denoiser";
    public const string Decoder       = "decoder";

    /// <summary>Reads a picture to work from into the latents the denoiser takes beside its own.</summary>
    public const string PictureEncoder = "pictureEncoder";

    /// <summary>Reads a picture to work from, with a vision tower, into the tokens that stand for it in the prompt.</summary>
    public const string PictureReader = "pictureReader";

    /// <summary>The parts a model <paramref name="method"/> draws with is made of.</summary>
    public static IReadOnlyList<string> Of(DrawingMethod method) => method switch
    {
        DrawingMethod.ZImage    => [PromptEncoder, Denoiser, Decoder],
        DrawingMethod.HiDream   => [Denoiser, PictureReader],
        DrawingMethod.MossImage => [PromptEncoder, Denoiser, Decoder],
        DrawingMethod.Flux2Klein => [PromptEncoder, Denoiser, PictureEncoder, Decoder],
        DrawingMethod.QwenImageEdit => [PictureReader, PromptEncoder, PictureEncoder, Denoiser, Decoder],
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "no image model draws this way"),
    };
}

/// <summary>What sets apart how each method samples, where its catalog entry needs to know.</summary>
public static class DrawingMethods
{
    /// <summary>Whether a model drawn <paramref name="method"/>'s way shifts its noise levels by an amount that follows the
    /// picture's size rather than by a shift of its own (its catalog's shift is 0).</summary>
    public static bool ShiftsBySize(DrawingMethod method) => method is DrawingMethod.Flux2Klein or DrawingMethod.QwenImageEdit;
}

/// <summary>
/// Draws one picture from a prompt, and from pictures to work from where its model takes them. Each step — reading the
/// prompt, reading each picture, every denoising step, decoding — takes the GPU at the request's priority, so speech and
/// replies get in between; each denoising step is reported as a <see cref="Progress"/> (stage "drawing", done of total).
/// The picture comes back as one <see cref="Drawn"/>.
/// </summary>
/// <param name="Model">The image model, as the catalog names it: the parts it draws with, the method and the sampling, the host's to
/// take from its catalog.</param>
/// <param name="Width">In pixels; with <see cref="Height"/>, a size the model draws a picture from a prompt alone at. An edit —
/// a picture made from <see cref="Sources"/> — is drawn at the size its model reads the first at, which the host decides and
/// <see cref="Drawn"/> reports, so it names none: 0 × 0.</param>
/// <param name="Seed">The noise the picture starts from: the same seed and prompt draw the same picture.</param>
/// <param name="NegativePrompt">What to keep out of the picture, for a model that is told; null for none.</param>
/// <param name="Lora">The LoRA it is drawn with, for a model that takes one; null for none.</param>
public sealed record Draw(long RequestId, string Model, string Prompt, int Width, int Height, long Seed,
                          RequestPriority Priority, string? NegativePrompt = null, DrawingLora? Lora = null) : HostRequest(RequestId)
{
    /// <summary>The pictures to work from, in order, each at the size it is: the host reads each at the size its model reads a
    /// picture of its proportions at. None to draw from the prompt alone.</summary>
    public IReadOnlyList<DrawingSource> Sources { get; init; } = [];

    /// <summary>What is drawn again over the first source, at that source's size as sent: a byte a pixel, row by row — 255 where
    /// the picture is drawn again, 0 where the first source is kept, and between them a blend; null to draw the whole
    /// picture.</summary>
    public SharedBytes? Mask { get; init; }
}

/// <summary>A picture to work from: interleaved 8-bit RGB, row by row.</summary>
public sealed record DrawingSource(int Width, int Height, SharedBytes Rgb);

/// <summary>
/// A LoRA a picture is drawn with: an ONNX Runtime adapter for its model's denoiser, applied unmerged at a strength, and
/// — for a distillation — the sampler it brings in place of its model's own. Its files are the host's to find in its store.
/// </summary>
/// <param name="Id">The LoRA, as the image model's catalog names it.</param>
/// <param name="Strength">How much of it is added; 1 as its makers trained it.</param>
public sealed record DrawingLora(string Id, float Strength);

/// <summary>The picture, <paramref name="Width"/> × <paramref name="Height"/>, interleaved 8-bit RGB row by row: encoding it is
/// whoever keeps it's.</summary>
public sealed record Drawn(long RequestId, int Width, int Height, SharedBytes Rgb) : HostReply(RequestId);
