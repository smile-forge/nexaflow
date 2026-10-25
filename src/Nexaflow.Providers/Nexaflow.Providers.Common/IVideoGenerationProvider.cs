namespace Nexaflow.Providers.Common;

/// <summary>
/// A video model bound to a single model: a prompt, and optionally the picture to start from, in; one clip out, made as
/// a job that takes minutes. How far a job has got is reported through the activity manager the provider was built
/// with. Failures surface as an <see cref="LlmProviderException"/>.
/// </summary>
public interface IVideoGenerationProvider : IAiProvider
{
    /// <summary>The clip lengths it makes, in seconds, shortest first; a request is made at the nearest.</summary>
    IReadOnlyList<int> Lengths { get; }

    /// <summary>Whether it starts a clip from a <see cref="VideoGenerationRequest.FirstFrame"/>.</summary>
    bool StartsFromImages { get; }

    /// <summary>What a clip may be asked for with besides its prompt, its length and its first frame.</summary>
    VideoSettings Settings { get; }

    /// <summary>
    /// The exact size a first frame must have for a clip in <paramref name="aspect"/> at <paramref name="resolution"/> —
    /// null for the model's own — or null when it takes a picture of any size. The caller fits the picture to it.
    /// </summary>
    (int Width, int Height)? FirstFrameSize(MediaAspect aspect, string? resolution);

    Task<GeneratedMedia> GenerateVideoAsync(VideoGenerationRequest request, CancellationToken ct = default);
}

/// <param name="Prompt">What happens in the clip.</param>
/// <param name="Aspect">Its proportions: one of the model's <see cref="VideoSettings.Aspects"/>.</param>
/// <param name="Seconds">How long it should run; one of <see cref="IVideoGenerationProvider.Lengths"/>.</param>
public sealed record VideoGenerationRequest(string Prompt, MediaAspect Aspect, int Seconds)
{
    /// <summary>The name of one of the model's <see cref="VideoSettings.Resolutions"/>; null for its own.</summary>
    public string? Resolution { get; init; }

    /// <summary>The seed it is made from, 0 or more, for a model that <see cref="VideoSettings.Seeds"/>; null for one of its choosing.</summary>
    public int? Seed { get; init; }

    /// <summary>What to keep out of the clip, for a model that takes <see cref="VideoSettings.NegativePrompts"/>.</summary>
    public string? NegativePrompt { get; init; }
    /// <summary>The picture the clip starts from — only for a provider that
    /// <see cref="IVideoGenerationProvider.StartsFromImages"/>, at its <see cref="IVideoGenerationProvider.FirstFrameSize"/>.</summary>
    public MediaBytes? FirstFrame { get; init; }
}
