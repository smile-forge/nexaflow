using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Images.FileActions;

/// <summary>
/// Opens an image straight into edit mode. Its <c>/image/edit</c> experience is mapped onto the image types as an
/// optional one, so it sits on the action strip without competing to be what a double-click opens; the edits it
/// leads to are kept beside the photo and shown whenever it is viewed.
/// </summary>
public sealed class EditImageAction : IFileAction, ICacheable
{
    private readonly IShellServices _shellServices;

    public EditImageAction(IShellServices shellServices) => _shellServices = shellServices;

    public bool   IsDestructive          => false;
    public bool   SupportsMultipleFiles  => true;
    public IconRef Icon                  => IconRef.Emoji("✎");
    public string DisplayName            => Str.Get("Images.Action.Edit");
    public string? Tooltip               => Str.Get("Images.Action.EditTip");
    public static string? StaticExperienceId => "/image/edit";
    public string ExperienceId           => "/image/edit";
    public string ExperienceDescription  => Str.Get("Images.Action.EditDescription");
    public bool   RequiresRefresh        => false;
    public bool   CanPerformAction       => true;
    public bool   OpensViewer            => true;

    /// <summary>A sidecar is written beside the photo, which an archive has no room for.</summary>
    public bool   RequiresFullyBackedPath => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default) => PerformActionAsync([filePath], ct);

    /// <summary>A selection opens together, the first image in the editor and the rest a step away.</summary>
    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        var images = filePaths.Where(ImageFileTypes.IsImage).ToList();
        if (images.Count == 0) return Task.FromResult(false);
        _shellServices.OpenTab(ImageTabRegistration.StaticPageKind, new Dictionary<string, string>
        {
            ["paths"] = string.Join('|', images),
            ["view"]  = "edit",
        });
        return Task.FromResult(true);
    }
}
