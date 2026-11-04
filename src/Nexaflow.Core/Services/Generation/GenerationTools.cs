using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Generation;

/// <summary>
/// The tools that make things — a picture drawn or changed, text read aloud, a video clip — each offered when the workspace
/// has a model for it now: its Image Generation and Image Generation (HQ) rows for generate_image, Image Editing for
/// edit_image, Speech Out, and Video Generation and Video Generation (HQ) rows. Adult content is offered only while the app allows it (<paramref name="allowsAdultContent"/>), and
/// every picture passes <paramref name="pictureCheck"/> where Enhanced AI Protection Mode gives one.
/// </summary>
/// <param name="imageEdits">The Image Editing model, which edit_image works with.</param>
internal sealed class GenerationTools(IImageGenerationProvider? images, IImageGenerationProvider? highQualityImages, IImageGenerationProvider? imageEdits,
                                      ITextToSpeechProvider? voice, IVideoGenerationProvider? videos,
                                      IVideoGenerationProvider? highQualityVideos, GeneratedFiles files, bool allowsAdultContent = false,
                                      IPictureCheck? pictureCheck = null)
{
    public IReadOnlyList<IClientTool> Tools =>
    [
        .. images is null && highQualityImages is null
            ? Array.Empty<IClientTool>()
            : [new GenerateImageTool(images, highQualityImages, files, allowsAdultContent, pictureCheck)],
        .. imageEdits is { MaxSources: > 0 } ? [new EditImageTool(imageEdits, files, pictureCheck)] : Array.Empty<IClientTool>(),
        .. voice  is null ? Array.Empty<IClientTool>() : [new GenerateSpeechTool(voice, files)],
        .. videos is null && highQualityVideos is null ? Array.Empty<IClientTool>() : [new GenerateVideoTool(videos, highQualityVideos, files)],
    ];

    /// <summary>What each tool's 'path' says to the model: saved only into a folder open in a tab, else opened unsaved.</summary>
    internal const string SavedWhere =
        "Where to save it: a folder, or a file path, inside a folder open in a tab. Not given, it opens for the user unsaved, for them to save where they choose.";
}
