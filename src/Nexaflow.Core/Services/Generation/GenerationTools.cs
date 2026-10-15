using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Generation;

/// <summary>
/// The tools that make things — a picture, text read aloud, a video clip — each offered when the workspace has a model
/// for it now: its Image Generation and Image Generation (HQ), Speech Out, and Video Generation and Video Generation (HQ)
/// rows.
/// </summary>
internal sealed class GenerationTools(IImageGenerationProvider? images, IImageGenerationProvider? highQualityImages,
                                      ITextToSpeechProvider? voice, IVideoGenerationProvider? videos,
                                      IVideoGenerationProvider? highQualityVideos, GeneratedFiles files)
{
    public IReadOnlyList<IClientTool> Tools =>
    [
        .. images is null && highQualityImages is null ? Array.Empty<IClientTool>() : [new GenerateImageTool(images, highQualityImages, files)],
        .. voice  is null ? Array.Empty<IClientTool>() : [new GenerateSpeechTool(voice, files)],
        .. videos is null && highQualityVideos is null ? Array.Empty<IClientTool>() : [new GenerateVideoTool(videos, highQualityVideos, files)],
    ];
}
