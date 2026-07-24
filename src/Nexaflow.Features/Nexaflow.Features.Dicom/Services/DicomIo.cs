using System.Collections.Generic;
using System.IO;
using FellowOakDicom;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Dicom.Services;

/// <summary>
/// Reads DICOM content from either a real on-disk path or an <b>in-archive (virtual) path</b> — so a study
/// opened from inside a <c>.zip</c> works the same as one on disk. The shell's <see cref="VirtualFileSystem"/>
/// is transparent for real paths, so callers never branch on where the bytes live.
/// </summary>
internal static class DicomIo
{
    private static IVirtualFileSystem Vfs => VirtualFileSystem.Instance;

    public static bool Exists(string path) => File.Exists(path) || Vfs.Exists(path);

    /// <summary>True for a real directory, an archive container (a <c>.zip</c> whose contents we browse), or a
    /// virtual folder inside one.</summary>
    public static bool IsDirectory(string path)
        => Directory.Exists(path)
           || Vfs.IsContainer(path)
           || (!File.Exists(path) && Vfs.GetEntryInfo(path) is { IsDirectory: true });

    /// <summary>Opens a DICOM file from a real or virtual path. The stream is fully consumed by the chosen
    /// <paramref name="option"/> (headers-only or read-all) before it is disposed.</summary>
    public static DicomFile Open(string path, FileReadOption option)
    {
        if (File.Exists(path)) return DicomFile.Open(path, option);
        using var s = Vfs.OpenRead(path);
        return DicomFile.Open(s, option);
    }

    /// <summary>Recursively enumerates every file under a real directory or a virtual folder/archive.</summary>
    public static IEnumerable<string> EnumerateFiles(string dir)
    {
        if (Directory.Exists(dir))
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) yield return f;
            yield break;
        }
        foreach (var f in EnumerateVirtual(dir)) yield return f;
    }

    private static IEnumerable<string> EnumerateVirtual(string dir)
    {
        IReadOnlyList<VirtualEntry> entries;
        try { entries = Vfs.EnumerateEntries(dir); }
        catch { yield break; }

        foreach (var e in entries)
        {
            var child = dir.TrimEnd('/', '\\') + Path.DirectorySeparatorChar + e.Name;
            if (e.IsDirectory)
                foreach (var f in EnumerateVirtual(child)) yield return f;
            else
                yield return child;
        }
    }

    /// <summary>Recognises a DICOM file at a real or virtual path: a known extension, else the <c>DICM</c> magic.</summary>
    public static bool IsDicom(string path)
    {
        if (DicomFileSniffer.HasDicomExtension(path)) return true;
        if (File.Exists(path)) return DicomFileSniffer.HasDicmMagic(path);
        try
        {
            using var s = Vfs.OpenRead(path);
            return DicomFileSniffer.HasDicmMagic(s);
        }
        catch { return false; }
    }
}
