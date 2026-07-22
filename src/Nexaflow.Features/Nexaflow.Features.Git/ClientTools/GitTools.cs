using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Git.Services;

namespace Nexaflow.Features.Git.ClientTools;

internal static class GitToolArgs
{
    /// <summary>Caps a potentially huge diff/log so it never floods the model.</summary>
    public static string Cap(string text, int maxLines = 600)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length <= maxLines) return text.TrimEnd();
        return string.Join('\n', lines[..maxLines]) + $"\n… ({lines.Length - maxLines} more lines truncated)";
    }

    /// <summary>An ISO-8601 date argument, or null when absent or unparseable.</summary>
    public static DateTimeOffset? Date(JsonObject args, string name) =>
        DateTimeOffset.TryParse(ToolArgs.Str(args, name), out var d) ? d : null;

    /// <summary>
    /// Reads the optional <c>from</c>/<c>to</c> pair. Both or neither: half a range is a mistake worth naming
    /// rather than silently ignoring, since the model would otherwise get a whole-history answer it couldn't
    /// tell apart from the range it asked for.
    /// </summary>
    public static bool TryRange(JsonObject args, out GitRange? range, out string? error)
    {
        var from = ToolArgs.Str(args, "from");
        var to   = ToolArgs.Str(args, "to");
        range = null;
        error = null;

        var hasFrom = !string.IsNullOrWhiteSpace(from);
        var hasTo   = !string.IsNullOrWhiteSpace(to);

        if (hasFrom != hasTo)
        {
            error = $"'{(hasFrom ? "from" : "to")}' was given without '{(hasFrom ? "to" : "from")}' — a range needs both ends.";
            return false;
        }

        if (hasFrom) range = new GitRange(from!, to!);
        return true;
    }
}

/// <summary>Branch, upstream tracking, and the per-file staged/modified/untracked lists.</summary>
public sealed class GitStatusTool(GitService git) : IClientTool
{
    public string Name => "git_status";
    public string Description => "Show git status: current branch, ahead/behind vs upstream, and the staged, modified, and untracked files.";
    public IReadOnlyList<ClientToolParameter> Parameters => [];
    public ToolSafety Safety => ToolSafety.SafeOperation;
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
    public string Description =>
        "List commits (hash, author, date, subject). Scope to a branch, a file path, or a revision range "
      + "(from/to accept tags, branches or hashes) and narrow with since/until/author/grep/no_merges. "
      + "To summarise a release, pass from='v1.3.0' to='main' with no_merges=true.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("count",     "How many commits to return (default 20, max 200).", Required: false, Type: "integer"),
        new("branch",    "Branch to read history from (default: current HEAD). Ignored when from/to are given.", Required: false),
        new("path",      "Limit history to commits touching this file/folder path.", Required: false),
        new("from",      "Range start, EXCLUSIVE — a tag, branch or hash. Requires 'to'.", Required: false),
        new("to",        "Range end, inclusive — a tag, branch or hash. Requires 'from'.", Required: false),
        new("since",     "Only commits authored on or after this date (ISO-8601, e.g. 2026-07-09).", Required: false),
        new("until",     "Only commits authored on or before this date (ISO-8601).", Required: false),
        new("author",    "Only commits whose author name or email contains this text.", Required: false),
        new("grep",      "Only commits whose message contains this text.", Required: false),
        new("no_merges", "True to skip merge commits — usually what you want for a changelog.", Required: false, Type: "boolean"),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var count  = Math.Clamp(ToolArgs.Int(arguments, "count", 20), 1, 200);
        var branch = ToolArgs.Str(arguments, "branch");
        var path   = ToolArgs.Str(arguments, "path");

        if (GitToolArgs.TryRange(arguments, out var range, out var rangeError) is false)
            return ToolResult.Error(rangeError!);

        var filter = new GitLogFilter(
            Range:    range,
            Since:    GitToolArgs.Date(arguments, "since"),
            Until:    GitToolArgs.Date(arguments, "until"),
            Author:   ToolArgs.Str(arguments, "author"),
            Grep:     ToolArgs.Str(arguments, "grep"),
            NoMerges: ToolArgs.Bool(arguments, "no_merges"));

        IReadOnlyList<GitCommitInfo> commits;
        try { commits = git.GetLog(count, branch, path, filter); }
        catch (ArgumentException ex) { return ToolResult.Error(ex.Message); }

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
    public string Description =>
        "Show a diff. With no from/to, diffs uncommitted work (working tree vs HEAD, or staged=true for the "
      + "index). Give from and to — tags, branches or hashes — to diff two revisions instead. Defaults to a "
      + "'stat' summary; ask for format='patch' only once you know which files you care about.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("path",   "Limit the diff to this file/folder path.", Required: false),
        new("staged", "True for staged (index) changes; false (default) for all working-tree changes. Ignored when from/to are given.", Required: false, Type: "boolean"),
        new("from",   "Diff start revision — a tag, branch or hash. Requires 'to'.", Required: false),
        new("to",     "Diff end revision — a tag, branch or hash. Requires 'from'.", Required: false),
        new("format", "How to render a from/to diff: 'stat' (default, per-file counts), 'name-only', or 'patch'.", Required: false),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var path = ToolArgs.Str(arguments, "path");

