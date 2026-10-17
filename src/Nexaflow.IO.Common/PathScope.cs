namespace Nexaflow.IO.Common;


/// <summary>
/// The files and folders a user has put in front of the assistant — the tabs it is given — and whether a path a tool is
/// handed falls inside them: one of those files, or one of those folders or anything under it, never above it.
/// C:\temp\special holds C:\temp\special\other, never C:\temp, nor C:\temp\specialist.
/// </summary>
/// <remarks>
/// The scope keeps the assistant's reach the same as what the user can see, read as the user reads a path: made full, so
/// ".." climbs no further than it says; short 8.3 names spelled out; a device or long-path prefix taken off; case ignored;
/// a folder's name ending at a separator. A link inside a folder is followed as the user can follow it there — a risk the
/// user made is theirs, not the scope's to shield them from. A relative path is no path here: a tool resolves it against
/// what its page shows before asking.
/// </remarks>
public sealed class PathScope
{
    /// <summary>Nothing in front of the assistant: no path is inside.</summary>
    public static PathScope None { get; } = new([], []);

    private readonly string[] _files;
    private readonly string[] _folders;

    /// <param name="files">Files given one by one — an open picture or document.</param>
    /// <param name="folders">Folders given whole — a folder a file browser shows — with everything under them.</param>
    public PathScope(IEnumerable<string> files, IEnumerable<string> folders)
    {
        _files = [.. files.Select(Full).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];
        _folders = [.. folders.Select(Full).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The files given one by one, made full.</summary>
    public IReadOnlyList<string> Files => _files;

    /// <summary>The folders given whole, made full.</summary>
    public IReadOnlyList<string> Folders => _folders;

    /// <summary>Nothing is given.</summary>
    public bool IsEmpty => _files.Length == 0 && _folders.Length == 0;

    /// <summary>
    /// <paramref name="path"/>, made full, where it is inside the scope — one of its files, or in one of its folders or
    /// under it — to read or to write; null where it is not, or is no full path. Only a folder holds files not yet there.
    /// </summary>
    public string? Resolve(string? path)
    {
        if (Full(path) is not { } full) return null;
        if (_files.FirstOrDefault(file => file.Equals(full, StringComparison.OrdinalIgnoreCase)) is { } given) return given;
        return _folders.Any(folder => IsWithin(full, folder)) ? full : null;
    }

    /// <summary>
    /// <paramref name="path"/>, made full, where it is in one of the scope's folders or under it — where a new file may be
    /// written; null where it is not. A file given alone holds only itself, never a file made beside it.
    /// </summary>
    public string? ResolveInFolder(string? path)
        => Full(path) is { } full && _folders.Any(folder => IsWithin(full, folder)) ? full : null;

    /// <summary>The scope with <paramref name="other"/>'s files and folders too.</summary>
    public PathScope With(PathScope other) => new([.. _files, .. other._files], [.. _folders, .. other._folders]);

    /// <summary>Whether <paramref name="full"/> is <paramref name="folder"/> or under it — at a separator, so a folder's
    /// name never matches the start of another's — case ignored. Both are taken as written: make them full first.</summary>
    public static bool IsWithin(string full, string folder)
    {
        var path = Trimmed(full);
        var root = Trimmed(folder);
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        // A separator at the end said nothing more, but a root's own is part of it.
        static string Trimmed(string path)
            => path.Length > (Path.GetPathRoot(path)?.Length ?? 0) ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : path;
    }

    /// <summary>
    /// <paramref name="path"/> as the user reads it, made full: a long-path or device prefix before a drive or share taken
    /// off, ".." and "." worked out, short 8.3 names spelled out, no separator at its end but a root's. Null for anything
    /// that is no plain path on a drive or share — relative, a device, a named stream, a wildcard.
    /// </summary>
    public static string? Full(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var text = path.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (text.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) text = @"\\" + text[8..];
        else if (text.StartsWith(@"\\?\", StringComparison.Ordinal) || text.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            // Only a drive behind the prefix is a plain path; a volume, a pipe or a raw device is not one a user reads.
            text = text[4..];
            if (!(text.Length >= 2 && char.IsAsciiLetter(text[0]) && text[1] == ':')) return null;
        }
        if (!Path.IsPathFullyQualified(text) || text.IndexOfAny(['*', '?', '<', '>', '|', '"']) >= 0) return null;
        // A colon past the drive names a stream inside a file, or a device: not a place a user opens.
        if (text.IndexOf(':', text.Length >= 2 && text[1] == ':' ? 2 : 0) >= 0) return null;
        string full;
        try
        {
            full = Path.GetFullPath(text);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        if (full.Contains('~')) full = LongNames(full);
        var root = Path.GetPathRoot(full) ?? "";
        return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar) : full;
    }

    /// <summary>Each part of <paramref name="full"/> that is a short 8.3 name of something there spelled out as its long
    /// name — the parent listed for it, which matches short names as Windows does; the rest left as written.</summary>
    private static string LongNames(string full)
    {
        var root = Path.GetPathRoot(full)!;
        var parts = full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var built = root;
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Contains('~'))
            {
                try
                {
                    var found = new DirectoryInfo(built).EnumerateFileSystemInfos(part).FirstOrDefault();
                    if (found is not null) part = found.Name;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
            built = Path.Combine(built, part);
        }
        return built;
    }
}
