namespace Nexaflow.IO.Common;

/// <summary>
/// A safe recursive directory move: copy the whole tree to the destination, then delete the source.
/// The copy opens each source file with <see cref="FileShare.ReadWrite"/> | <see cref="FileShare.Delete"/>
/// so a file held open elsewhere is still copied — the destination always ends up with a complete copy
/// before anything is removed. Cross-volume safe (never assumes a rename). Cancellation is honoured
/// between files.
/// </summary>
public static class DirectoryMover
{
    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="destination"/> then deletes the source.
    /// Throws if the destination already exists (the caller decides how to resolve a name clash).
    /// </summary>
    public static Task MoveAsync(string source, string destination, CancellationToken ct = default)
        => Task.Run(() =>
        {
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException($"Source folder '{source}' does not exist.");
            if (Directory.Exists(destination))
                throw new IOException($"A folder already exists at '{destination}'.");

            CopyDirectory(source, destination, ct);
            Directory.Delete(source, recursive: true);
        }, ct);

    private static void CopyDirectory(string source, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        var dir = new DirectoryInfo(source);

        foreach (var file in dir.GetFiles())
        {
            ct.ThrowIfCancellationRequested();
            CopyFileShared(file.FullName, Path.Combine(destination, file.Name));
        }

        foreach (var sub in dir.GetDirectories())
        {
            ct.ThrowIfCancellationRequested();
            CopyDirectory(sub.FullName, Path.Combine(destination, sub.Name), ct);
        }
    }

    private static void CopyFileShared(string source, string destination)
    {
        using (var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var to = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            from.CopyTo(to);

        // Best-effort metadata preservation — never fail the copy over an attribute we couldn't set.
        try { File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source)); } catch { }
        try { File.SetAttributes(destination, File.GetAttributes(source)); } catch { }
    }
}
