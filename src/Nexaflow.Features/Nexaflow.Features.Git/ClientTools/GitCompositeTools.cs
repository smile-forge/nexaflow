using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Git.Services;

namespace Nexaflow.Features.Git.ClientTools;

/// <summary>Everything about "what is the difference between A and B", in one call.</summary>
public sealed class GitCompareTool(GitInsightService insights) : IClientTool
{
    public string Name => "git_compare";
    public string Description =>
        "Compare two points in history in one call: how far apart they are, which files changed, and the "
      + "commits between them. This is the tool for 'what changed between release X and now' — prefer it "
      + "over separate git_log and git_diff calls.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("from", "The earlier point — a tag, branch or hash (e.g. 'v1.3.0')."),
        new("to",   "The later point — a tag, branch or hash (e.g. 'main')."),
        new("max_commits", "How many commits to list (default 100).", Required: false, Type: "integer"),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var from = ToolArgs.Str(arguments, "from");
        var to   = ToolArgs.Str(arguments, "to");
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return ToolResult.Error("Both 'from' and 'to' are required.");

        GitComparison c;
        try { c = insights.Compare(from!, to!, Math.Clamp(ToolArgs.Int(arguments, "max_commits", 100), 1, 500)); }
        catch (ArgumentException ex) { return ToolResult.Error(ex.Message); }

        var sb = new StringBuilder()
            .Append($"{to} is {c.Divergence.Behind ?? 0} commit(s) ahead of {from}")
            .Append(c.Divergence.Ahead is > 0 ? $" ({from} has {c.Divergence.Ahead} it lacks)" : "")
            .Append(".\n\nFILES CHANGED\n").Append(GitToolArgs.Cap(c.Stat, 200))
            .Append("\n\nCOMMITS\n");

        foreach (var commit in c.Commits)
            sb.Append(commit.Hash).Append("  ").Append(commit.When.ToString("yyyy-MM-dd"))
              .Append("  ").Append(commit.Subject).Append('\n');

        return ToolResult.Ok($"{c.Commits.Count} commit(s) between {from} and {to}",
                             GitToolArgs.Cap(sb.ToString(), 400));
    }, ct);
}

/// <summary>Release-note material for a range.</summary>
public sealed class GitChangelogTool(GitInsightService insights) : IClientTool
{
    public string Name => "git_changelog";
    public string Description =>
        "Assemble release notes for a range: the pull requests merged in it, the individual commit subjects, "
      + "who contributed, and a file-stat summary. Pull-request titles are recovered from the merge commits, "
      + "so this needs no network access.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("from", "The previous release — a tag, branch or hash (e.g. 'v1.3.0')."),
        new("to",   "The new release point — a tag, branch or hash (default 'HEAD').", Required: false),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var from = ToolArgs.Str(arguments, "from");
        if (string.IsNullOrWhiteSpace(from)) return ToolResult.Error("'from' is required.");
        var to = ToolArgs.Str(arguments, "to") is { Length: > 0 } t ? t : "HEAD";

        GitChangelog log;
        try { log = insights.Changelog(from!, to); }
        catch (ArgumentException ex) { return ToolResult.Error(ex.Message); }

        var sb = new StringBuilder($"Changelog {from} → {to}\n");

        if (log.PullRequests.Count > 0)
        {
            sb.Append($"\nMERGED PULL REQUESTS ({log.PullRequests.Count})\n");
            foreach (var pr in log.PullRequests)
                sb.Append("  #").Append(pr.Number).Append("  ").Append(pr.Title).Append('\n');
        }

        sb.Append($"\nCOMMITS ({log.Commits.Count}, merges excluded)\n");
        foreach (var c in log.Commits)
            sb.Append("  ").Append(c.Hash).Append("  ").Append(c.Subject).Append('\n');

        sb.Append($"\nCONTRIBUTORS: {string.Join(", ", log.Contributors)}\n");
        sb.Append("\nFILES CHANGED\n").Append(GitToolArgs.Cap(log.Stat, 150));

        return ToolResult.Ok($"{log.Commits.Count} commit(s), {log.PullRequests.Count} PR(s)",
                             GitToolArgs.Cap(sb.ToString(), 500));
    }, ct);
}

