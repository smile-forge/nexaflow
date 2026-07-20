using System.Text;

namespace Nexaflow.Features.Git.Services;

/// <summary>
/// Renders the git viewlet's honest AI-context line from a <see cref="GitStatus"/> (and optional
/// <see cref="GitWorktreeInfo"/>). Kept separate from the WPF viewlet so the wording — branch,
/// ahead/behind, staged/modified/untracked, last commit, and worktree/unpushed state — is unit-testable
/// against constructed status records without a repository or a UI control.
/// </summary>
public static class GitContextFormatter
{
    public static string Describe(GitStatus s, GitWorktreeInfo? worktree)
    {
        var sb = new StringBuilder($"Git: on '{s.Branch}'");
        if (s.Upstream is not null && (s.Ahead is > 0 || s.Behind is > 0))
        {
            var ahead  = s.Ahead  is > 0 ? $"{s.Ahead}↑"  : null;
            var behind = s.Behind is > 0 ? $"{s.Behind}↓" : null;
            sb.Append($" ({string.Join(" ", new[] { ahead, behind }.Where(x => x != null))} vs {s.Upstream})");
        }
        sb.Append('.');

        var parts = new List<string>();
        if (s.StagedCount    > 0) parts.Add($"{s.StagedCount} staged");
        if (s.ModifiedCount  > 0) parts.Add($"{s.ModifiedCount} modified");
        if (s.UntrackedCount > 0) parts.Add($"{s.UntrackedCount} untracked");
        sb.Append(parts.Count == 0 ? " Working tree clean." : " " + string.Join(", ", parts) + ".");

        if (s.LastCommitHash is not null)
            sb.Append($" Last commit {s.LastCommitHash} \"{s.LastCommitSubject}\".");

        if (worktree is { } wt)
        {
            if (wt.IsBroken)
                sb.Append(" This is a broken worktree remnant (dangling .git link).");
            else
            {
                sb.Append(" This is a linked worktree");
                sb.Append(wt.MergeTargetBranch is { } t
                    ? (wt.IsMerged ? $", merged into {t}" : $", not yet merged into {t}")
                    : "");
                sb.Append(!wt.HasUpstream ? ", never pushed."
                    : wt.IsPushed ? ", pushed to its remote."
                    : $", {wt.AheadOfRemote} commit(s) unpushed.");
            }
        }
        return sb.ToString();
    }
}
