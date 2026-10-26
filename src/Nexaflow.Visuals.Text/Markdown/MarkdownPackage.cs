using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Markdown.Packaging;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// A Markdown Package (*.mdz): one Markdown document and the files it shows or links to, zipped together as a .docx keeps
/// its pictures, so it reads the same wherever it goes. The document is <see cref="Document"/> at the top; every file it
/// names beside itself is under <see cref="Attachments"/>, its address written to that (<see cref="MarkdownAttachments"/>).
/// <para>
/// A package is a zip to the file system, so it is reached the way any archive is — in a folder, in a zip, in an ISO — and
/// read and written back through it. It is edited unpacked: <see cref="Unpack"/> lays it out in a folder, where the
/// document reads its attachments as it would beside any file, and <see cref="Pack"/> gathers a document and what it names
/// from its folder into one again.
/// </para>
/// </summary>
public static class MarkdownPackage
{
    /// <summary>A package's extension.</summary>
    public const string Extension = ".mdz";

    /// <summary>The document in a package.</summary>
    public const string Document = "document.md";

    /// <summary>The folder in a package that the files its document names are in.</summary>
    public const string Attachments = "attachments";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Whether <paramref name="path"/> names a package.</summary>
    public static bool Names(string path) => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lays the package at <paramref name="package"/> — wherever <paramref name="vfs"/> reaches it — out in
    /// <paramref name="folder"/>, its document and its attachments as they stand in it, and gives the document's text. An
    /// entry that would land outside the folder is not written.
    /// </summary>
    /// <exception cref="InvalidDataException">It holds no document.</exception>
    public static async Task<string> UnpackAsync(IVirtualFileSystem vfs, string package, string folder, CancellationToken ct = default)
    {
        string root = Path.GetFullPath(folder);
        await vfs.CreateDirectoryAsync(root, ct);
        await LayAsync(vfs, package, root, root, ct);
        string document = Path.Combine(root, Document);
        return await vfs.ExistsAsync(document, ct)
            ? await vfs.ReadAllTextAsync(document, Utf8, ct)
            : throw new InvalidDataException($"{Path.GetFileName(package)} is not a Markdown Package: it holds no {Document}.");
    }

    private static async Task LayAsync(IVirtualFileSystem vfs, string from, string into, string root, CancellationToken ct)
    {
        var entries = new List<LocationEntry>();
        await foreach (var entry in vfs.EnumerateAsync(from, ct)) entries.Add(entry);

        foreach (var entry in entries)
        {
            string target = Path.GetFullPath(Path.Combine(into, entry.Name));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            string source = Path.Combine(from, entry.Name);
            if (entry.IsDirectory)
            {
                await vfs.CreateDirectoryAsync(target, ct);
                await LayAsync(vfs, source, target, root, ct);
            }
            else await vfs.WriteAllBytesAsync(target, await vfs.ReadAllBytesAsync(source, ct), ct);
        }
    }

    /// <summary>
    /// Writes <paramref name="markdown"/> — a document whose attachments are read from <paramref name="folder"/> — as the
    /// package <paramref name="package"/>: each file it names beside itself under <see cref="Attachments"/> by its own name
    /// (numbered where two share one), the address written to it there. The package is put in place whole, so a save that
    /// fails leaves it as it was. Both ends go through <paramref name="vfs"/>, so either may be on a location; the package is
    /// built in a staging folder on this disk first.
    /// </summary>
    /// <exception cref="NotSupportedException">Where the package is cannot be written — inside a disc image.</exception>
    public static async Task PackAsync(IVirtualFileSystem vfs, string markdown, string folder, string package, CancellationToken ct = default)
    {
        string staging = Path.Combine(Path.GetTempPath(), "nexaflow-mdz", "packing-" + Guid.NewGuid().ToString("N")[..12]);
        string zip = staging + Extension;
        try
        {
            await vfs.CreateDirectoryAsync(Path.Combine(staging, Attachments), ct);
            var placed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // path beside it → its name inside
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var attachments = new List<MarkdownAttachment>();
            foreach (var attachment in MarkdownAttachments.Find(markdown).OrderBy(a => a.At))
                if (await vfs.ExistsAsync(Path.Combine(folder, attachment.Path), ct)) attachments.Add(attachment);
            // Named in the order the document names them, so the second of two the same is the one numbered…
            foreach (var attachment in attachments)
            {
                if (placed.ContainsKey(attachment.Path)) continue;
                string name = Free(Path.GetFileName(attachment.Path), taken);
                placed[attachment.Path] = name;
                await vfs.CopyFileAsync(Path.Combine(folder, attachment.Path), Path.Combine(staging, Attachments, name), replace: false, ct);
            }
            // …and their addresses written from the end, so each still stands where it was found.
            var text = new StringBuilder(markdown);
            for (int i = attachments.Count - 1; i >= 0; i--)
                text.Remove(attachments[i].At, attachments[i].Length)
                    .Insert(attachments[i].At, Attachments + "/" + Uri.EscapeDataString(placed[attachments[i].Path]));
            await vfs.WriteAllTextAsync(Path.Combine(staging, Document), text.ToString(), Utf8, ct);

            await vfs.CreateArchiveAsync(zip, staging, ct: ct);
            await vfs.ReplaceAsync(package, await vfs.ReadAllBytesAsync(zip, ct), ct);
        }
        finally
        {
            try { await vfs.DeleteAsync(staging, recursive: true, CancellationToken.None); } catch (IOException) { }
            try { await vfs.DeleteAsync(zip, recursive: false, CancellationToken.None); } catch (IOException) { }
        }
    }

    /// <summary>A name not yet taken in the package: <paramref name="name"/>, or it numbered.</summary>
    private static string Free(string name, HashSet<string> taken)
    {
        string free = name;
        for (int n = 2; !taken.Add(free); n++)
            free = $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}";
        return free;
    }
}