/// <summary>The "is this safe to prune" review, for every branch at once.</summary>
public sealed class GitBranchAuditTool(GitInsightService insights) : IClientTool
{
    public string Name => "git_branch_audit";
    public string Description =>
        "Review every local branch at once: its upstream, whether that upstream is gone, how far it is from "
      + "the mainline, whether a worktree holds it, and whether deleting it would lose work. Use this before "
      + "any branch cleanup.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("mainline", "Ref to measure against (default: origin/main, else origin/master, else local main/master).", Required: false),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var rows = insights.AuditBranches(ToolArgs.Str(arguments, "mainline"));
        if (rows.Count == 0) return ToolResult.Ok("no branches", "No local branches.");

        var safe = rows.Count(r => r.SafeToDelete);
        var sb   = new StringBuilder();

        foreach (var r in rows.OrderBy(r => r.SafeToDelete).ThenBy(r => r.Name, StringComparer.Ordinal))
        {
            sb.Append(r.IsCurrent ? "* " : "  ").Append(r.Name);
            sb.Append(r.MergedIntoMainline ? "  merged" : $"  UNMERGED ({r.AheadOfMainline ?? 0} commit(s) not on the mainline)");
            if (r.Upstream is not null) sb.Append("  upstream=").Append(r.Upstream).Append(r.UpstreamGone ? " (GONE)" : "");
            if (r.HeldByWorktree is not null) sb.Append("  held by worktree: ").Append(r.HeldByWorktree);
            sb.Append(r.SafeToDelete ? "  → safe to delete" : "  → keep").Append('\n');
        }

        return ToolResult.Ok($"{rows.Count} branch(es), {safe} safe to delete", sb.ToString().TrimEnd());
    }, ct);
}

/// <summary>Where did that work go?</summary>
public sealed class GitFindWorkTool(GitInsightService insights) : IClientTool
{
    public string Name => "git_find_work";
    public string Description =>
        "Find work by topic across branches, tags, stashes, the reflog and commit messages — the 'where did "
      + "that change go' question. Work whose worktree was deleted still appears here, because removing a "
      + "worktree deletes only the folder, never the branch or its commits.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("query", "Text to look for in branch/tag names, stash and reflog messages, and commit subjects."),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var query = ToolArgs.Str(arguments, "query");
        if (string.IsNullOrWhiteSpace(query)) return ToolResult.Error("A search query is required.");

        var hits = insights.FindWork(query!);
        if (hits.Count == 0)
            return ToolResult.Ok("nothing found",
                $"Nothing matching '{query}' in branches, tags, stashes, the reflog or recent commit subjects.");

        var sb = new StringBuilder();
        foreach (var group in hits.GroupBy(h => h.Kind))
        {
            sb.Append(group.Key).Append(":\n");
            foreach (var h in group) sb.Append("  ").Append(h.Name).Append("  ").Append(h.Detail).Append('\n');
        }

        return ToolResult.Ok($"{hits.Count} hit(s)", GitToolArgs.Cap(sb.ToString()));
    }, ct);
}

/// <summary>A file's whole life, renames included.</summary>
public sealed class GitFileHistoryTool(GitInsightService insights) : IClientTool
{
    public string Name => "git_file_history";
    public string Description =>
        "List the commits that touched a file, following renames, with the path it had at each. Pair with "
      + "git_file_at to read the file's contents at any of the returned commits.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("path",  "Repository-relative path of the file."),
        new("count", "How many commits to return (default 50).", Required: false, Type: "integer"),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var path = ToolArgs.Str(arguments, "path");
        if (string.IsNullOrWhiteSpace(path)) return ToolResult.Error("A file path is required.");

        var entries = insights.FileHistory(path!, Math.Clamp(ToolArgs.Int(arguments, "count", 50), 1, 200));
        if (entries.Count == 0)
            return ToolResult.Ok("no history", $"No commits touch '{path}'.");

        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            sb.Append(e.Commit.Hash).Append("  ").Append(e.Commit.When.ToString("yyyy-MM-dd"))
              .Append("  ").Append(e.Commit.Author).Append("  ").Append(e.Commit.Subject);
            // The path is echoed only when it differs, so a rename stands out rather than repeating.
            if (!string.Equals(e.Path, path!.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                sb.Append("   (as ").Append(e.Path).Append(')');
            sb.Append('\n');
        }

        return ToolResult.Ok($"{entries.Count} commit(s)", GitToolArgs.Cap(sb.ToString()));
    }, ct);
}
