namespace Nexaflow.Providers.Common;

/// <summary>
/// An image model bound to a single model: a prompt in, one picture out, made as a job that takes seconds to a minute.
/// A model that takes <see cref="MaxSources"/> images works from them — to change, restyle or combine them — and one
/// that <see cref="Masks"/> redraws only the part of the first source a mask leaves transparent. How far a job has got
/// is reported through the activity manager the provider was built with. Failures surface as an
/// <see cref="LlmProviderException"/>.
/// </summary>
public interface IImageGenerationProvider : IAiProvider
{
    /// <summary>How many images a request may give it to work from; 0 when it only draws from a prompt.</summary>
    int MaxSources { get; }

    /// <summary>Whether it takes a <see cref="ImageGenerationRequest.Mask"/> over the first source.</summary>
    bool Masks { get; }

    /// <summary>What a picture may be asked for with besides its prompt and sources.</summary>
    ImageSettings Settings { get; }

    Task<GeneratedMedia> GenerateImageAsync(ImageGenerationRequest request, CancellationToken ct = default);
}

/// <param name="Prompt">What to draw, or what to change in the sources.</param>
/// <param name="Aspect">The picture's proportions: one of the model's <see cref="ImageSettings.Aspects"/>.</param>
public sealed record ImageGenerationRequest(string Prompt, MediaAspect Aspect)
{
    /// <summary>One of the model's <see cref="ImageSettings.Resolutions"/>; null for its own.</summary>
    public string? Resolution { get; init; }

    /// <summary>One of the model's <see cref="ImageSettings.Details"/>; null for its own.</summary>
    public ImageDetail? Detail { get; init; }

    /// <summary>The seed it draws from, 0 or more, for a model that <see cref="ImageSettings.Seeds"/>; null for one of its choosing.</summary>
    public int? Seed { get; init; }

    /// <summary>What to keep out of the picture, for a model that takes <see cref="ImageSettings.NegativePrompts"/>.</summary>
    public string? NegativePrompt { get; init; }

    /// <summary>Drawn on a transparent background, by a model that <see cref="ImageSettings.TransparentBackgrounds"/>.</summary>
    public bool TransparentBackground { get; init; }

    /// <summary>One of the model's <see cref="ImageSettings.Loras"/>, by its id; null for none.</summary>
    public string? Lora { get; init; }
    /// <summary>Images to work from, at most <see cref="IImageGenerationProvider.MaxSources"/>.</summary>
    public IReadOnlyList<MediaBytes> Sources { get; init; } = [];

    /// <summary>A PNG the size of the first source whose transparent part is what gets redrawn — only for a provider
    /// that <see cref="IImageGenerationProvider.Masks"/>.</summary>
    public MediaBytes? Mask { get; init; }
}

/// <summary>The proportions of a picture or a video.</summary>
public enum MediaShape
{
    Square,
    Landscape,
    Portrait,
}

/// <summary>A picture, a recording or a video as bytes, with its MIME type.</summary>
public sealed record MediaBytes(byte[] Bytes, string MimeType);

/// <param name="Media">What was made.</param>
/// <param name="Note">What the model said about it, where it says anything — the prompt it actually drew from, say.</param>
public sealed record GeneratedMedia(MediaBytes Media, string? Note = null);
