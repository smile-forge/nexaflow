using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.IO.Common;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What a Markdown document is wherever it is kept: a Markdown file, or a Markdown Package (<see cref="MarkdownPackage"/>)
/// holding one with the files it names, as a .docx holds its pictures. Whatever works on a document works on either. What
/// it says is read where it stands — a package's own document read inside it, never unpacked to be read — and the files it
/// names are read from beside a file, or from inside a package.
/// </summary>
public static class MarkdownDocuments
{
    /// <summary>Whether <paramref name="path"/> names a Markdown document: a Markdown file or a Markdown Package.</summary>
    public static bool Names(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown" || MarkdownPackage.Names(path);

    /// <summary>
    /// What the document at <paramref name="path"/> says, wherever <paramref name="vfs"/> reaches it, its lines ended as the
    /// editor ends them; null where nothing is there.
    /// </summary>
    /// <exception cref="InvalidDataException">The package there holds no document.</exception>
    public static async Task<string?> ReadTextAsync(IVirtualFileSystem vfs, string path, CancellationToken ct = default)
    {
        if (!await vfs.ExistsAsync(path, ct)) return null;

        var document = MarkdownPackage.Names(path) ? Path.Combine(path, MarkdownPackage.Document) : path;
        if (!await vfs.ExistsAsync(document, ct))
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a Markdown Package: it holds no {MarkdownPackage.Document}.");

        return (await vfs.ReadAllTextAsync(document, ct: ct)).ReplaceLineEndings("\n");
    }

    /// <summary>
    /// As <see cref="ReadTextAsync"/>, answered at once, for a document already known to be on this disk — one the
    /// assistant's security context resolved, whose path cannot name a location. For a decision made before any work runs,
    /// such as whether a call needs approval.
    /// </summary>
    /// <exception cref="InvalidDataException">The package there holds no document.</exception>
    [LocalDiskOnly("a document the assistant's security context resolved, which is always on this disk")]
    public static string? ReadLocalText(string path)
    {
        var files = VirtualFileSystem.Instance;
        if (!files.Exists(path)) return null;

        var document = MarkdownPackage.Names(path) ? Path.Combine(path, MarkdownPackage.Document) : path;
        if (!files.Exists(document))
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a Markdown Package: it holds no {MarkdownPackage.Document}.");

        return files.ReadAllText(document).ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Where the files the document at <paramref name="path"/> names are read from: the folder beside a Markdown file, or the
    /// package itself, which the file system reads into as a folder; null for a file in no folder.
    /// </summary>
    public static string? FolderOf(string path) =>
        MarkdownPackage.Names(path) ? path : Path.GetDirectoryName(path) is { Length: > 0 } folder ? folder : null;
}
