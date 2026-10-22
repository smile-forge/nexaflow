using System.Collections.Concurrent;

namespace Nexaflow.Visuals.Common.Shell;

/// <summary>
/// The shell's name for a kind of file — "Text Document", "File folder" — as Explorer's Type column shows it. Read from the
/// registry once per extension and kept, so a background enumerator can name every row it builds.
/// </summary>
public static class ShellFileTypes
{
    private static readonly ConcurrentDictionary<(string Extension, bool IsFolder), string> Names = new();

    /// <summary>
    /// The type name of files ending <paramref name="extension"/> (with its dot, or empty for none), or of a folder. Where
    /// the shell gives no name, the extension itself in capitals, so the column still tells the rows apart.
    /// </summary>
    public static string NameFor(string extension, bool isFolder) =>
        Names.GetOrAdd((isFolder ? string.Empty : extension.ToLowerInvariant(), isFolder), static key =>
        {
            string name = ShellInterop.TypeName(key.Extension, key.IsFolder);
            return name.Length > 0 ? name : key.Extension.TrimStart('.').ToUpperInvariant();
        });
}
