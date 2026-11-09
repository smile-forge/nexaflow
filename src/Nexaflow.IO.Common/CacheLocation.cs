using System.Diagnostics;

namespace Nexaflow.IO.Common;

/// <summary>
/// Where the app keeps what it can make again — a reading of pages, a picture tool's result, a model's notes on a document — each
/// kind in a folder of its own under the app's local data. Not the temp folder: Windows' clean-up takes files from it while what
/// they belong to is still in use. Whoever keeps something holds it under a budget of their own (<see cref="Bound"/>); what one
/// process works on while it runs is kept in a folder of that session's own (<see cref="SessionFolder"/>).
/// </summary>
public static class CacheLocation
{
    /// <summary>The folder every kind of kept thing has its folder in.</summary>
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smile", "nexaflow", "cache");

    /// <summary>The folder what is kept under <paramref name="name"/> goes in.</summary>
    public static string Folder(string name) => Path.Combine(Root, name);

    /// <summary>
    /// The folder this process keeps its working copies of kind <paramref name="name"/> in — an archive's entry opened, a font
    /// unpacked, a message's body written out, a file's unsaved edits — made on first asking. Its owner lets go of what it put
    /// there; what a process that ended without doing so left behind is let go, in the background, the first time another asks
    /// for that kind (<see cref="LetGoOfEndedSessions"/>), so nothing waits on Windows' clean-up of the temp folder, which takes
    /// files still in use.
    /// </summary>
    public static string SessionFolder(string name, string? root = null)
    {
        var kind = Path.Combine(root ?? Root, name);
        var mine = Path.Combine(kind, ThisSession);

        Directory.CreateDirectory(mine);

        // What ended sessions left is let go of off the asking thread: often the UI's, at start-up, and after a crash it can be
        // gigabytes of an archive's entries.
        bool first;
        lock (Swept) first = Swept.Add(kind);
        if (first) _ = Task.Run(() => LetGoOfEndedSessions(kind));

        return mine;
    }

    /// <summary>
    /// Lets go of the folders under <paramref name="kind"/> that sessions no longer running left behind (<see cref="SessionFolder"/>),
    /// never this one's, nor a folder not named as a session.
    /// </summary>
    public static void LetGoOfEndedSessions(string kind)
    {
        try
        {
            if (!Directory.Exists(kind)) return;
            foreach (var folder in Directory.EnumerateDirectories(kind))
                if (Ended(Path.GetFileName(folder))) Remove(folder);
        }
        catch (IOException) { /* the folder went or moved under it: the next session tries again */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>The kinds whose ended sessions this process has let go of already.</summary>
    private static readonly HashSet<string> Swept = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// This process's session: its id, which says whether it still runs, and one of its own, so a later process given the same id
    /// is not taken for it. Nothing here asks the system anything, so the first thread to ask for a folder — often the UI's, at
    /// start-up — waits on nothing.
    /// </summary>
    private static readonly string ThisSession = $"{Environment.ProcessId}-{Guid.NewGuid():N}";

    /// <summary>
    /// Whether the session <paramref name="session"/> names has ended: no process runs with its id. One whose id a later process
    /// has taken waits until that one ends too — later, never wrongly; one that cannot be asked about is taken as running, and a
    /// folder not named as a session is left alone.
    /// </summary>
    private static bool Ended(string session)
    {
        var parts = session.Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var id) || parts[1].Length == 0) return false;

        try
        {
            using var process = Process.GetProcessById(id);
            return process.HasExited;
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
        catch (System.ComponentModel.Win32Exception) { return false; }
        catch (NotSupportedException) { return false; }
    }

    /// <summary>Marks <paramref name="folder"/> as used now, so a budget lets it go last.</summary>
    public static void Touch(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.SetLastWriteTimeUtc(folder, DateTime.UtcNow);
        }
        catch (IOException) { /* in use or read-only: it is only let go a little sooner */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>When the folder <paramref name="folder"/> was last used: touched, or anything in it written.</summary>
    public static DateTime LastUsed(string folder)
    {
        var used = Directory.GetLastWriteTimeUtc(folder);
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var written = File.GetLastWriteTimeUtc(file);
            if (written > used) used = written;
        }
        return used;
    }

    /// <summary>
    /// Holds the folders directly under <paramref name="root"/> under <paramref name="most"/> bytes, letting go of whole folders,
    /// those used longest ago first (<see cref="LastUsed"/>), never one <paramref name="keep"/> names. One still open somewhere is
    /// left for the next to let go.
    /// </summary>
    public static void Bound(string root, long most, IReadOnlySet<string>? keep = null)
    {
        if (!Directory.Exists(root)) return;

        var folders = Directory.EnumerateDirectories(root)
            .Where(folder => keep is null || !keep.Contains(Path.GetFullPath(folder)))
            .Select(folder => (Folder: folder, Bytes: Bytes(folder), Used: LastUsed(folder)))
            .OrderBy(folder => folder.Used)
            .ToList();

        var total = Directory.EnumerateDirectories(root).Sum(Bytes);

        foreach (var folder in folders)
        {
            if (total <= most) return;
            if (Remove(folder.Folder)) total -= folder.Bytes;
        }
    }

    /// <summary>Removes <paramref name="folder"/> and all it holds; false where something in it is in use, so it stays.</summary>
    public static bool Remove(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static long Bytes(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
}
