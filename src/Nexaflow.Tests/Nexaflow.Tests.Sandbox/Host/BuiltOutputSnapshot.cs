using System.Diagnostics;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Sandbox.Session;

namespace Nexaflow.Tests.Sandbox.Host;

/// <summary>
/// The build a session runs, copied into its folder when it starts: the output folders of the app, the journey suite and
/// this tool, each at its path relative to the checkout, under a manifest (<see cref="BuiltOutput.SnapshotManifestName"/>)
/// that makes <see cref="BuiltOutput"/> look for builds there for anything run from inside it. The agent runs from the copy,
/// so the app it launches and the journeys it runs are the copy's too, and the checkout can be rebuilt while they run.
/// </summary>
/// <param name="TakenAt">When the copy was made.</param>
/// <param name="AppBuiltAt">When the app it holds was built, which is what tells one snapshot from another.</param>
/// <param name="Outputs">The output folders copied, relative to the checkout and to the snapshot alike.</param>
/// <param name="Took">How long the copy took.</param>
internal sealed record BuiltOutputSnapshot(
    DateTimeOffset TakenAt, DateTimeOffset AppBuiltAt, IReadOnlyList<string> Outputs, int Files, long Bytes, TimeSpan Took)
{
    private static readonly TimeSpan RemoveRetry = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Copies <paramref name="outputs"/>, folders under <paramref name="checkout"/>, into <paramref name="root"/>. The manifest
    /// is written last, so a copy that fails part-way is never taken for a snapshot; it is removed instead.
    /// </summary>
    public static BuiltOutputSnapshot Take(string checkout, string root, string app, IReadOnlyList<string> outputs)
    {
        var clock = Stopwatch.StartNew();
        var folders = outputs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var files = folders
            .SelectMany(folder => new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories))
            .DistinctBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        try
        {
            // Hundreds of files, most of them small: copied side by side, a start spends seconds on them rather than minutes.
            Parallel.ForEach(files, file =>
            {
                var landing = Path.Combine(root, Path.GetRelativePath(checkout, file.FullName));
                Directory.CreateDirectory(Path.GetDirectoryName(landing)!);
                file.CopyTo(landing);
            });
        }
        catch (AggregateException ex)
        {
            Remove(root, TimeSpan.Zero);
            throw new SandboxException(
                $"Could not copy the build into {root}; is it being rebuilt right now? {ex.InnerExceptions[0].Message}");
        }

        var snapshot = new BuiltOutputSnapshot(
            DateTimeOffset.Now, File.GetLastWriteTime(app), [.. folders.Select(folder => Path.GetRelativePath(checkout, folder))],
            files.Count, files.Sum(file => file.Length), clock.Elapsed);
        SessionFolder.WriteJson(ManifestFile(root), snapshot);
        return snapshot;
    }

    /// <summary>The snapshot at <paramref name="root"/>, or null when none was taken there.</summary>
    public static BuiltOutputSnapshot? Read(string root) => SessionFolder.ReadJson<BuiltOutputSnapshot>(ManifestFile(root));

    /// <summary>
    /// Deletes the snapshot at <paramref name="root"/>, trying for up to <paramref name="patience"/> while something still
    /// holds a file of it — a sandbox only just stopped lets go of its mapped files a moment later. False when it could not.
    /// </summary>
    public static bool Remove(string root, TimeSpan patience)
    {
        var deadline = DateTime.UtcNow + patience;
        while (true)
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline) return false;
                Thread.Sleep(RemoveRetry);
            }
        }
    }

    /// <summary>What it holds and when it was taken, on one line.</summary>
    public string Describe() =>
        $"{Outputs.Count} output folder(s), {Files:N0} files, {Bytes / (1024.0 * 1024.0):N0} MB, copied at {TakenAt:HH:mm:ss} "
        + $"in {Took.TotalSeconds:0.0}s; the app in it was built {AppBuiltAt:yyyy-MM-dd HH:mm:ss}";

    private static string ManifestFile(string root) => Path.Combine(root, BuiltOutput.SnapshotManifestName);
}
