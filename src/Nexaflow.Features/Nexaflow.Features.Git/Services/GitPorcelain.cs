using System.Globalization;

namespace Nexaflow.Features.Git.Services;

/// <summary>
/// Reads what the <c>git</c> command line prints for the questions a working copy on a location's host asks: the porcelain
/// and format-string outputs, which git keeps stable across versions and languages. Pure text in, records out, so every
/// rule here is tested without a host.
/// </summary>
internal static class GitPorcelain
{
    /// <summary>What <see cref="GitStatus.Branch"/> holds for a detached HEAD — LibGit2Sharp's own word, so a status read
    /// either way reads the same.</summary>
    internal const string DetachedBranch = "(no branch)";

    /// <summary>The working-tree half of <c>git status --porcelain=v2 --branch -z</c>.</summary>
    internal sealed record StatusReport(
        string                       Branch,
        bool                         IsDetached,
        string?                      Upstream,
        int?                         Ahead,
        int?                         Behind,
        IReadOnlyList<GitFileChange> Staged,
        IReadOnlyList<GitFileChange> Modified,
        IReadOnlyList<string>        Untracked);

    /// <summary>
    /// Reads <c>git status --porcelain=v2 --branch -z</c>. A path staged is listed staged only, as the in-process status
    /// lists it; an unmerged path and an ignored one are counted nowhere. The fields after a rename are its new path and
    /// then, as a field of its own, the old one.
    /// </summary>
    internal static StatusReport ParseStatus(string printed)
    {
        string  branch   = DetachedBranch;
        bool    detached = false;
        string? upstream = null;
        int?    ahead = null, behind = null;
        var staged    = new List<GitFileChange>();
        var modified  = new List<GitFileChange>();
        var untracked = new List<string>();

        var fields = printed.Split('\0');
        for (int i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            if (field.Length < 2) continue;

            if (field.StartsWith("# ", StringComparison.Ordinal))
            {
                var header = field[2..];
                int space  = header.IndexOf(' ');
                if (space < 0) continue;
                var value = header[(space + 1)..];
                switch (header[..space])
                {
                    case "branch.head":
                        detached = value == "(detached)";
                        branch   = detached ? DetachedBranch : value;
                        break;
                    case "branch.upstream":
                        upstream = value;
                        break;
                    case "branch.ab":
                        var counts = value.Split(' ');
                        if (counts.Length == 2
                            && int.TryParse(counts[0].TrimStart('+'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var a)
                            && int.TryParse(counts[1].TrimStart('-'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
                            (ahead, behind) = (a, b);
                        break;
                }
                continue;
            }

            switch (field[0])
            {
                case '1': Sort(field.Split(' ', 9), pathAt: 8); break;
                case '2': Sort(field.Split(' ', 10), pathAt: 9); i++; break;
                case '?': untracked.Add(field[2..]); break;
            }
        }

        return new StatusReport(branch, detached, upstream, ahead, behind, staged, modified, untracked);

        void Sort(string[] parts, int pathAt)
        {
            if (parts.Length <= pathAt || parts[1].Length != 2) return;
            var (index, tree) = (parts[1][0], parts[1][1]);
            if (index != '.')     staged.Add(new GitFileChange(parts[pathAt], Describe(index)));
            else if (tree != '.') modified.Add(new GitFileChange(parts[pathAt], Describe(tree)));
        }
    }

    /// <summary>How many tracked paths are staged and how many changed in the tree, from
    /// <c>git status --porcelain=v2 -z --untracked-files=no</c>; a path may count in both.</summary>
    internal static (int Staged, int Modified) CountChanges(string printed)
    {
        int staged = 0, modified = 0;
        var fields = printed.Split('\0');
        for (int i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            if (field.Length < 4 || field[0] is not ('1' or '2')) continue;
            if (field[2] != '.') staged++;
            if (field[3] != '.') modified++;
            if (field[0] == '2') i++;
        }
        return (staged, modified);
    }

    /// <summary>A status letter as the in-process status words it.</summary>
    internal static string Describe(char code) => code switch
    {
        'A' => "new",
        'D' => "deleted",
        'R' => "renamed",
        'M' => "modified",
        'T' => "typechange",
        'C' => "copied",
        _   => code.ToString(),
    };

    /// <summary>The tip commit from <c>git log -1 --format=%H%x00%s%x00%aI</c>: its short hash, subject and author time;
    /// null when nothing was printed — a repository with no commit yet.</summary>
    internal static (string Hash, string Subject, DateTimeOffset? When)? ParseLastCommit(string printed)
    {
        var parts = printed.TrimEnd('\n', '\r').Split('\0');
        if (parts.Length < 3 || parts[0].Length < 7) return null;
        DateTimeOffset? when = DateTimeOffset.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null;
        return (parts[0][..7], parts[1], when);
    }

    /// <summary>One ref from <c>git for-each-ref --format=%(refname)%00%(objectname)%00%(upstream)</c>.</summary>
    internal sealed record Ref(string FullName, string Sha, string? UpstreamFullName)
    {
        public bool IsRemote => FullName.StartsWith("refs/remotes/", StringComparison.Ordinal);

        /// <summary>The name people use: <c>main</c>, <c>origin/main</c>.</summary>
        public string Name => FullName.StartsWith("refs/heads/", StringComparison.Ordinal) ? FullName["refs/heads/".Length..]
                            : IsRemote ? FullName["refs/remotes/".Length..]
                            : FullName;
    }

    internal static IReadOnlyList<Ref> ParseRefs(string printed)
        => printed.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                  .Select(line => line.TrimEnd('\r').Split('\0'))
                  .Where(parts => parts.Length >= 2)
                  .Select(parts => new Ref(parts[0], parts[1], parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null))
                  .ToList();

    /// <summary>
    /// The refs a linked worktree's state is measured by, chosen as <see cref="GitWorktreeService"/> chooses them: the
    /// branch it owns (its checked-out branch, or — detached — a local branch named after its folder sitting at its very
    /// commit), the remote-tracking branch its commits belong on (the owned branch's upstream where that exists, else a
    /// same-named remote branch), and the mainline it merges into (a local <c>main</c>/<c>master</c>/… other than its own,
    /// else the matching remote branch).
    /// </summary>
    internal static (string? OwnBranch, Ref? Remote, Ref? MergeTarget) ResolveWorktreeRefs(
        IReadOnlyList<Ref> refs, string? headBranch, string? tipSha, string folderName)
    {
        var locals  = refs.Where(r => !r.IsRemote && r.FullName.StartsWith("refs/heads/", StringComparison.Ordinal)).ToList();
        var remotes = refs.Where(r => r.IsRemote).ToList();

        string? own = headBranch ?? (tipSha is null ? null
            : locals.FirstOrDefault(b => (b.Name == folderName || b.Name.EndsWith("/" + folderName, StringComparison.Ordinal))
                                         && b.Sha == tipSha)?.Name);

        Ref? remote = null;
        if (headBranch is not null
            && locals.FirstOrDefault(b => b.Name == headBranch)?.UpstreamFullName is { } upstream)
            remote = refs.FirstOrDefault(r => r.FullName == upstream);
        if (remote is null && own is not null)
            remote = remotes.FirstOrDefault(r => r.Name.EndsWith("/" + own, StringComparison.Ordinal));

        Ref? target = null;
        foreach (var candidate in GitWorktreeService.MergeTargetCandidates)
            if (locals.FirstOrDefault(b => b.Name == candidate) is { } b && b.Name != own) { target = b; break; }
        if (target is null)
            foreach (var candidate in GitWorktreeService.MergeTargetCandidates)
                if (remotes.FirstOrDefault(r => r.Name.EndsWith("/" + candidate, StringComparison.Ordinal)) is { } r && r.Name != own)
                { target = r; break; }

        return (own, remote, target);
    }

    /// <summary>One linked worktree from <c>git worktree list --porcelain</c>.</summary>
    internal sealed record ListedWorktree(string Path, bool IsPrunable);

    /// <summary>The linked worktrees of <c>git worktree list --porcelain</c> — every stanza after the first, which is the
    /// main working tree.</summary>
    internal static IReadOnlyList<ListedWorktree> ParseWorktreeList(string printed)
    {
        var found = new List<ListedWorktree>();
        string? path = null;
        bool prunable = false;
        foreach (var raw in printed.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                if (path is not null) found.Add(new ListedWorktree(path, prunable));
                (path, prunable) = (null, false);
            }
            else if (line.StartsWith("worktree ", StringComparison.Ordinal)) path = line["worktree ".Length..];
            else if (line == "prunable" || line.StartsWith("prunable ", StringComparison.Ordinal)) prunable = true;
        }
        if (path is not null) found.Add(new ListedWorktree(path, prunable));
        return found.Skip(1).ToList();
    }

    /// <summary>The folder a linked worktree's <c>.git</c> pointer names (<c>gitdir: …</c>), made absolute against the
    /// worktree folder; null when the pointer names none.</summary>
    internal static string? AdminFolderOf(string pointer, string worktreeFolder)
    {
        var line = pointer.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("gitdir:", StringComparison.Ordinal));
        if (line is null) return null;
        var named = line["gitdir:".Length..].Trim();
        return named.Length == 0 ? null : ResolvePosix(worktreeFolder, named);
    }

    /// <summary>A POSIX path made absolute against a folder, its <c>.</c> and <c>..</c> walked.</summary>
    internal static string ResolvePosix(string folder, string path)
    {
        var parts = new List<string>();
        foreach (var part in (path.StartsWith('/') ? path : folder.TrimEnd('/') + "/" + path).Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(part);
        }
        return "/" + string.Join('/', parts);
    }

    /// <summary>The last segment of a POSIX path.</summary>
    internal static string NameOf(string path)
    {
        var trimmed = path.TrimEnd('/');
        return trimmed[(trimmed.LastIndexOf('/') + 1)..];
    }

    /// <summary>What a failed git said, in the line that says it: its last line on stderr, else on stdout.</summary>
    internal static string Said(Nexaflow.IO.Common.Locations.ProgramExit exit)
    {
        foreach (var text in (string[])[exit.Errors, exit.Output])
            if (text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) is { } line) return line;
        return $"git exited with {exit.ExitCode}";
    }
}
