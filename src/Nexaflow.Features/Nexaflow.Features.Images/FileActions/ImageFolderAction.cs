using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Images.FileActions;

/// <summary>
/// Shared base for the folder actions that open a directory of images in the viewer. Both restrict
/// themselves to folders where at least 30% of the files are images (<see cref="ImageFileTypes.Globs"/>);
/// they differ only in the initial view they request (slideshow vs album).
/// </summary>
public abstract class ImageFolderAction : IFolderAction, ICacheable
{
    private readonly IShellServices _shell;

    protected ImageFolderAction(IShellServices shell) => _shell = shell;

    /// <summary>The <c>view</c> page param the viewer opens in ("slideshow" or "album").</summary>
    protected abstract string ViewMode { get; }

    public bool   IsDestructive         => false;
    public bool   SupportsMultipleFiles => false;
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;
    public bool   AppliesToRoot         => true;    // also offered for the open folder (gated by the 30% check)
    public bool   AppliesToDrives       => false;

    public string[]? ContainsFileGlobs            => ImageFileTypes.Globs;
    public int       MinimumFileGlobMatchPercentage => 30;

    public abstract string Icon        { get; }
    public abstract string DisplayName { get; }

    public Task<bool> PerformActionAsync(string folderPath, CancellationToken ct = default)
    {
        var images = ImageFileTypes.EnumerateImages(folderPath);
        if (images.Count == 0) return Task.FromResult(false);

        _shell.OpenTab("Images", new Dictionary<string, string>
        {
            ["paths"] = string.Join('|', images),
            ["view"]  = ViewMode,
            ["scope"] = "folder",   // whole-folder view → breadcrumb reads "all image files"
        });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> folderPaths, CancellationToken ct = default)
    => folderPaths.FirstOrDefault() is { } first ? PerformActionAsync(first, ct) : Task.FromResult(false);
}
