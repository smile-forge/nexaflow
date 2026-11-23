using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Features.Wsl;

/// <summary>
/// Restates a crumb that stands for a place in a WSL distro as Linux names it, not as the file system does: the whole
/// path as the distro's address and its folders joined by <c>/</c> (<c>wsl://Ubuntu/etc</c>, shortened by the shell
/// from the middle), or the place's own Linux name (<c>at 12:30.txt</c>, not its encoded segment); copied and shown on
/// hover as its <c>wsl://</c> address.
/// </summary>
public sealed class DistroPlaceDisplay(IVirtualFileSystem files, ILocationDirectory locations) : IBreadcrumbDisplayFilter
{
    public int Order => 0;

    public void Apply(BreadcrumbSegment crumb, BreadcrumbDisplay display)
    {
        if (crumb.Content is not PlaceContent place || !DistroPlaces.Holds(locations, place.Path)) return;

        var address = files.GetDisplayPath(place.Path);
        display.CopyText = address;
        display.ToolTip  = address;

        switch (place.Naming)
        {
            case PlaceNaming.WholePath:
                // The distro's address leads (wsl://Ubuntu), then each folder by its Linux name.
                var trail = files.GetBreadcrumbs(place.Path);
                display.Parts = trail.Count > 1
                    ? [files.GetDisplayPath(trail[0].Path).TrimEnd('/'), .. trail.Skip(1).Select(t => t.Label)]
                    : [address];
                display.Separator = "/";
                break;
            case PlaceNaming.OwnName:
                display.Parts     = [files.GetDisplayName(place.Path)];
                display.Separator = string.Empty;
                break;
        }
    }
}
