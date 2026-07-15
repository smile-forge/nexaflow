using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Features.VirtualDisk.Services;

/// <summary>Outcome of a mount attempt.</summary>
public readonly record struct MountOutcome(bool Success, string? DriveLetter, string? Error)
{
    public static MountOutcome Ok(string? drive) => new(true, drive, null);
    public static MountOutcome Failed(string? error) => new(false, null, error);
}

/// <summary>
/// Mounts and unmounts disk images through the native Windows Virtual Disk API (see
/// <see cref="NativeVirtualDisk"/>) — no process is ever spawned. ISO attaches as a standard user; VHD/VHDX
/// attach needs elevation (see <see cref="MountSupport"/>) and is routed through the privilege bridge — this
/// class runs the un-elevated (ISO) path.
/// <para>
/// The Unmount drive action is offered only for images this app mounted (tracked below with their exact
/// backing path), so a click can always detach the right image with no guesswork. Images mounted outside
/// the app (or in a previous session) are unmounted where they were mounted.
/// </para>
/// </summary>
public sealed class DiskMounter
{
    /// <summary>Mounts <paramref name="imagePath"/> and returns the assigned drive letter. Runs without
    /// elevation (correct for ISO); VHD/VHDX go through the bridge instead.</summary>
    public MountOutcome Mount(string imagePath)
    {
        try
        {
            var readOnly = imagePath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase);
            var drive    = NativeVirtualDisk.Attach(imagePath, readOnly);
            NoteMounted(drive, imagePath);
            return MountOutcome.Ok(drive);
        }
        catch (Exception ex)
        {
            return MountOutcome.Failed(ex.Message);
        }
    }

    /// <summary>Dismounts <paramref name="imagePath"/>. Un-elevated path (ISO).</summary>
    public bool Dismount(string imagePath)
    {
        try
        {
            NativeVirtualDisk.Detach(imagePath);
            NoteUnmounted(imagePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── Session mount registry ──────────────────────────────────────────────────
    // The single source of truth for "is this drive an image this app mounted, and what's its backing file?".
    // A dictionary lookup — no process, no IOCTL, no query — so it's free to consult on every folder selection.

    private static readonly Dictionary<char, string> _appMounted = new();
    private static readonly object _appMountedLock = new();

    /// <summary>Records a mount this app performed. Called for both the in-process (ISO) path and the
    /// elevated (VHD/VHDX) bridge path — <paramref name="driveLetter"/> may be <c>"E:"</c>, <c>"E"</c> or null.</summary>
    public static void NoteMounted(string? driveLetter, string imagePath)
    {
        var letter = driveLetter?.TrimEnd(':', '\\', '/');
        if (!string.IsNullOrEmpty(letter) && char.IsLetter(letter[0]))
            lock (_appMountedLock) _appMounted[char.ToUpperInvariant(letter[0])] = imagePath;
    }

    /// <summary>Forgets a mount this app dismounted, so its Unmount action stops being offered at once.</summary>
    public static void NoteUnmounted(string imagePath)
    {
        lock (_appMountedLock)
            foreach (var letter in _appMounted.Where(kv => string.Equals(kv.Value, imagePath, StringComparison.OrdinalIgnoreCase))
                                              .Select(kv => kv.Key).ToList())
                _appMounted.Remove(letter);
    }

    /// <summary>True if <paramref name="driveLetter"/> is a volume of an image this app mounted this session.
    /// A plain dictionary lookup — safe to call on every selection.</summary>
    public bool IsImageBacked(char driveLetter)
    {
        lock (_appMountedLock) return _appMounted.ContainsKey(char.ToUpperInvariant(driveLetter));
    }

    /// <summary>The image file backing <paramref name="driveLetter"/>, or null if this app didn't mount it.</summary>
    public string? ImagePathForDrive(char driveLetter)
    {
        lock (_appMountedLock)
            return _appMounted.GetValueOrDefault(char.ToUpperInvariant(driveLetter));
    }
}
