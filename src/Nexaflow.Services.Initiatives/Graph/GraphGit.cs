using System;
using System.Collections.Generic;
using System.IO;
using LibGit2Sharp;
using Nexaflow.Services.Initiatives.Graph.Store;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// One look at what git knows about a working tree, taken to vouch for files without reading them — see
/// <see cref="GraphAnchor"/> for when that is worth it.
/// </summary>
/// <param name="Head">HEAD's commit.</param>
/// <param name="Tracked">Every path in the index, a submodule by its own path.</param>
/// <param name="Dirty">Every path whose working copy is not HEAD's — modified, staged, added, deleted or untracked — and a
/// submodule by its path when its checkout has moved or has changes of its own.</param>
/// <param name="ChangedSince">Every path that differs between the anchor's commit and HEAD, or null when that commit is
/// not one this repository has.</param>
public sealed record GitLook(string Head, IReadOnlySet<string> Tracked, IReadOnlySet<string> Dirty,
                             IReadOnlySet<string>? ChangedSince)
{
    /// <summary>
    /// Whether the graph's record of <paramref name="rel"/> — which describes <paramref name="anchor"/>'s commit unless
    /// the anchor says otherwise — is what the working tree holds now: the file is tracked (or lies in a tracked
    /// submodule), the same in that commit and HEAD, and clean.
    /// </summary>
    public bool Vouches(string rel, GraphAnchor anchor)
    {
        if (ChangedSince is null || Dirty.Contains(rel) || anchor.Departed.Contains(rel)) return false;
        return Cover(rel) is { } entry
            && !ChangedSince.Contains(entry) && !Dirty.Contains(entry) && !anchor.Departed.Contains(entry);
    }

    /// <summary>The index entry that holds <paramref name="rel"/>: the file itself, or the submodule it lies in.</summary>
    private string? Cover(string rel)
    {
        if (Tracked.Contains(rel)) return rel;
        for (var cut = rel.LastIndexOf('/'); cut > 0; cut = rel.LastIndexOf('/', cut - 1))
            if (Tracked.Contains(rel[..cut])) return rel[..cut];
        return null;
    }
}

/// <summary>The graph's questions for git, asked of the working tree a graph describes.</summary>
public static class GraphGit
{
    /// <summary>
    /// What git knows about <paramref name="workTree"/> now, and how HEAD differs from <paramref name="since"/>; null when
    /// it is not the top of a git working tree or git cannot say.
    /// <para>
    /// A status of the whole tree is hundreds of milliseconds, so this is for the moments it saves far more than that — a
    /// tree whose every stamp disagrees — and for anchoring a graph off the request path, never for a query.
    /// </para>
    /// </summary>
    public static GitLook? Look(string workTree, string? since)
    {
        try
        {
            if (!Repository.IsValid(workTree)) return null;
            using var repo = new Repository(workTree);
            if (repo.Head.Tip is not { } head) return null;

            var tracked = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in repo.Index) tracked.Add(Slashed(entry.Path));

            var dirty = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in repo.RetrieveStatus(new StatusOptions
                     {
                         IncludeUntracked       = true,
                         RecurseUntrackedDirs   = true,
                         IncludeIgnored         = false,
                         ExcludeSubmodules      = false,
                         DetectRenamesInIndex   = false,
                         DetectRenamesInWorkDir = false,
                     }))
                if (entry.State is not (FileStatus.Unaltered or FileStatus.Ignored)) dirty.Add(Slashed(entry.FilePath));

            HashSet<string>? changed = null;
            if (since is { Length: > 0 } && repo.Lookup<Commit>(since) is { } then)
            {
                changed = new HashSet<string>(StringComparer.Ordinal);
                if (then.Sha != head.Sha)
                    foreach (var change in repo.Diff.Compare<TreeChanges>(then.Tree, head.Tree))
                    {
                        changed.Add(Slashed(change.Path));
                        changed.Add(Slashed(change.OldPath));
                    }
            }

            return new GitLook(head.Sha, tracked, dirty, changed);
        }
        catch (Exception e) when (e is LibGit2SharpException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Where git keeps <paramref name="workTree"/>'s HEAD and index — <c>.git</c>, or a worktree's own folder under the
    /// main checkout's — for watching the two files every checkout, merge, reset and commit rewrites. Null outside git.
    /// </summary>
    public static string? GitDirectory(string workTree)
    {
        try
        {
            return Repository.IsValid(workTree) && Repository.Discover(workTree) is { Length: > 0 } dir
                ? Path.TrimEndingDirectorySeparator(dir)
                : null;
        }
        catch (Exception e) when (e is LibGit2SharpException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Slashed(string path) => path.Replace('\\', '/');
}
