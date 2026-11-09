namespace Nexaflow.IO.Common;

/// <summary>
/// Where files made for the user are kept until they save them — a picture drawn, a recording, a clip, a cut-out, pages converted
/// to Markdown — each in a folder of its own, so a name never collides, under the app's own local data. Not the temp folder:
/// Windows' clean-up takes files from it while a draft is still open, and an unsaved draft is the only copy there is. The app
/// clears drafts itself instead (<see cref="Sweep"/>): never one open, those untouched for <see cref="Untouched"/>, and the least
/// recently used while all of them come to more than <see cref="Most"/>. A draft opened is marked used (<see cref="Touch"/>).
/// </summary>
public static class DraftLocation
{
    /// <summary>How long a draft nobody opens or writes is kept.</summary>
    public static readonly TimeSpan Untouched = TimeSpan.FromDays(30);

    /// <summary>The most every draft together is let come to, in bytes, before the least recently used are let go.</summary>
    public const long Most = 8L << 30;

    /// <summary>The folder every draft's own folder is in.</summary>
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smile", "nexaflow", "drafts");

    /// <summary>A path named <paramref name="fileName"/> in a new folder of its own under <paramref name="root"/>, the folder made.</summary>
    public static string NewFile(string fileName, string? root = null) => Path.Combine(NewFolder(root), fileName);

    /// <summary>A new folder of a draft's own under <paramref name="root"/>, made — for a draft of several files, a document's working copy.</summary>
    public static string NewFolder(string? root = null) => Directory.CreateDirectory(Path.Combine(root ?? Root, Guid.NewGuid().ToString("N")[..12])).FullName;

    /// <summary>Whether <paramref name="path"/> is a draft, or in one: inside a draft's own folder under <paramref name="root"/>.</summary>
    public static bool Holds(string path, string? root = null) => FolderOf(path, root) is not null;

    /// <summary>Marks the draft holding <paramref name="path"/> as used now, so it is kept; anything not a draft is left alone.</summary>
    public static void Touch(string path, string? root = null)
    {
        if (FolderOf(path, root) is { } folder) CacheLocation.Touch(folder);
    }

    /// <summary>
    /// Every path a tab's <paramref name="parameters"/> name: each value, and each of several joined by <c>|</c>, which no Windows path
    /// holds — for what a tab shows to be found among the drafts.
    /// </summary>
    public static IEnumerable<string> PathsIn(IReadOnlyDictionary<string, string>? parameters) =>
        parameters is null ? [] : parameters.Values.SelectMany(value => value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// Lets go of the drafts no longer wanted: never one holding a path in <paramref name="open"/> — what the tabs show — and of
    /// the rest, those untouched for <see cref="Untouched"/>, then the least recently used while all of them come to more than
    /// <see cref="Most"/>.
    /// </summary>
    public static void Sweep(IEnumerable<string> open, string? root = null, DateTime? now = null)
    {
        root ??= Root;
        if (!Directory.Exists(root)) return;

        var inUse = open.Select(path => FolderOf(path, root)).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var oldest = (now ?? DateTime.UtcNow) - Untouched;

        foreach (var folder in Directory.EnumerateDirectories(root).Select(Path.GetFullPath))
            if (!inUse.Contains(folder) && CacheLocation.LastUsed(folder) < oldest) CacheLocation.Remove(folder);

        CacheLocation.Bound(root, Most, inUse);
    }

    /// <summary>The draft's own folder holding <paramref name="path"/>, in full; null where it is in none.</summary>
    private static string? FolderOf(string path, string? root)
    {
        string full, under;
        try
        {
            full = Path.GetFullPath(path);
            under = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root ?? Root)) + Path.DirectorySeparatorChar;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (!full.StartsWith(under, StringComparison.OrdinalIgnoreCase)) return null;
        var inside = full[under.Length..];
        var own = inside.IndexOf(Path.DirectorySeparatorChar);
        return own > 0 ? under + inside[..own] : null;
    }
}
