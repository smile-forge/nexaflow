using Nexaflow.IO.Common.Locations;

namespace Nexaflow.IO.Common;

/// <summary>
/// Work every caller of the file system needs, built on its members: a new file with given bytes, a copy, and whether a
/// folder holds a folder. The writes go through <see cref="IVirtualFileSystem.BeginWriteAsync"/>, so nobody sees half of
/// either, and every path may be a real path, a mount, an archive entry or a connected location.
/// </summary>
public static class VirtualFileSystemFiles
{
    /// <summary>Creates the file holding <paramref name="content"/>; <see cref="LocationError.Conflict"/> when something
    /// is already there.</summary>
    public static async ValueTask CreateFileAsync(this IVirtualFileSystem vfs, string path, ReadOnlyMemory<byte> content,
                                                  CancellationToken ct = default)
    {
        await using var write = await vfs.BeginWriteAsync(path, WriteMode.CreateNew, expected: null, ct).ConfigureAwait(false);
        await write.Content.WriteAsync(content, ct).ConfigureAwait(false);
        await write.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Copies the file at <paramref name="from"/> to <paramref name="to"/>, streamed, wherever each is. With
    /// <paramref name="replace"/> false a file already at <paramref name="to"/> is a <see cref="LocationError.Conflict"/>.</summary>
    public static async ValueTask CopyFileAsync(this IVirtualFileSystem vfs, string from, string to, bool replace,
                                                CancellationToken ct = default)
    {
        await using var source = await vfs.OpenReadAsync(from, ReadIntent.Sequential, ct).ConfigureAwait(false);
        await using var write  = await vfs.BeginWriteAsync(to, replace ? WriteMode.Replace : WriteMode.CreateNew, expected: null, ct)
                                          .ConfigureAwait(false);
        await source.CopyToAsync(write.Content, ct).ConfigureAwait(false);
        await write.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Whether the folder at <paramref name="path"/> holds a folder — what gives a tree row its expander. Reads the
    /// listing only until the first folder; a folder that cannot be read, or an archive entry that is only a temp copy,
    /// holds none.</summary>
    public static async Task<bool> HasChildFoldersAsync(this IVirtualFileSystem vfs, string path, CancellationToken ct = default)
    {
        if (vfs.GetBacking(path) is VirtualBacking.Materialized) return false;
        try
        {
            await foreach (var entry in vfs.EnumerateAsync(path, ct).ConfigureAwait(false))
                if (entry.IsDirectory) return true;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }
}
