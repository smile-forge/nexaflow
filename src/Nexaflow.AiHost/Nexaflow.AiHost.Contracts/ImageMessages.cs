namespace Nexaflow.AiHost.Contracts;

/// <summary>
/// An image model's three parts and how it samples: the prompt encoder (its tokenizer beside it), the denoiser the
/// sampler steps through, and the decoder that turns the latents into pixels.
/// </summary>
/// <param name="Steps">How many denoising steps a picture takes.</param>
/// <param name="Shift">The flow-matching shift applied to the noise levels the steps run through.</param>
public sealed record DrawingModel(ModelSpec PromptEncoder, ModelSpec Denoiser, ModelSpec Decoder, int Steps, float Shift);

/// <summary>
/// Draws one picture from a prompt. Each step — reading the prompt, every denoising step, decoding — takes the GPU at
/// the request's priority, so speech and replies get in between; each denoising step is reported as a
/// <see cref="Progress"/> (stage "drawing", done of total). The picture comes back as one <see cref="Drawn"/>.
/// </summary>
/// <param name="Width">In pixels; with <see cref="Height"/>, a size the model draws at.</param>
/// <param name="Seed">The noise the picture starts from: the same seed and prompt draw the same picture.</param>
public sealed record Draw(long RequestId, DrawingModel Model, string Prompt, int Width, int Height, long Seed,
                          RequestPriority Priority) : HostRequest(RequestId);

/// <summary>The picture, as a PNG.</summary>
public sealed record Drawn(long RequestId, byte[] Png) : HostReply(RequestId);
