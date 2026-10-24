using Nexaflow.Features.Common;
using Nexaflow.Features.Video.ViewModels;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Video.FileActions;

/// <summary>
/// "New → Gen Video": a clip the workspace's video model makes from what the user describes, set up in the picker with
/// what the model offers (<see cref="GenerateVideoOptions"/>), made in the background into the folder and opened once it
/// is made. Offered while a model makes clips, or one of Nexaflow's own waits on its download.
/// </summary>
public sealed class GenerateVideoCreateAction(IAIService ai) : IFileCreateWithOptions, ICacheable
{
    public string Icon          => "🎬";
    public string DisplayName   => Str.Get("Video.Create.GenVideo");
    public string FileExtension => ".mp4";

    public bool IsOffered => GenerateVideoOptions.Offered(ai);

    public IFileCreateOptions OpenOptions(string folderPath) => new GenerateVideoOptions(ai, folderPath);
}
