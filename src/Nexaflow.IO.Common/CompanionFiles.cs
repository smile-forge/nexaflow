namespace Nexaflow.IO.Common;

/// <summary>
/// Moving a file's companions with it — the files named after it plus a suffix (<c>photo.jpg</c> and
/// <c>photo.jpg.nfedit</c>). One implementation for the transfer engine and every single-file operation, so a
/// companion follows its file the same way whichever path moved it.
/// </summary>
public static class CompanionFiles
{
    /// <summary>The companions of <paramref name="primary"/> that exist.</summary>
    public static IEnumerable<string> Existing(string primary, IReadOnlyList<string> suffixes) =>
        suffixes.Select(s => primary + s).Where(p => File.Exists(LongPath.Prefix(p)));

    /// <summary>
    /// The companions of <paramref name="source"/> go to <paramref name="destination"/>'s name — copied, or moved.
    /// Where the source has none, one already at the destination described the file just replaced there, and is
    /// removed. Returns what could not follow; the rest has.
    /// </summary>
    public static IReadOnlyList<(string Path, Exception Error)> Follow(string source, string destination,
                                                                       IReadOnlyList<string> suffixes, bool move)
    {
        var failures = new List<(string, Exception)>();
        foreach (var suffix in suffixes)
        {
            string from = LongPath.Prefix(source + suffix), to = LongPath.Prefix(destination + suffix);
            try
            {
                // A companion is often hidden, and a hidden file refuses to be overwritten.
                if (File.Exists(to)) File.SetAttributes(to, FileAttributes.Normal);

                if (File.Exists(from))
                {
                    if (move) File.Move(from, to, overwrite: true);
                    else File.Copy(from, to, overwrite: true);
                }
                else if (File.Exists(to))
                {
                    File.Delete(to);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add((source + suffix, ex));
            }
        }
        return failures;
    }

    /// <summary>
    /// A deleted file's companions go too — only once the file itself has gone. Returns what could not be deleted.
    /// </summary>
    public static IReadOnlyList<(string Path, Exception Error)> DeleteWith(string primary, IReadOnlyList<string> suffixes)
    {
        var failures = new List<(string, Exception)>();
        if (suffixes.Count == 0 || File.Exists(LongPath.Prefix(primary))) return failures;

        foreach (var companion in Existing(primary, suffixes).ToList())
        {
            try
            {
                var prefixed = LongPath.Prefix(companion);
                File.SetAttributes(prefixed, FileAttributes.Normal);
                File.Delete(prefixed);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add((companion, ex));
            }
        }
        return failures;
    }
}
