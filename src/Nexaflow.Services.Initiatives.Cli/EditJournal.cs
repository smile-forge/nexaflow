using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nexaflow.Services.Initiatives.Graph;

namespace Nexaflow.Services.Initiatives.Cli;

/// <summary>
/// The edits written to a working tree, newest last, each as every file it touched was found and was left — so the last
/// one can be taken back with <c>graph edit undo</c>.
/// <para>
/// This is what makes a dry run a habit worth dropping. An edit is planned, parsed and compiled before it is written
/// whether or not it is a dry run, and it prints what it changed either way; all a dry run added was a second call to
/// write what the first had already shown. So an edit is written, and one that turns out wrong is undone — which puts
/// back exactly what it replaced, and refuses when anything has changed since rather than overwrite later work.
/// </para>
/// <para>
/// On disk rather than in the resident process, so an undo survives that process stopping; kept per working tree, and
/// only the last <see cref="Kept"/>.
/// </para>
/// </summary>
internal static class EditJournal
{
    /// <summary>How many edits are kept to undo, newest first.</summary>
    internal const int Kept = 20;

    private static int _sequence;

    /// <summary>One edit: what it was, when it was written, and each file before and after.</summary>
    internal sealed record Entry(string Label, DateTime WrittenUtc, IReadOnlyList<EditPlan.Written> Files);

    /// <summary>Where a working tree's edits are kept.</summary>
    internal static string DirectoryFor(string codeRoot)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(codeRoot)).ToUpperInvariant())))[..24];
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smile", "nfi", "undo", key);
    }

    /// <summary>Keeps an edit that has just been written. Losing the record costs the undo, never the edit, so a failure
    /// to keep it is swallowed.</summary>
    internal static void Record(string directory, string label, IReadOnlyList<EditPlan.Written> files)
    {
        try
        {
            Directory.CreateDirectory(directory);
            // Ordered by name, and unique within a tick: the clock is too coarse to tell two edits in a script run apart.
            var path = Path.Combine(directory, $"{DateTime.UtcNow.Ticks:D19}-{Interlocked.Increment(ref _sequence):D6}-{Environment.ProcessId}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new Entry(label, DateTime.UtcNow, files)));

            foreach (var old in Entries(directory).SkipLast(Kept)) File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { }
    }

    /// <summary>The newest edit still kept, and the file it is kept in — null when there is none.</summary>
    internal static (Entry Entry, string Path)? Last(string directory)
    {
        foreach (var path in Entries(directory).Reverse())
        {
            try
            {
                if (JsonSerializer.Deserialize<Entry>(File.ReadAllText(path)) is { Files.Count: > 0 } entry) return (entry, path);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return null;
    }

    /// <summary>Drops an edit once it has been undone, so the next undo takes the one before it.</summary>
    internal static void Forget(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static IEnumerable<string> Entries(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal).ToList()
            : [];
}
