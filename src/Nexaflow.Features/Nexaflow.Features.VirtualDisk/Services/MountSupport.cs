using System;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.VirtualDisk.Services;

/// <summary>Which disk formats Windows can surface as a real drive letter <b>without a third-party driver</b>,
/// and which of those need administrator rights. Backed by the built-in Virtual Disk Service: ISO mounts as a
/// standard user; VHD/VHDX attach requires elevation. Other DiscUtils-readable formats
/// (VDI/VMDK/DMG/…) can be browsed and inspected but not mounted natively.</summary>
public static class MountSupport
{
    private static readonly HashSet<string> Mountable =
        new(StringComparer.OrdinalIgnoreCase) { ".iso", ".vhd", ".vhdx" };

    public static bool CanMount(string fileName) =>
        Mountable.Contains(Path.GetExtension(fileName));

    /// <summary>True when attaching/detaching this image requires administrator rights (everything but ISO).</summary>
    public static bool RequiresElevation(string fileName) =>
        !Path.GetExtension(fileName).Equals(".iso", StringComparison.OrdinalIgnoreCase);
}
