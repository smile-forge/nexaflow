using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Generation;

/// <summary>
/// The tools that make things — a picture, text read aloud, a video clip — each offered when the workspace has a model
/// for it now: its Image Generation, Speech Out and Video Generation rows.
/// </summary>
internal sealed class GenerationTools(IImageGenerationProvider? images, ITextToSpeechProvider? voice, IVideoGenerationProvider? videos, GeneratedFiles files)
{
    public IReadOnlyList<IClientTool> Tools =>
    [
        .. images is null ? Array.Empty<IClientTool>() : [new GenerateImageTool(images, files)],
        .. voice  is null ? Array.Empty<IClientTool>() : [new GenerateSpeechTool(voice, files)],
        .. videos is null ? Array.Empty<IClientTool>() : [new GenerateVideoTool(videos, files)],
    ];
}
