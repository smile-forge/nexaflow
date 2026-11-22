using Nexaflow.Features.Common.ThisPc;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// Puts the runtime's locations in "This PC" — each WSL distro, each connected server — opened at their own root through
/// the runtime's file system. A connection that could not be mounted is listed too, unavailable, saying why. Nothing here
/// touches a location: drawing This PC never starts a distro or dials a server, so each row's detail is what the runtime
/// already knows.
/// </summary>
public sealed class LocationThisPcProvider : IThisPcItemProvider
{
    private readonly ILocationDirectory _locations;

    public LocationThisPcProvider(ILocationDirectory locations)
    {
        _locations = locations;
        _locations.Changed += () => Changed?.Invoke();
    }

    public string ProviderId => "locations";

    /// <summary>After the volumes and the cloud folders.</summary>
    public int SortOrder => 40;

    public event Action? Changed;

    public IReadOnlyList<ThisPcItem> GetItems()
    {
        var items = new List<ThisPcItem>();
        // Grouped by kind, each kind's rows in the order the runtime mounted them — a default distro first.
        foreach (var location in _locations.Mounted.OrderBy(l => l.Kind, StringComparer.Ordinal))
            items.Add(Row(location.Id, location.Label, location.Root, location.Kind, location.Capabilities, items.Count));
        foreach (var unreachable in _locations.Unreachable)
        {
            var connection = unreachable.Connection;
            items.Add(Row(connection.Id, connection.Label, VirtualMount.RootFor(connection.Id), connection.Kind,
                          LocationCapabilities.None, items.Count));
        }
        return items;
    }

    private static ThisPcItem Row(string id, string label, string root, string kind, LocationCapabilities capabilities, int order)
        => new()
        {
            Id         = id,
            Label      = label,
            TargetPath = root,
            TypeLabel  = KindLabel(kind),
            Icon       = (capabilities & LocationCapabilities.SameMachine) != 0 ? ThisPcItemIcon.Disk : ThisPcItemIcon.Network,
            Backing    = ThisPcItemBacking.Virtual,
            SortOrder  = order,
        };

    private static string KindLabel(string kind)
        => kind == "wsl" ? Str.Get("WindowsFileSystem.Locations.Wsl") : Str.Format("WindowsFileSystem.Locations.KindFormat", kind.ToUpperInvariant());

    /// <summary>A mounted location is there and opens as a folder; one that could not be mounted says why.</summary>
    public Task<ThisPcItemDetail> GetDetailAsync(ThisPcItem item, CancellationToken ct = default)
    {
        var unreachable = _locations.Unreachable.FirstOrDefault(u => string.Equals(u.Connection.Id, item.Id, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(unreachable is null
            ? new ThisPcItemDetail
            {
                Available   = _locations.Mounted.Any(m => string.Equals(m.Id, item.Id, StringComparison.OrdinalIgnoreCase)),
                HasChildren = true,
            }
            : new ThisPcItemDetail
            {
                Available = false,
                Label     = Str.Format("WindowsFileSystem.Locations.UnreachableFormat", item.Label, unreachable.Reason),
            });
    }
}
