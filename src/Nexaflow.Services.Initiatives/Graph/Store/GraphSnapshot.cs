using System;
using System.Collections.Generic;
using Nexaflow.Services.Initiatives.Graph.Model;

namespace Nexaflow.Services.Initiatives.Graph.Store;

/// <summary>
/// What one working tree's graph weighs in memory: the assembled graph, the per-file material it was
/// assembled from, and a stamp per file saying whether that material is still current.
/// <para>
/// These three used to be two JSON files with no relationship expressed between them — <c>graph.json</c> and
/// <c>graph-cache.json</c> — which meant every consumer loaded both to do anything, and 68% of the second
/// was a second copy of the first. They are one artefact because they are one fact: this is the graph, this
/// is what it was built from, and this is how to tell whether it still holds.
/// </para>
/// </summary>
public sealed class GraphSnapshot
{
    public KnowledgeGraph Graph { get; set; } = new();

    /// <summary>The per-file extraction the graph was assembled from — reused verbatim for a file that has
    /// not changed, which is what makes a rebuild proportional to the edit rather than to the repo.</summary>
    public GraphCache Cache { get; set; } = new();

    /// <summary>Repo-relative path → what that file looked like when it was last extracted.</summary>
    public Dictionary<string, FileStamp> Files { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// What a file looked like when its contribution was extracted. The hash is the authority — two files with
/// the same content produce the same graph however their timestamps differ — but hashing means reading, and
/// reading fifteen thousand files to learn that none of them changed is the ninety seconds a full build
/// spends. Length and write time come from the directory entry, so a scan that finds both unchanged can
/// skip the read entirely and only fall back to hashing where they disagree.
/// </summary>
/// <param name="Hash">Content hash of the decoded text — the same one <see cref="FileContribution.Hash"/> carries.</param>
/// <param name="Length">Size in bytes as the directory entry reports it.</param>
/// <param name="ModifiedUtc">Last write time, UTC, as the directory entry reports it.</param>
public readonly record struct FileStamp(string Hash, long Length, DateTime ModifiedUtc)
{
    /// <summary>Whether a directory entry matches this stamp — cheap enough to ask of every file in the repo,
    /// and a miss only means "read it and hash it", never "assume it changed".</summary>
    public bool Matches(long length, DateTime modifiedUtc) =>
        Length == length && ModifiedUtc == modifiedUtc;

    /// <summary>Whether this stamp says anything. An archive written before stamps were recorded carries
    /// one of these for every file, and "unknown" has to mean unknown rather than changed — the difference
    /// between a scan and an accidental rebuild of the whole repository.</summary>
    public bool IsKnown => Length > 0 || ModifiedUtc != default;
}
