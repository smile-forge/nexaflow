namespace Nexaflow.Interaction;

/// <summary>
/// Where files that arrive as content are written, so they have paths once the drag is over.
/// <para>
/// The names come from another application — a web page can choose them — so each is cut down to plain names inside the
/// folder the drop was given, and each file is marked as come from the internet, since where it came from cannot be
/// known.
/// </para>
/// </summary>
public sealed class DropStaging(string root)
{
    private const int LongestName = 120;
    private static readonly TimeSpan Kept = TimeSpan.FromDays(7);
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>Windows' own rules, whatever this runs on: the names are for a Windows disk.</summary>
    private static readonly char[] NotInAName = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private int _swept;

    public static DropStaging Default { get; } = new(Path.Combine(Path.GetTempPath(), "Nexaflow", "drops"));

    /// <summary>
    /// Writes <paramref name="files"/> to a folder of their own and returns what was dragged at the top of it: the files
    /// and the folders, not what is inside those folders.
    /// </summary>
    public IReadOnlyList<string> Save(IReadOnlyList<CarriedFile> files)
    {
        if (files.Count == 0) return [];
        SweepOnce();

        var folder = Path.Combine(root, Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(folder);
        try
        {
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var top = new List<string>();

            foreach (var file in files)
            {
                var parts = SafeParts(file.Name);
                var path = Path.Combine([folder, .. parts]);
                if (file.IsFolder)
                {
                    Directory.CreateDirectory(path);
                }
                else
                {
                    path = Unused(path, written);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using (var stream = File.Create(path)) file.CopyTo(stream);
                    MarkFromInternet(path);
                }

                written.Add(path);
                var outermost = parts.Count == 1 ? path : Path.Combine(folder, parts[0]);
                if (!top.Contains(outermost, StringComparer.OrdinalIgnoreCase)) top.Add(outermost);
            }
            return top;
        }
        catch
        {
            // All or nothing: half a drop is not what was dragged. The failure itself is the caller's to report.
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary><paramref name="name"/> as plain names, none of which can climb out, name a device or name a stream.</summary>
    internal static IReadOnlyList<string> SafeParts(string name)
    {
        var parts = name.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(SafeName)
            .Where(part => part.Length > 0)
            .ToList();
        return parts.Count > 0 ? parts : ["dropped"];
    }

    private static string SafeName(string part)
    {
        var safe = new string([.. part.Select(c => char.IsControl(c) || NotInAName.Contains(c) ? '_' : c)]).Trim().TrimEnd('.', ' ');
        if (safe.Length > LongestName)
        {
            var extension = Path.GetExtension(safe);
            if (extension.Length > LongestName / 2) extension = string.Empty;
            safe = safe[..(LongestName - extension.Length)] + extension;
        }
        return Reserved.Contains(safe.Split('.')[0].TrimEnd()) ? "_" + safe : safe;
    }

    private static string Unused(string path, HashSet<string> written)
    {
        var candidate = path;
        for (var n = 2; written.Contains(candidate); n++)
            candidate = Path.Combine(Path.GetDirectoryName(path)!,
                                     $"{Path.GetFileNameWithoutExtension(path)} ({n}){Path.GetExtension(path)}");
        return candidate;
    }

    private static void MarkFromInternet(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n"); }
        catch (IOException) { /* a volume without alternate streams has nowhere to keep the mark */ }
    }

    private void SweepOnce()
    {
        if (Interlocked.Exchange(ref _swept, 1) == 1 || !Directory.Exists(root)) return;

        // Off the drop's thread, and only drops long since taken: each is a folder of this class's own making.
        _ = Task.Run(() =>
        {
            foreach (var old in new DirectoryInfo(root).EnumerateDirectories())
            {
                if (old.Name.Length != 12 || DateTime.UtcNow - old.CreationTimeUtc < Kept) continue;
                try { old.Delete(recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        });
    }
}
