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

    /// <summary>
    /// The exact size a first frame must have for <paramref name="shape"/>, or null when it takes a picture of any size.
    /// The caller fits the picture to it.
    /// </summary>
    (int Width, int Height)? FirstFrameSize(MediaShape shape);

    Task<GeneratedMedia> GenerateVideoAsync(VideoGenerationRequest request, CancellationToken ct = default);
}

/// <param name="Prompt">What happens in the clip.</param>
/// <param name="Shape">Landscape or portrait — video models make no square clips.</param>
/// <param name="Seconds">How long it should run; one of <see cref="IVideoGenerationProvider.Lengths"/>.</param>
public sealed record VideoGenerationRequest(string Prompt, MediaShape Shape, int Seconds)
{
    /// <summary>The picture the clip starts from — only for a provider that
    /// <see cref="IVideoGenerationProvider.StartsFromImages"/>, at its <see cref="IVideoGenerationProvider.FirstFrameSize"/>.</summary>
    public MediaBytes? FirstFrame { get; init; }
}
