using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.IO.Common;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Graph.Store;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// Which files a graph no longer describes as they stand on disk: the one judgement every currency check makes.
/// <para>
/// A file is judged by its stamp — length and write time, recorded when it was last read (see
/// <see cref="GraphSnapshot.Files"/>) — so the question costs a stat and no read. A stamp that disagrees means "read
/// it and see", never "it changed": a file touched and put back reads as stale, and re-reading it finds the same
/// content and costs one hash. A file that is code and has never been stamped is stale for the same reason.
/// </para>
/// <para>
/// <see cref="Scan"/> asks it of every file the graph knows, for when nothing has said what moved: a process
/// starting, a watcher that lost track. <see cref="Of"/> asks it of only the paths a watcher reported, which is what
/// lets a warm process answer without looking at the other ten thousand.
/// </para>
/// <para>
/// Additions need a walk, and a full scan narrows it to the directories the solution's projects live in: the whole
/// repository is 17,038 files, 14,849 of them pinned submodule test corpora that cannot gain a file without a
/// deliberate pin bump. Absences are reported, not acted on — whether an absent file is gone or was only ever
/// another tree's is the graph's owner's call.
/// </para>
/// </summary>
public static class GraphFreshness
{
    /// <param name="Stale">Files whose stamp no longer holds, each with its directory entry as the scan found it.</param>
    /// <param name="Added">Code files the graph does not know.</param>
    /// <param name="Absent">Files the graph knows that are not on disk.</param>
    public sealed record Drift(IReadOnlyDictionary<string, FileStamp> Stale, IReadOnlyList<string> Added,
                               IReadOnlyList<string> Absent)
    {
        public static readonly Drift None = new(new Dictionary<string, FileStamp>(), [], []);

        public bool IsEmpty => Stale.Count == 0 && Added.Count == 0 && Absent.Count == 0;
    }

    /// <summary>Every file <paramref name="snapshot"/> knows, judged against <paramref name="codeRoot"/>, and the
    /// project directories walked for code it does not.</summary>
    public static Drift Scan(GraphSnapshot snapshot, string codeRoot)
    {
        var known = snapshot.KnownFiles;
        var found = new Found(snapshot, codeRoot);
        foreach (var rel in known) found.Judge(rel);

        foreach (var dir in ScanRoots(known, codeRoot))
            foreach (var full in RepoFiles.EnumerateSource(dir, 100_000))
                found.Consider(full, known);

        return found.Result();
    }

    /// <summary>
    /// Only what <paramref name="changed"/> — full paths a watcher reported, files or directories — can have moved.
    /// A directory stands for everything the graph knows beneath it and every code file now in it, which is how a
    /// folder renamed or deleted whole is told about by one notification.
    /// </summary>
    public static Drift Of(GraphSnapshot snapshot, string codeRoot, IEnumerable<string> changed) =>
        OfReported(snapshot, codeRoot, changed, _ => true);

    /// <summary>
    /// Only what a watcher's <paramref name="batch"/> reported, as <see cref="Of(GraphSnapshot, string, IEnumerable{string})"/>
    /// — except that a directory only written to stands for nothing. Its entries changed, and each of them is in the batch
    /// by its own name; walking it again for every file saved in it is what made the graph's own writes cost a look at
    /// their whole folder.
    /// </summary>
    public static Drift Of(GraphSnapshot snapshot, string codeRoot, TreeChangeBatch batch) =>
        OfReported(snapshot, codeRoot, batch.Paths ?? [], full => batch.Moved?.Contains(full) ?? true);

    private static Drift OfReported(GraphSnapshot snapshot, string codeRoot, IEnumerable<string> changed,
                                    Func<string, bool> cameOrWent)
    {
        var known = snapshot.KnownFiles;
        var found = new Found(snapshot, codeRoot);
        var fresh = new List<string>();

        foreach (var full in changed.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Relative(codeRoot, full) is not { } rel) continue;
            if (known.Contains(rel)) { found.Judge(rel); continue; }

            if (Directory.Exists(full))
            {
                if (!cameOrWent(full)) continue;
                foreach (var within in Beneath(known, rel)) found.Judge(within);
                foreach (var file in RepoFiles.EnumerateSource(full, 100_000)) found.Consider(file, known);
            }
            else if (File.Exists(full)) fresh.Add(full);
            else foreach (var within in Beneath(known, rel)) found.Judge(within);   // a directory that went whole
        }

