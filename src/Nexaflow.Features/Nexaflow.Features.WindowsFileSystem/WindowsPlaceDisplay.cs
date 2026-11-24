using Nexaflow.Features.Common;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.WindowsFileSystem;

/// <summary>
/// Restates a crumb that stands for a place on this computer — a folder or file on a drive or a share, an entry in an
/// archive, a mounted image's — as a person names it: the whole path as its parts joined by <c>\</c>, so the shell can
/// shorten it from the middle (<c>D:\…\temp</c>), or the place's own name; copied and shown on hover whole. A
/// location's places are its own feature's to restate (a distro's, the WSL feature's), so it leaves them as they are.
/// </summary>
public sealed class WindowsPlaceDisplay(IVirtualFileSystem files) : IBreadcrumbDisplayFilter
{
    public int Order => 0;

    public void Apply(BreadcrumbSegment crumb, BreadcrumbDisplay display)
    {
        if (crumb.Content is not PlaceContent { Path.Length: > 0 } place || files.IsOnLocation(place.Path)) return;

        var whole = files.GetDisplayPath(place.Path);
        display.CopyText = whole;
        display.ToolTip  = whole;

        switch (place.Naming)
        {
            case PlaceNaming.WholePath:
                // The root as the file system labels it (D:, \\server\share, a mounted image's name), then each folder.
                var trail = files.GetBreadcrumbs(place.Path);
                display.Parts     = trail.Count > 1 ? [.. trail.Select(t => t.Label)] : [whole];
                display.Separator = "\\";
                break;
            case PlaceNaming.OwnName:
                display.Parts     = [files.GetDisplayName(place.Path)];
                display.Separator = string.Empty;
                break;
        }
    }
}
