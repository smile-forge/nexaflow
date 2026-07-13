using Nexaflow.Features.VirtualDisk.Models;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Features.VirtualDisk.ViewModels;

/// <summary>A volume row in the inspector's metadata pane — the display-formatted view of a
/// <see cref="DiskVolumeInfo"/>.</summary>
public sealed record DiskVolumeRow(string Title, string Detail, string Usage)
{
    public static DiskVolumeRow From(DiskVolumeInfo v)
    {
        var detail = v.Label is null ? v.FileSystem : $"{v.FileSystem} · {v.Label}";
        var usage = v.Capacity > 0
            ? $"{SizeFormatter.FormatBytes(v.Used)} used of {SizeFormatter.FormatBytes(v.Capacity)}"
            : string.Empty;
        return new DiskVolumeRow($"Volume {v.Index}", detail, usage);
    }
}
