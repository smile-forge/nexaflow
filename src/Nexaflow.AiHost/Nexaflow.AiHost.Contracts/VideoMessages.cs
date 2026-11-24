namespace Nexaflow.AiHost.Contracts;

/// <summary>How a model films: its family and sampler, and so the parts it has and how they are driven.</summary>
public enum FilmingMethod
{
    /// <summary>Wan2.2-TI2V: a UMT5 prompt encoder, a latent denoiser asked at each UniPC step with the prompt and with the
    /// negative prompt guidance steers away from, a causal VAE decoder, and the VAE's encoder for a picture to start from.</summary>
    Wan,

    /// <summary>A Wan2.2-TI2V denoiser distilled to a few steps (FastWan): each step predicts the clean clip, which the
    /// next noises again, freshly, to its own level; nothing is guided. Distilled from text alone, it cuts away from a
    /// picture to start from, so it takes none: the parts of <see cref="Wan"/> but the frame encoder.</summary>
    WanDistilled,
}

/// <summary>The parts a video model has, by role.</summary>
public static class FilmingParts
{
    public const string PromptEncoder = DrawingParts.PromptEncoder;
    public const string Denoiser      = DrawingParts.Denoiser;
    public const string Decoder       = DrawingParts.Decoder;

    /// <summary>What reads the picture a clip starts from into the latents the denoiser makes.</summary>
    public const string FrameEncoder = "frameEncoder";

    /// <summary>The parts a model <paramref name="method"/> films with is made of.</summary>
    public static IReadOnlyList<string> Of(FilmingMethod method) => method switch
    {
        FilmingMethod.Wan          => [PromptEncoder, Denoiser, Decoder, FrameEncoder],
        FilmingMethod.WanDistilled => [PromptEncoder, Denoiser, Decoder],
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "no video model films this way"),
    };
}

/// <summary>The stages a clip's <see cref="Progress"/> names, each counting done of total.</summary>
public static class FilmingStages
{
    /// <summary>Denoising steps.</summary>
    public const string Filming = "filming";

    /// <summary>Latent frames decoded to pictures.</summary>
    public const string Decoding = "decoding";
}

/// <summary>
/// Makes one clip from a prompt, and from a picture to start from where one is given. Each step — reading the prompt,
/// every denoising step, decoding each stretch of frames — takes the GPU at the request's priority, so speech and replies
/// get in between; each denoising step is reported as a <see cref="Progress"/> (<see cref="FilmingStages.Filming"/>),
/// then each latent frame decoded (<see cref="FilmingStages.Decoding"/>). The clip comes back as <see cref="FilmedFrames"/> as each
/// stretch of it is decoded.
/// </summary>
/// <param name="Model">The video model, as the catalog names it: the parts it films with, the method and the sampling, the host's to
/// take from its catalog.</param>
/// <param name="Width">In pixels; with <see cref="Height"/>, a size the model films at.</param>
/// <param name="Frames">How many frames: one, then a multiple of the model's frames per latent frame.</param>
/// <param name="Seed">The noise the clip starts from: the same seed, prompt and picture make the same clip.</param>
public sealed record Film(long RequestId, string Model, string Prompt, int Width, int Height, int Frames, int FramesPerSecond,
                          long Seed, RequestPriority Priority) : HostRequest(RequestId)
{
    /// <summary>The picture the clip starts from, <see cref="Width"/> × <see cref="Height"/>, interleaved 8-bit RGB row by
    /// row; null for a clip from the prompt alone.</summary>
    public SharedBytes? FirstFrame { get; init; }

    /// <summary>What to keep out of the clip besides what the model's makers keep out, for a model that is guided; null for theirs
    /// alone.</summary>
    public string? NegativePrompt { get; init; }
}

/// <summary>Frames of the clip as they are decoded, in order: interleaved 8-bit RGB, one frame after another, each the
/// <see cref="Film"/>'s size. The request completes once the last is sent; encoding the clip is whoever keeps it's, frame by frame,
/// so it is never held whole as pictures.</summary>
public sealed record FilmedFrames(long RequestId, SharedBytes Rgb) : HostReply(RequestId);
