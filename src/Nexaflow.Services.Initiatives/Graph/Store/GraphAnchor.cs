using System;
using System.Collections.Generic;

namespace Nexaflow.Services.Initiatives.Graph.Store;

/// <summary>
/// What git can vouch for in a graph: the commit it describes, and the files where it may describe something else.
/// <para>
/// A file's stamp answers "is this file as the graph last read it?" for the price of a stat, and it is the right
/// question for a tree that moves a few files at a time. It is the wrong one after anything that rewrites the tree
/// wholesale. A checkout gives every file a new write time, so a worktree starting from the main checkout's graph, or
/// a tree just switched to another branch, has every stamp disagree and every file to be read again to learn that
/// almost none of them changed. Git has already worked that out: a file that is clean in the working tree and the
/// same in <see cref="Commit"/> and HEAD holds exactly what the graph holds, so it needs no reading.
/// </para>
/// <para>
/// Only true while the graph describes <see cref="Commit"/> everywhere outside <see cref="Departed"/> — so a file the
/// graph takes from the working tree joins it, and only a fresh look at git, taken over a graph known to describe the
/// working tree, empties it.
/// </para>
/// </summary>
/// <param name="Commit">The commit the graph describes, outside <paramref name="Departed"/>.</param>
/// <param name="Departed">Repo-relative paths whose graph content came from the working tree and may differ from
/// <paramref name="Commit"/>: changed, added or removed since, or never tracked at all.</param>
public sealed record GraphAnchor(string Commit, IReadOnlySet<string> Departed)
{
    /// <summary>The same anchor, with <paramref name="paths"/> no longer vouched for by its commit.</summary>
    public GraphAnchor Without(IEnumerable<string> paths)
    {
        HashSet<string>? departed = null;
        foreach (var path in paths)
        {
            if (Departed.Contains(path)) continue;
            departed ??= new HashSet<string>(Departed, StringComparer.Ordinal);
            departed.Add(path);
        }
        return departed is null ? this : this with { Departed = departed };
    }
}
