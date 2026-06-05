using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Git.Services;

namespace Nexaflow.Features.Git.ClientTools;

internal static class GitToolArgs
{
    public static string? Str(JsonObject o, string key)
        => o.TryGetPropertyValue(key, out var v) && v is not null ? v.GetValue<string>() : null;

    public static bool Bool(JsonObject o, string key, bool fallback = false)
        => o.TryGetPropertyValue(key, out var v) && v is not null && bool.TryParse(v.ToString(), out var b) ? b : fallback;

    public static int Int(JsonObject o, string key, int fallback)
        => o.TryGetPropertyValue(key, out var v) && v is not null && int.TryParse(v.ToString(), out var i) ? i : fallback;

    /// <summary>Caps a potentially huge diff/log so it never floods the model.</summary>
    public static string Cap(string text, int maxLines = 600)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length <= maxLines) return text.TrimEnd();
        return string.Join('\n', lines[..maxLines]) + $"\n… ({lines.Length - maxLines} more lines truncated)";
    }
}

/// <summary>Branch, upstream tracking, and the per-file staged/modified/untracked lists.</summary>
public sealed class GitStatusTool(GitService git) : IClientTool
{
    public string Name => "git_status";
    public string Description => "Show git status: current branch, ahead/behind vs upstream, and the staged, modified, and untracked files.";
    public IReadOnlyList<ClientToolParameter> Parameters => [];
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var s  = git.GetStatus();
        var sb = new StringBuilder($"On branch {s.Branch}");
        if (s.Upstream is not null)
        {
            sb.Append($" (tracking {s.Upstream}");
            if (s.Ahead is > 0 || s.Behind is > 0)
                sb.Append($", {s.Ahead ?? 0} ahead, {s.Behind ?? 0} behind");
            sb.Append(')');
        }
        sb.Append('.').Append('\n');

        void Section(string title, IEnumerable<string> items)
        {
            var list = items.ToList();
            if (list.Count == 0) return;
            sb.Append('\n').Append(title).Append(":\n");
            foreach (var i in list) sb.Append("  ").Append(i).Append('\n');
        }

        Section("Staged",    s.Staged.Select(f => $"{f.Change}: {f.Path}"));
        Section("Modified",  s.Modified.Select(f => $"{f.Change}: {f.Path}"));
        Section("Untracked", s.Untracked);

        if (s.StagedCount + s.ModifiedCount + s.UntrackedCount == 0)
            sb.Append("\nWorking tree clean.");

        var summary = $"{s.Branch}: {s.StagedCount} staged, {s.ModifiedCount} modified, {s.UntrackedCount} untracked";
        return ToolResult.Ok(summary, sb.ToString().TrimEnd());
    }, ct);
}

/// <summary>Recent commit history.</summary>
public sealed class GitLogTool(GitService git) : IClientTool
{
    public string Name => "git_log";
    public string Description => "List recent commits (hash, author, date, subject). Optionally scope to a branch and/or a file path.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("count",  "How many commits to return (default 20).", Required: false, Type: "integer"),
        new("branch", "Branch to read history from (default: current HEAD).", Required: false),
        new("path",   "Limit history to commits touching this file/folder path.", Required: false),
    ];
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var count  = Math.Clamp(GitToolArgs.Int(arguments, "count", 20), 1, 200);
        var branch = GitToolArgs.Str(arguments, "branch");
        var path   = GitToolArgs.Str(arguments, "path");

        var commits = git.GetLog(count, branch, path);
        if (commits.Count == 0)
            return ToolResult.Ok("no commits", "No commits found for that query.");

        var sb = new StringBuilder();
        foreach (var c in commits)
            sb.Append(c.Hash).Append("  ").Append(c.When.ToString("yyyy-MM-dd"))
              .Append("  ").Append(c.Author).Append("  ").Append(c.Subject).Append('\n');
        return ToolResult.Ok($"{commits.Count} commit(s)", sb.ToString().TrimEnd());
    }, ct);
}

