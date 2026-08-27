using System;
using System.IO;
using System.Text;

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

    private static bool Starts(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++) if (bytes[i] != prefix[i]) return false;
        return true;
    }
}
