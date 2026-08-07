using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Nexaflow.IO.Common;
using Nexaflow.Search;

namespace Nexaflow.Features.VirtualDisk.Services;

/// <summary>
/// Walks a disk image looking for entries whose name or in-image path satisfies a query.
/// <para>
/// It exists because the contents tree is <b>lazy</b>: only the folders the user opened have ever been
/// read, so searching what is on screen would answer "is it in the three folders you clicked" — a question
/// nobody asked, and one whose empty answer reads as "not in this image".
/// </para>
/// <para>
/// Pure IO — it reads directories through the virtual file system and never touches the page, so the whole
/// walk runs off the UI thread. Everything that bounds it lives here: a visited-folder cap, a hit cap, and
/// a cancellation token checked per folder rather than per subtree.
/// </para>
/// </summary>
internal static class DiskSearchScanner
{
    /// <summary>One entry inside the image that satisfied the query. Carries what the row needs so the
    /// filtered tree can be built from the scan alone, without a second read of the image.</summary>
    /// <param name="InnerPath">Path inside the image, forward-slash separated (e.g. <c>docs/guide.txt</c>).</param>
    internal readonly record struct DiskMatch(string InnerPath, bool IsFolder, long Size, DateTime Modified);

    internal readonly record struct ScanResult(
        IReadOnlyList<DiskMatch> Matches, int FoldersVisited, bool Truncated);

    /// <summary>
    /// Breadth-first from the image root. Breadth-first on purpose: under a cap, the matches worth keeping
    /// are the shallow ones, where a depth-first walk would spend the whole budget inside the first branch
    /// it happened to enter.
    /// </summary>
    internal static ScanResult Scan(
        IVirtualFileSystem vfs, string diskPath, SearchRequest request,
        int folderCap, int hitCap, CancellationToken ct)
    {
        var matches = new List<DiskMatch>();
        var queue   = new Queue<string>();
        queue.Enqueue(string.Empty);          // "" = the image root

        var visited   = 0;
        var truncated = false;

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            if (visited >= folderCap) { truncated = true; break; }

            var inner = queue.Dequeue();
            visited++;

            IReadOnlyList<VirtualEntry> entries;
            // A directory the filesystem driver can't read is skipped rather than reported: a damaged or
            // partly-unsupported image still has the rest of its tree worth searching.
            try { entries = vfs.EnumerateEntries(Full(diskPath, inner)); }
            catch (Exception) { continue; }

            foreach (var e in Order(entries))
            {
                // Inside the loop as well as outside it: one folder can hold more entries than the whole
                // hit budget, and a cap only checked between folders would sail past it.
                if (matches.Count >= hitCap) { truncated = true; break; }

                var path = inner.Length == 0 ? e.Name : $"{inner}/{e.Name}";

                // Name or path: a name-scoped term (a glob) is judged against the name alone, everything
                // else may be satisfied by either.
                if (request.MatchesFile(e.Name, path))
                    matches.Add(new DiskMatch(path, e.IsDirectory, e.Size, e.Modified));

                if (e.IsDirectory) queue.Enqueue(path);
            }

            if (truncated) break;
        }

        return new ScanResult(matches, visited, truncated);
    }

    /// <summary>The same folders-before-files order the tree draws in, so the hit list and the filtered
    /// tree agree about what comes first.</summary>
    private static IEnumerable<VirtualEntry> Order(IReadOnlyList<VirtualEntry> entries) =>
        entries.OrderBy(e => e.IsDirectory ? 0 : 1)
               .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

    private static string Full(string diskPath, string innerPath) =>
        innerPath.Length == 0
            ? diskPath
            : Path.Combine(diskPath, innerPath.Replace('/', Path.DirectorySeparatorChar));
}