/// <summary>Unified diff of uncommitted changes (working tree or staged).</summary>
public sealed class GitDiffTool(GitService git) : IClientTool
{
    public string Name => "git_diff";
    public string Description => "Show the unified diff of uncommitted changes. By default the working tree vs HEAD; set staged=true for staged changes only. Optionally scope to one path.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("path",   "Limit the diff to this file/folder path.", Required: false),
        new("staged", "True for staged (index) changes; false (default) for all working-tree changes.", Required: false, Type: "boolean"),
    ];
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var path   = GitToolArgs.Str(arguments, "path");
        var staged = GitToolArgs.Bool(arguments, "staged");

        var diff = git.GetDiff(path, staged);
        if (string.IsNullOrWhiteSpace(diff))
            return ToolResult.Ok("no changes", staged ? "No staged changes." : "No uncommitted changes.");
        return ToolResult.Ok($"diff ({(staged ? "staged" : "working tree")})", GitToolArgs.Cap(diff));
    }, ct);
}

/// <summary>Local + remote branches with tracking info.</summary>
public sealed class GitBranchesTool(GitService git) : IClientTool
{
    public string Name => "git_branches";
    public string Description => "List local and remote branches; the current branch is marked, with upstream and ahead/behind where tracked.";
    public IReadOnlyList<ClientToolParameter> Parameters => [];
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var branches = git.GetBranches();
        var sb = new StringBuilder();
        foreach (var b in branches)
        {
            sb.Append(b.IsCurrent ? "* " : "  ")
              .Append(b.IsRemote ? "[remote] " : "")
              .Append(b.Name);
            if (b.Upstream is not null)
            {
                sb.Append(" → ").Append(b.Upstream);
                if (b.Ahead is > 0 || b.Behind is > 0)
                    sb.Append($" ({b.Ahead ?? 0}↑ {b.Behind ?? 0}↓)");
            }
            sb.Append('\n');
        }
        return ToolResult.Ok($"{branches.Count} branch(es)", sb.ToString().TrimEnd());
    }, ct);
}

/// <summary>A commit's message + diff against its first parent.</summary>
public sealed class GitShowTool(GitService git) : IClientTool
{
    public string Name => "git_show";
    public string Description => "Show a commit's metadata, full message, and diff against its first parent.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("commit", "Commit hash (full or abbreviated) to show.", Required: true),
    ];
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var hash = GitToolArgs.Str(arguments, "commit");
        if (string.IsNullOrWhiteSpace(hash))
            return ToolResult.Error("A commit hash is required.");

        var detail = git.Show(hash);
        if (detail is null)
            return ToolResult.Error($"Commit '{hash}' not found.");

        var sb = new StringBuilder()
            .Append("commit ").Append(detail.Hash).Append('\n')
            .Append("Author: ").Append(detail.Author).Append('\n')
            .Append("Date:   ").Append(detail.When.ToString("yyyy-MM-dd HH:mm")).Append("\n\n")
            .Append(detail.Message).Append("\n\n")
            .Append(detail.Diff);
        return ToolResult.Ok($"commit {detail.Hash}", GitToolArgs.Cap(sb.ToString()));
    }, ct);
}

/// <summary>Configured remotes (name + URL).</summary>
public sealed class GitRemotesTool(GitService git) : IClientTool
{
    public string Name => "git_remotes";
    public string Description => "List the repository's configured remotes (name and URL).";
    public IReadOnlyList<ClientToolParameter> Parameters => [];
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var remotes = git.GetRemotes();
        if (remotes.Count == 0)
            return ToolResult.Ok("no remotes", "No remotes configured.");
        var text = string.Join('\n', remotes.Select(r => $"{r.Name}\t{r.Url}"));
        return ToolResult.Ok($"{remotes.Count} remote(s)", text);
    }, ct);
}
