using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using FellowOakDicom;
using FellowOakDicom.Media;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Features.Dicom.Services;

/// <summary>
/// Reads DICOM content wherever the runtime's file system reaches it — on disk, inside an archive or on a location — so
/// a study opened from inside a <c>.zip</c> works the same as one on disk. A file on this disk is opened by its path, so
/// fo-dicom reads what it skipped on demand; anywhere else its bytes come through the file system.
/// </summary>
internal sealed class DicomIo(IVirtualFileSystem vfs)
{
    /// <summary>Whether <paramref name="path"/> is a file on this disk — one fo-dicom can read by path, lazily.</summary>
    public bool IsOnDisk(string path) => vfs.ResolveOnDisk(path) is not null;

    public ValueTask<bool> ExistsAsync(string path, CancellationToken ct = default) => vfs.ExistsAsync(path, ct);

    /// <summary>True for a folder, an archive container (a <c>.zip</c> whose contents we browse), or a folder inside one.</summary>
    public ValueTask<bool> IsDirectoryAsync(string path, CancellationToken ct = default) => vfs.IsDirectoryAsync(path, ct);

    /// <summary>Opens a DICOM file. Read through a stream, the file is fully consumed by the chosen
    /// <paramref name="option"/> (headers-only or read-all) before the stream is disposed.</summary>
    public async Task<DicomFile> OpenAsync(string path, FileReadOption option, CancellationToken ct = default)
    {
        if (vfs.ResolveOnDisk(path) is { } real) return await DicomFile.OpenAsync(real, option);
        await using var s = await vfs.OpenReadAsync(path, ReadIntent.Random, ct);
        return await DicomFile.OpenAsync(s, option);
    }

    /// <summary>Opens a <c>DICOMDIR</c> index.</summary>
    public async Task<DicomDirectory> OpenDirectoryAsync(string path, CancellationToken ct = default)
    {
        if (vfs.ResolveOnDisk(path) is { } real) return await DicomDirectory.OpenAsync(real);
        await using var s = await vfs.OpenReadAsync(path, ReadIntent.Random, ct);
        return await DicomDirectory.OpenAsync(s);
    }

    /// <summary>Every file under a folder or an archive, however deep. A folder that cannot be listed has none.</summary>
    public async IAsyncEnumerable<string> EnumerateFilesAsync(string dir, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var entries = new List<LocationEntry>();
        try
        {
            await foreach (var entry in vfs.EnumerateAsync(dir, ct)) entries.Add(entry);
        }
        catch (Exception e) when (e is not OperationCanceledException) { yield break; }

        foreach (var entry in entries)
        {
            var child = Path.Combine(dir, entry.Name);
            if (entry.IsDirectory)
                await foreach (var f in EnumerateFilesAsync(child, ct)) yield return f;
            else
                yield return child;
        }
    }

    /// <summary>Recognises a DICOM file: a known extension, else the <c>DICM</c> magic.</summary>
    public async Task<bool> IsDicomAsync(string path, CancellationToken ct = default)
    {
        if (DicomFileSniffer.HasDicomExtension(path)) return true;
        try
        {
            await using var s = await vfs.OpenReadAsync(path, ReadIntent.Random, ct);
            return DicomFileSniffer.HasDicmMagic(s);
        }
        catch (Exception e) when (e is not OperationCanceledException) { return false; }
    }
}
