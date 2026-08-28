using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Nexaflow.Services.Initiatives.Product.Model;

namespace Nexaflow.Services.Initiatives.Product.Services;

/// <summary>
/// Reads and writes the per-branch snaplink change sets that live under the <b>committed</b> export
/// directory (<c>docs/product/pending/&lt;branch&gt;.json</c>).
/// <para>
/// Committed on purpose. The live tree is gitignored because it changes constantly and would conflict on
/// every merge; a pending set is the opposite — small, written once per branch, and belonging to exactly one
/// PR. Keeping it in git is what removes the hard part of the problem: at merge the change set arrives in
/// the main checkout together with the code it describes, so consolidating it needs no way of asking "which
/// worktree, on whose machine, produced this?". Two branches touching the same node's links conflict in git,
/// where a human is already looking.
/// </para>
/// <para>
/// Its presence in the main checkout <i>is</i> the merged signal: a branch's file can only get there by
/// being merged, so the next run that notices it can fold it in and delete it.
/// </para>
/// </summary>
public sealed class PendingStore
{
    /// <summary>Where pending sets live, relative to the export directory.</summary>
    public const string FolderName = "pending";

    private readonly string _dir;

    /// <param name="exportDir">The committed export directory — <c>docs/product</c> by default.</param>
    public PendingStore(string productRoot, string exportDir = "docs/product")
        => _dir = Path.Combine(productRoot, exportDir.Replace('/', Path.DirectorySeparatorChar), FolderName);

    public string PathFor(string branch) => Path.Combine(_dir, FileNameFor(branch));

    /// <summary>A branch name flattened into one file name — <c>claude/foo</c> becomes <c>claude--foo</c>.</summary>
    public static string FileNameFor(string branch) =>
        new string([.. branch.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' ? '-' : c)])
            .Replace("--", "--") + ".json";

    /// <summary>This branch's pending set, or an empty one when it has changed nothing yet.</summary>
    public PendingSnaplinks Load(string branch)
    {
        var path = PathFor(branch);
        try
        {
            if (File.Exists(path)
                && JsonSerializer.Deserialize<PendingSnaplinks>(File.ReadAllText(path), ProductJson.Options)
                   is { } loaded)
            {
                loaded.Branch = branch;     // the file name is the authority, not a stale field inside it
                return loaded;
            }
        }
        catch { }                           // unreadable is treated as "nothing pending", never as a failure
        return new PendingSnaplinks { Branch = branch };
    }

    /// <summary>Writes the set, or deletes the file when the branch no longer changes anything.</summary>
    public void Save(PendingSnaplinks pending)
    {
        var path = PathFor(pending.Branch);
        if (pending.IsEmpty) { Delete(pending.Branch); return; }

        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, JsonSerializer.Serialize(pending, ProductJson.Options));
    }

    public bool Delete(string branch)
    {
        var path = PathFor(branch);
        try { if (File.Exists(path)) { File.Delete(path); return true; } }
        catch { }
        return false;
    }

    /// <summary>
    /// Every pending set present here. In the main checkout these are the ones that have arrived by merge
    /// and are waiting to be folded in; in a worktree it is normally just this branch's own.
    /// </summary>
    public IReadOnlyList<PendingSnaplinks> All()
    {
        if (!Directory.Exists(_dir)) return [];

        var found = new List<PendingSnaplinks>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                if (JsonSerializer.Deserialize<PendingSnaplinks>(File.ReadAllText(file), ProductJson.Options)
                    is { } loaded && !loaded.IsEmpty)
                {
                    if (string.IsNullOrEmpty(loaded.Branch))
                        loaded.Branch = Path.GetFileNameWithoutExtension(file);
                    found.Add(loaded);
                }
            }
            catch { }
        }
        return found;
    }

    /// <summary>The repo-relative paths of the files backing <paramref name="sets"/>, for staging a commit.</summary>
    public IReadOnlyList<string> RelativePaths(string productRoot, IEnumerable<PendingSnaplinks> sets) =>
        [.. sets.Select(s => Path.GetRelativePath(productRoot, PathFor(s.Branch)).Replace('\\', '/'))];
}
