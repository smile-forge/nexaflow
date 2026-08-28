using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// Whether a built graph still describes what is on disk, and which files it does not.
/// <para>
/// The point is to let a query say something definite. "Here is what I found" leaves the reader to wonder
/// whether the graph was current, and wondering is what makes a tool feel untrustworthy — so a query says
/// either <i>this is current</i> or <i>N files have moved on, re-run with --refresh</i>. Both are answers;
/// silence is not.
/// </para>
/// <para>
/// Timestamps, not hashes. Re-hashing the repository costs 1.8s warm and <b>169s cold</b> — reading half a
/// gigabyte is the dominant cost of a full build, and paying it per query would be absurd. And the baseline
/// needs no new stored field: <c>graph.json</c>'s own write time is when the graph last described the world,
/// so anything modified since is what it has not seen.
/// </para>
/// <para>
/// The halves are scoped differently, because they are different questions. <b>Changed</b> and <b>absent</b>
/// are answered by stat-ing the files the graph already recorded — no directory walk at all. <b>Added</b>
/// needs a walk, and that is narrowed to the directories the solution's projects live in: scanning the whole
/// repository means 17,038 files, of which 14,849 are pinned submodule test corpora that cannot gain a file
/// without a deliberate pin bump. Project-scoped it is 4,009 files, and the whole check lands around 100ms.
/// </para>
/// <para>
/// <b>Absent is reported, never acted on.</b> <c>graph.json</c> is shared with every worktree, and a branch
/// that runs a build publishes its own files into it — so a file the graph knows and this tree does not is
/// as likely to be a parallel session's work in progress as it is to be deleted. Nothing here can tell those
/// apart, and guessing wrong deletes someone else's feature from the shared graph. A full <c>graph build</c>
/// is what reconciles deletions, because it sees the whole tree rather than one path.
/// </para>
/// <para>
/// It errs towards saying "stale". A file touched and reverted, or rewritten with identical content, is
/// reported as changed — a needless refresh costs one parse, whereas a missed one is a wrong answer
/// delivered confidently.
/// </para>
/// </summary>
public static class GraphFreshness
{
    /// <param name="Known">How many files the graph was built from.</param>
    /// <param name="Available">False when the check could not run — nothing was built, so nothing is claimed.</param>
    /// <param name="Absent">Files the graph describes that are not in this tree. NOT the same as deleted —
    /// <c>graph.json</c> is shared, and a branch that runs a build publishes its own files into it, so these
    /// are as likely to be another session's work in progress. Reported, never acted on.</param>
    /// <param name="OtherBranch">The graph was built from a different working tree than the one queried —
    /// so most of the difference is the branch, not edits since.</param>
    public sealed record Report(
        int Known,
        IReadOnlyList<string> Changed,
        IReadOnlyList<string> Added,
        IReadOnlyList<string> Absent,
        bool Available,
        bool OtherBranch = false)
    {
        public static readonly Report Unknown = new(0, [], [], [], false);

        /// <summary>Drift that makes this answer wrong. An absent file does not: it means the graph knows
        /// something extra, which is a different (and much less alarming) thing than knowing something
        /// out of date.</summary>
        public int Drifted => Changed.Count + Added.Count;

        public bool IsCurrent => Available && Drifted == 0;

        /// <summary>
        /// Every file worth re-reading. Deliberately excludes <see cref="Absent"/>: re-reading a file that
        /// is not here can only mean removing it from the graph, and doing that on a guess would delete a
        /// parallel branch's published work. A full <c>graph build</c> is what reconciles deletions.
        /// </summary>
        public IReadOnlyList<string> Stale =>
            [.. Changed.Concat(Added).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        /// <summary>One line for a human or an agent — definite either way.</summary>
        public string Summary()
        {
            if (!Available) return "graph: not built yet — nothing to be stale.";

            // Kept separate from the verdict, and phrased as extra knowledge rather than as a fault. From a
            // shared graph these are usually another branch's in-flight work, and reporting someone else's
            // feature as "removed" is both wrong and the sort of noise that gets a warning ignored.
            var aside = Absent.Count == 0 ? ""
                : $" {Absent.Count:N0} file(s) it describes are not in this tree — another branch's work, or "
                + "removed; a full `graph build` settles which.";

            if (IsCurrent)
                return $"graph: current for this tree — {Known:N0} files, none changed since it was built.{aside}";

            var parts = new List<string>();
            if (Changed.Count > 0) parts.Add($"{Changed.Count} changed");
            if (Added.Count   > 0) parts.Add($"{Added.Count} added");

            // From a worktree the graph was built from another branch, so the count is partly branch
            // difference rather than edits since. Saying that stops a large, permanent-looking number
            // reading as alarm — which would train exactly the shrug this whole check exists to prevent.
            var why = OtherBranch
                ? " (the graph was built from a different branch, so some of this is that). Queries that read "
                + "source already use your tree; --refresh brings the graph's own record across."
                : " — this answer may be out of date. Re-run with --refresh to fold them in first.";

            return $"graph: {string.Join(", ", parts)} vs this working tree{why}{aside}";
        }
    }

    /// <summary>
    /// Compares the files the graph was built from against what is on disk now.
    /// </summary>
    /// <param name="known">Repo-relative paths the graph parsed — <c>GraphCache.Files.Keys</c>.</param>
    /// <param name="codeRoot">Where source is read from: the caller's worktree, or the product root.</param>
    /// <param name="graphFile">The built <c>graph.json</c>, whose write time is the baseline.</param>
    public static Report Check(IReadOnlyCollection<string> known, string codeRoot, string graphFile,
                               bool otherBranch = false)
    {
        DateTime baseline;
        try
        {
            if (!File.Exists(graphFile)) return Report.Unknown;
            baseline = File.GetLastWriteTimeUtc(graphFile);
        }
        catch { return Report.Unknown; }
        if (known.Count == 0) return Report.Unknown;

        var changed = new List<string>();
        var absent  = new List<string>();

        // Changed and absent: a stat each, over the set the graph already knows. No walk, and it covers the
        // thousand-odd files that live outside any project directory (build props, the product exports).
        foreach (var rel in known)
        {
            var full = Path.Combine(codeRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                var info = new FileInfo(full);
                if (!info.Exists) absent.Add(rel);
                else if (info.LastWriteTimeUtc > baseline) changed.Add(rel);
            }
            catch { }                                    // unreadable is not evidence of anything
        }

        var recorded = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
        var added    = new List<string>();

        foreach (var dir in ScanRoots(known, codeRoot))
        {
            try
            {
                foreach (var full in RepoFiles.EnumerateSource(dir, 100_000))
                {
                    var rel = Path.GetRelativePath(codeRoot, full).Replace('\\', '/');
                    if (!recorded.Contains(rel)) added.Add(rel);
                }
            }
            catch { }
        }

        changed.Sort(StringComparer.Ordinal);
        absent.Sort(StringComparer.Ordinal);

        return new Report(known.Count,
                          changed,
                          [.. added.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)],
                          absent,
                          true,
                          otherBranch);
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
}
