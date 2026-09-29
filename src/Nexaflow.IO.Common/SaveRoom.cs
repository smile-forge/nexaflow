using System;
using System.IO;

namespace Nexaflow.IO.Common;

/// <summary>Why a save would fail before a byte of it is written.</summary>
public enum SaveTrouble
{
    None,
    NoName,
    NotAPath,
    NoFolder,
    FolderRefused,
    FileReadOnly,
    FileInUse,
    FileRefused,
}

/// <summary>What saving to one path would come to, read as a snapshot: what is already there, what the volume
/// has left, and whether the write would be allowed at all.</summary>
public sealed record SaveRoom(string Target, long Bytes, long? Free, long? Replacing,
                             DateTime? LastWritten, SaveTrouble Trouble)
{
    public bool Replaces => Replacing is not null;

    /// <summary>Replacing hands the old file's space back, so overwriting 100 MB with 150 asks for 50, not 150.
    /// A volume that would not say how much it has left never blocks a save.</summary>
    public bool Fits => Free is not { } free || Bytes <= free + (Replacing ?? 0);

    public long Shortfall => Fits ? 0 : Bytes - (Free ?? 0) - (Replacing ?? 0);

    public bool Allowed => Trouble is SaveTrouble.None && Fits;

    public static SaveRoom For(string? target, long bytes = 0)
    {
        if (string.IsNullOrWhiteSpace(target)) return Blocked(target, bytes, SaveTrouble.NoName);

        string full;
        try { full = Path.GetFullPath(target); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Blocked(target, bytes, SaveTrouble.NotAPath);
        }

        if (Path.GetFileName(full) is not { Length: > 0 } leaf) return Blocked(full, bytes, SaveTrouble.NoName);
        if (Path.GetDirectoryName(full) is not { Length: > 0 } folder) return Blocked(full, bytes, SaveTrouble.NotAPath);

        // GetFullPath stopped refusing these, so nothing before here has looked at them.
        if (leaf.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return Blocked(full, bytes, SaveTrouble.NotAPath);

        var existing = new FileInfo(LongPath.Prefix(full));
        var there    = existing.Exists;

        return new SaveRoom(full, bytes, FreeOn(full), there ? existing.Length : null,
                            there ? existing.LastWriteTime : null, Stopping(folder, existing));
    }

    private static SaveRoom Blocked(string? target, long bytes, SaveTrouble trouble) =>
        new(target ?? string.Empty, bytes, null, null, null, trouble);

    /// <summary>What the volume has left for this user — the quota-aware figure, not the disk's own.</summary>
    private static long? FreeOn(string target)
    {
        try
        {
            // A UNC share has no DriveInfo, so a save to one reports its room as unknown rather than guessing.
            return Path.GetPathRoot(target) is { Length: > 0 } root && !root.StartsWith(@"\\")
                       ? new DriveInfo(root).AvailableFreeSpace
                       : null;
        }
        catch (Exception e) when (e is ArgumentException or DriveNotFoundException
                                      or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Whether a folder would take a save at all, asked of the folder rather than of a file in it.</summary>
    public static bool TakesASave(string? folder) =>
        folder is { Length: > 0 } && Directory.Exists(LongPath.Prefix(folder)) && Probe(folder) is SaveTrouble.None;

    private static SaveTrouble Stopping(string folder, FileInfo existing)
    {
        if (!Directory.Exists(LongPath.Prefix(folder))) return SaveTrouble.NoFolder;
        if (!existing.Exists) return Probe(folder);
        if (existing.IsReadOnly) return SaveTrouble.FileReadOnly;

        // Whether a write is allowed is the access we ask for, not the sharing we offer — so ask for exactly
        // what a save asks for, and allow everything, or a harmless reader reads as a lock.
        try { using var held = existing.Open(FileMode.Open, FileAccess.Write, FileShare.ReadWrite); }
        catch (UnauthorizedAccessException) { return SaveTrouble.FileRefused; }
        catch (IOException) { return SaveTrouble.FileInUse; }

        return SaveTrouble.None;
    }

    /// <summary>Nothing short of a write answers this: the verdict is the folder's ACL against this process's
    /// token, which no property of the path reports. DeleteOnClose takes the probe back out even if we are
    /// killed holding it.</summary>
    private static SaveTrouble Probe(string folder)
    {
        try
        {
            using var probe = new FileStream(LongPath.Prefix(Path.Combine(folder, $".nexaflow-save-{Guid.NewGuid():N}")),
                                             FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                             bufferSize: 1, FileOptions.DeleteOnClose);
            return SaveTrouble.None;
        }
        catch (UnauthorizedAccessException) { return SaveTrouble.FolderRefused; }
        catch (IOException) { return SaveTrouble.FolderRefused; }
    }
}
