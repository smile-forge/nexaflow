using Nexaflow.Features.Common;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Images.FileActions;

/// <summary>
/// "New → Gen Image": a picture the workspace's image model draws from what the user describes, set up in the picker
/// with what the model offers (<see cref="GenerateImageOptions"/>), drawn in the background into the folder and opened
/// once it is made. Offered while a model draws pictures, or one of Nexaflow's own waits on its download.
/// </summary>
public sealed class GenerateImageCreateAction(IAIService ai) : IFileCreateWithOptions, ICacheable
{
    public string Icon          => "🎨";
    public string DisplayName   => Str.Get("Images.Create.GenImage");
    public string FileExtension => ".png";

    public bool IsOffered => GenerateImageOptions.Offered(ai);

    public IFileCreateOptions OpenOptions(string folderPath) => new GenerateImageOptions(ai, folderPath);
}
