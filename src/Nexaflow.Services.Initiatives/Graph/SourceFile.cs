using System;
using System.IO;
using System.Linq;
using System.Text;
using Nexaflow.Syntax;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// Reading a source file so it can be written back looking the way it arrived — same byte-order mark, same
/// encoding. The line endings inside it are <see cref="SourceText"/>'s business; this is about the bytes
/// around them.
/// <para>
/// It matters for the same reason the line-ending handling does: writing a BOM where there wasn't one, or
/// dropping one there was, turns a one-line change into a whole-file diff and, for the files that carry one
/// deliberately, can change how another tool reads them. <c>File.ReadAllText</c> silently discards the
/// distinction, so it cannot be used on either side of an edit.
/// </para>
/// </summary>
public static class SourceFile
{
    /// <summary>How many of a directory's files it takes to read its line-ending convention, and how far up
    /// the tree to keep asking. Both are small on purpose: a convention that needs a wide survey to see is not
    /// one, and this runs while creating a file.</summary>
    private const int SampleFiles = 8;
    private const int SampleDepth = 4;

    /// <summary>A file's text and the encoding to write it back with, or null when it cannot be read.</summary>
    public static (string Text, Encoding Encoding)? Read(string fullPath)
    {
        try
        {
            var bytes = File.ReadAllBytes(fullPath);

            if (Starts(bytes, 0xEF, 0xBB, 0xBF))
                return (new UTF8Encoding(true).GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true));
            if (Starts(bytes, 0xFF, 0xFE))
                return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), new UnicodeEncoding(false, true));
            if (Starts(bytes, 0xFE, 0xFF))
                return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), new UnicodeEncoding(true, true));

            return (new UTF8Encoding(false).GetString(bytes), new UTF8Encoding(false));
        }
        catch { return null; }
    }

    /// <summary>
    /// Writes <paramref name="text"/> back, but only if the file is still exactly
    /// <paramref name="expected"/>. An edit is planned against a snapshot; if the file moved on in between,
    /// the offsets in hand describe something that no longer exists and applying them would corrupt it.
    /// </summary>
    /// <returns>Null on success, or why the write was refused.</returns>
    public static string? WriteIfUnchanged(string fullPath, string expected, string text, Encoding encoding)
    {
        var current = Read(fullPath);
        if (current is null) return $"{fullPath} could not be re-read before writing.";
        if (!string.Equals(current.Value.Text, expected, StringComparison.Ordinal))
            return $"{fullPath} changed while the edit was being prepared — nothing written.";

        try { File.WriteAllText(fullPath, text, encoding); return null; }
        catch (Exception ex) { return $"Could not write {fullPath}: {ex.Message}"; }
    }

    /// <summary>
    /// The line ending a file that does not exist yet should be written with: the one its neighbours already
    /// use. A new file has no endings of its own to preserve, so the only thing left to match is the codebase
    /// it is joining — and <see cref="Environment.NewLine"/> is a fact about the machine, not about the
    /// codebase. On Windows that writes CRLF into an LF repository, which is exactly the whole-file diff this
    /// type exists to avoid.
    /// <para>
    /// The directory the file lands in is asked first, then its parents, because a new file usually arrives in
    /// a new directory and the convention it must join lives above it. Only files sharing the new file's
    /// extension are sampled, so nothing has to guess whether a neighbour is text, and a directory whose
    /// sample is evenly split has no convention to report and is passed over. <paramref name="stopAt"/> is the
    /// tree the file belongs to: without it the walk can climb out of the repository and take its answer from
    /// whatever happens to sit above it. <see cref="Environment.NewLine"/> remains the answer when nothing
    /// inside those bounds has an opinion.
    /// </para>
    /// </summary>
    public static string NewlineFor(string fullPath, string? stopAt = null)
    {
        var extension = Path.GetExtension(fullPath);
        var boundary  = stopAt is { Length: > 0 } ? Normalised(stopAt) : null;
        var dir       = Path.GetDirectoryName(Normalised(fullPath));

        for (var up = 0; up < SampleDepth && dir is { Length: > 0 }; up++)
        {
            if (Convention(dir, extension) is { } newline) return newline;
            if (boundary is not null && string.Equals(dir, boundary, PathComparison)) break;
            dir = Path.GetDirectoryName(dir);
        }

        return Environment.NewLine;
    }

    private static string Normalised(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// The line ending most of one directory's files of the new file's kind use, or null when it has none to
    /// report — no such files, or an even split between them. Kind is the extension, so nothing has to guess
    /// whether a neighbour is text; an extensionless file is compared against the other extensionless ones,
    /// which is what a directory of shell hooks is made of.
    /// </summary>
    private static string? Convention(string dir, string extension)
    {
        int crlf = 0, lf = 0;
        try
        {
            var kin = Directory.EnumerateFiles(dir, extension.Length > 0 ? "*" + extension : "*")
                               .Where(f => Path.GetExtension(f).Length == extension.Length)
                               .Take(SampleFiles);

            foreach (var file in kin)
            {
                // A file with no line endings at all has nothing to contribute, and SourceText reports LF for
                // it by default — counting that would let a one-line neighbour speak for the whole directory.
                if (Read(file) is not { Text: var content } || !content.Contains('\n')) continue;

                switch (SourceText.Of(content).Newline)
                {
                    case "\r\n": crlf++; break;
                    case "\n":   lf++;   break;
                }
            }
        }
        catch { return null; }

        return crlf == lf ? null : crlf > lf ? "\r\n" : "\n";
    }

    private static bool Starts(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++) if (bytes[i] != prefix[i]) return false;
        return true;
    }
}