        if (GitToolArgs.TryRange(arguments, out var range, out var rangeError) is false)
            return ToolResult.Error(rangeError!);

        // Two revisions → the release-comparison path; otherwise the original uncommitted-changes behaviour.
        if (range is not null)
        {
            var format = ParseFormat(ToolArgs.Str(arguments, "format"));
            string between;
            try { between = git.GetDiffBetween(range.From, range.To, path, format); }
            catch (ArgumentException ex) { return ToolResult.Error(ex.Message); }

            return string.IsNullOrWhiteSpace(between)
                ? ToolResult.Ok("no changes", $"No differences between {range.From} and {range.To}.")
                : ToolResult.Ok($"diff {range.From}..{range.To} ({format.ToString().ToLowerInvariant()})",
                                GitToolArgs.Cap(between));
        }

        var staged = ToolArgs.Bool(arguments, "staged");
        var diff   = git.GetDiff(path, staged);
        if (string.IsNullOrWhiteSpace(diff))
            return ToolResult.Ok("no changes", staged ? "No staged changes." : "No uncommitted changes.");
        return ToolResult.Ok($"diff ({(staged ? "staged" : "working tree")})", GitToolArgs.Cap(diff));
    }, ct);

    private static GitDiffFormat ParseFormat(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "patch"                  => GitDiffFormat.Patch,
        "name-only" or "name_only" or "names" => GitDiffFormat.NameOnly,
        _                        => GitDiffFormat.Stat
    };
}

/// <summary>Tags — the release boundaries every "what changed since…" question starts from.</summary>
public sealed class GitTagsTool(GitService git) : IClientTool
{
    public string Name => "git_tags";
    public string Description =>
        "List the repository's tags, newest first, with the commit each points at and its date. "
      + "Use this to find a release boundary (e.g. the latest v1.* tag) before diffing or logging a range.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("pattern", "Only tags whose name contains this text (e.g. 'v1.').", Required: false),
        new("count",   "How many tags to return (default 50).", Required: false, Type: "integer"),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var count = Math.Clamp(ToolArgs.Int(arguments, "count", 50), 1, 500);
        var tags  = git.GetTags(ToolArgs.Str(arguments, "pattern")).Take(count).ToList();

        if (tags.Count == 0)
            return ToolResult.Ok("no tags", "This repository has no tags.");

        var sb = new StringBuilder();
        foreach (var t in tags)
            sb.Append(t.Name).Append("  ").Append(t.TargetHash).Append("  ")
              .Append(t.When?.ToString("yyyy-MM-dd") ?? "?").Append("  ")
              .Append(t.Subject ?? string.Empty).Append('\n');

        return ToolResult.Ok($"{tags.Count} tag(s)", sb.ToString().TrimEnd());
    }, ct);
}

/// <summary>A file's contents as of some revision — history of content, not just of commits.</summary>
public sealed class GitFileAtTool(GitService git) : IClientTool
{
    public string Name => "git_file_at";
    public string Description =>
        "Read a file's full contents as it was at a given revision (tag, branch or commit hash). "
      + "Use this to compare how a file looked at a release against how it looks now.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("revision", "The revision to read from — a tag, branch or commit hash (e.g. 'v1.3.0')."),
        new("path",     "Repository-relative path of the file (e.g. 'src/App/Config.json')."),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var revision = ToolArgs.Str(arguments, "revision");
        var path     = ToolArgs.Str(arguments, "path");

        if (string.IsNullOrWhiteSpace(revision)) return ToolResult.Error("A revision is required.");
        if (string.IsNullOrWhiteSpace(path))     return ToolResult.Error("A file path is required.");

        // Distinguish a bad revision from a file that simply didn't exist yet — very different fixes.
        if (!git.RevisionExists(revision))
            return ToolResult.Error($"Revision '{revision}' not found.");

        var text = git.GetFileAt(revision, path);
        if (text is null)
            return ToolResult.Error($"'{path}' does not exist at {revision} (or is not a text file).");

        return ToolResult.Ok($"{path}@{revision}", GitToolArgs.Cap(text, 1000));
    }, ct);
}

/// <summary>Local + remote branches with tracking info.</summary>
public sealed class GitBranchesTool(GitService git) : IClientTool
{
    public string Name => "git_branches";
    public string Description => "List local and remote branches; the current branch is marked, with upstream and ahead/behind where tracked.";
    public IReadOnlyList<ClientToolParameter> Parameters => [];
    public ToolSafety Safety => ToolSafety.SafeOperation;
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
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var hash = ToolArgs.Str(arguments, "commit");
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
    public ToolSafety Safety => ToolSafety.SafeOperation;
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
