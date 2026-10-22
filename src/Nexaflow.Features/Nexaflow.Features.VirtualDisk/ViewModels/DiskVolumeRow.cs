using Nexaflow.Features.VirtualDisk.Models;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.VirtualDisk.ViewModels;

/// <summary>A volume row in the inspector's metadata pane — the display-formatted view of a
/// <see cref="DiskVolumeInfo"/>, in the person's language. The AI tools read the <see cref="DiskVolumeInfo"/>
/// itself, never these labels.</summary>
public sealed record DiskVolumeRow(string Title, string Detail, string Usage)
{
    public static DiskVolumeRow From(DiskVolumeInfo v)
    {
        var fileSystem = v.FileSystem == DiskVolumeInfo.UnknownFileSystem
            ? Str.Get("VirtualDisk.Volume.UnknownFileSystem")
            : v.FileSystem;
        var detail = v.Label is null ? fileSystem : $"{fileSystem} · {v.Label}";
        var usage = v.Capacity > 0
            ? Str.Format("VirtualDisk.Volume.UsageFormat",
                SizeFormatter.FormatBytes(v.Used), SizeFormatter.FormatBytes(v.Capacity))
            : string.Empty;
        return new DiskVolumeRow(Str.Format("VirtualDisk.Volume.TitleFormat", v.Index), detail, usage);
    }
}
