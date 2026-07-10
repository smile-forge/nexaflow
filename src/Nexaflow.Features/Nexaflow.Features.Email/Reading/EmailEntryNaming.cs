using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nexaflow.Features.Email.Reading;

/// <summary>
/// Turns a part's raw display name into a stable, single-segment, collision-free VFS entry name. Shared by
/// both format readers so the entry name is derived identically no matter how the message was parsed. The
/// result must round-trip through the virtual file system: it can never contain a path separator or a
/// character that <c>Path</c>/<c>File.Exists</c> rejects (which would make
/// <c>VirtualFileSystem.FindFirstRealFile</c> silently fail to resolve the entry).
/// </summary>
internal static class EmailEntryNaming
{
    private static readonly char[] Invalid =
        [.. Path.GetInvalidFileNameChars().Concat(['/', '\\']).Distinct()];

    /// <summary>
    /// Sanitises <paramref name="displayName"/> to a legal single path segment and de-duplicates it against
    /// <paramref name="used"/> (case-insensitive, matching the VFS's <c>OrdinalIgnoreCase</c> comparisons),
    /// appending <c>" (2)"</c>, <c>" (3)"</c>… before the extension on collision. Adds the chosen name to
    /// <paramref name="used"/> and returns it.
    /// </summary>
    public static string SafeUnique(string displayName, ISet<string> used)
    {
        var name = Sanitize(displayName);
        if (name.Length == 0) name = "part";

        var candidate = name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        int n = 2;
        while (!used.Add(candidate.ToLowerInvariant()))
            candidate = $"{stem} ({n++}){ext}";
        return candidate;
    }

    private static string Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        // Strip any directory portion a MIME filename might carry, then scrub illegal chars.
        var leaf = raw.Replace('\\', '/');
        int slash = leaf.LastIndexOf('/');
        if (slash >= 0) leaf = leaf[(slash + 1)..];

        var sb = new StringBuilder(leaf.Length);
        foreach (var c in leaf)
            sb.Append(Array.IndexOf(Invalid, c) >= 0 ? '_' : c);
        return sb.ToString().Trim().Trim('.');
    }
}
