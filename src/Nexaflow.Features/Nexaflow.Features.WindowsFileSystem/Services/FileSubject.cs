using Nexaflow.IO.Common;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// A file as the file map matches it. <see cref="Path"/> gives its name, its extensions and the path patterns it falls
/// under. <see cref="LocalFile"/> is the file on this disk a magic-number rule may read — present only when the file
/// system resolves the path here. A file on a location, or inside an archive, has none, and is matched by name alone:
/// matching runs wherever the strip or a menu is built, and nothing there may wait on the network.
/// </summary>
public readonly record struct FileSubject(string Path, string? LocalFile)
{
    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>A file matched by its name alone — an extension's question, or a path whose bytes are not read.</summary>
    public static FileSubject ByName(string path) => new(path, null);

    /// <summary>The file at <paramref name="path"/>, with the file on this disk behind it when there is one.</summary>
    public static FileSubject Of(IVirtualFileSystem vfs, string path)
        => new(path, vfs.GetBacking(path).IsFullyOnDisk() ? vfs.TryResolveReal(path) : null);
}