        foreach (var full in RepoFiles.Indexable(codeRoot, fresh)) found.Consider(full, known);
        return found.Result();
    }

    /// <summary>The scan's working set, so the two entry points judge a file the same way.</summary>
    private sealed class Found(GraphSnapshot snapshot, string codeRoot)
    {
        private readonly Dictionary<string, FileStamp> _stale = new(StringComparer.Ordinal);
        private readonly HashSet<string> _added  = new(StringComparer.Ordinal);
        private readonly HashSet<string> _absent = new(StringComparer.Ordinal);

        public void Judge(string rel)
        {
            FileInfo info;
            try { info = new FileInfo(Path.Combine(codeRoot, rel.Replace('/', Path.DirectorySeparatorChar))); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return; }

            if (!info.Exists) { _absent.Add(rel); return; }

            var now = new FileStamp(string.Empty, info.Length, info.LastWriteTimeUtc);
            if (snapshot.Files.TryGetValue(rel, out var stamp) && stamp.IsKnown)
            {
                if (!stamp.Matches(now.Length, now.ModifiedUtc)) _stale[rel] = now;
            }
            // Never stamped: read it if its content is the graph's business, else there is nothing to compare.
            else if (snapshot.Cache.Files.ContainsKey(rel) || RepoFiles.IsExtracted(rel)) _stale[rel] = now;
        }

        public void Consider(string full, IReadOnlySet<string> known)
        {
            if (Relative(codeRoot, full) is { } rel && !known.Contains(rel)) _added.Add(rel);
        }

        public Drift Result() => _stale.Count == 0 && _added.Count == 0 && _absent.Count == 0
            ? GraphFreshness.Drift.None
            : new Drift(_stale, [.. _added.Order(StringComparer.Ordinal)], [.. _absent.Order(StringComparer.Ordinal)]);
    }

    private static IEnumerable<string> Beneath(IReadOnlySet<string> known, string dir)
    {
        var prefix = dir.TrimEnd('/') + "/";
        return known.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Relative(string codeRoot, string full)
    {
        var rel = Path.GetRelativePath(codeRoot, full);
        return rel == "." || rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)
            ? null
            : rel.Replace('\\', '/');
    }

    /// <summary>
    /// Where a genuinely new file could turn up: the directories the solution's projects live in, taken from
    /// the <c>.csproj</c> paths the graph already recorded, with nested ones folded into their parent so
    /// nothing is walked twice. Falls back to the whole tree when there are no projects to go on.
    /// </summary>
    private static IReadOnlyList<string> ScanRoots(IReadOnlyCollection<string> known, string codeRoot)
    {
        var dirs = known
            .Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Concat(SolutionProjects(codeRoot))
            .Select(p => Path.GetDirectoryName(p)?.Replace('\\', '/'))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList()!;

        if (dirs.Count == 0) return [codeRoot];

        return [.. dirs
            .Where(d => !dirs.Any(other => other!.Length < d!.Length
                                        && d.StartsWith(other + "/", StringComparison.OrdinalIgnoreCase)))
            .Select(d => Path.Combine(codeRoot, d!.Replace('/', Path.DirectorySeparatorChar)))
            .Where(Directory.Exists)];
    }

    /// <summary>
    /// The projects a solution file at the root of the tree names. The graph's own list holds only the projects it has read,
    /// so a project added since was never walked: a file in it that no edit had touched stayed invisible to every query,
    /// while the check reported the graph current.
    /// </summary>
    private static List<string> SolutionProjects(string codeRoot)
    {
        var found = new List<string>();
        try
        {
            foreach (var solution in Directory.EnumerateFiles(codeRoot, "*.sln*")
                                              .Where(f => f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                                                       || f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)))
                foreach (Match match in ProjectInSolution.Matches(File.ReadAllText(solution)))
                    found.Add(match.Groups[1].Value.Replace('\\', '/'));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return found;
    }

    private static readonly Regex ProjectInSolution = new(@"[""']([^""'\r\n]+?\.csproj)[""']", RegexOptions.IgnoreCase);
}
