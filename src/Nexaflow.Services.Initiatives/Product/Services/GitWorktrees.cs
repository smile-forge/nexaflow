namespace Nexaflow.Services.Initiatives.Product.Services;

/// <summary>
/// The linked git worktrees of a checkout, read straight from <c>.git/worktrees/*/gitdir</c> — pure file
/// reads, no git process, so it is safe on the UI thread and in the installer gate.
/// </summary>
/// <remarks>
/// <para>
/// A linked worktree is the <em>same repository</em> checked out at another branch, so a path inside one names
/// a file this repo already has — just a second copy of it. Anything that records a repo-relative path (a
/// snaplink <c>doc</c>, the test-coverage manifest) must therefore re-root such a path to the main checkout,
/// or the record rots the moment the branch merges and the worktree is removed.
/// </para>
/// <para>
/// The common shape here is <c>&lt;main&gt;/.claude/worktrees/&lt;name&gt;/…</c> — a worktree nested inside the
/// main checkout, which is why the leaked paths look repo-relative and slip past a naive "is it under the
/// root?" test. Nothing below assumes that layout; the git metadata is the source of truth.
/// </para>
/// </remarks>
public static class GitWorktrees
{
    /// <summary>
    /// The absolute root directory of every linked worktree of <paramref name="repoRoot"/> (empty when it is
    /// not a main checkout, or has none). Longest paths first, so a nested worktree wins over its container.
    /// </summary>
    public static IReadOnlyList<string> Roots(string repoRoot)
    {
        var container = Path.Combine(repoRoot, ".git", "worktrees");
        if (!Directory.Exists(container)) return [];

        var roots = new List<string>();
        foreach (var dir in Directory.GetDirectories(container))
        {
            var pointer = Path.Combine(dir, "gitdir");
            if (!File.Exists(pointer)) continue;
            string text;
            try { text = File.ReadAllText(pointer).Trim(); }
            catch { continue; }
            if (text.Length == 0) continue;

            // The pointer names the worktree's own ".git" FILE; its directory is the worktree root.
            var worktreeRoot = Path.GetDirectoryName(Path.GetFullPath(text));
            if (worktreeRoot is { Length: > 0 }) roots.Add(Norm(worktreeRoot));
        }

        roots.Sort((a, b) => b.Length.CompareTo(a.Length));
        return roots;
    }

    /// <summary>
    /// Re-roots <paramref name="path"/> when it points inside one of <paramref name="worktreeRoots"/>, yielding
    /// the forward-slash path relative to that worktree — which is exactly the path relative to the main
    /// checkout, the two being the same repo. Returns false (and echoes the input) for anything else.
    /// </summary>
    /// <param name="path">Absolute, or relative to the main checkout — both are handled.</param>
    /// <param name="repoRoot">The main checkout, used to make a relative <paramref name="path"/> absolute.</param>
    public static bool TryReRoot(string path, string repoRoot, IReadOnlyList<string> worktreeRoots, out string reRooted)
    {
        reRooted = path;
        if (worktreeRoots.Count == 0 || string.IsNullOrWhiteSpace(path)) return false;

        string full;
        try { full = Norm(Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(repoRoot, path))); }
        catch { return false; }

        foreach (var root in worktreeRoots)
        {
            if (!full.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) continue;
            reRooted = full[(root.Length + 1)..];
            return true;
        }
        return false;
    }

    private static string Norm(string p) => p.Replace('\\', '/').TrimEnd('/');
}
