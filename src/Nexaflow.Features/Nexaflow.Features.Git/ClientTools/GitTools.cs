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

/// <summary>Ahead/behind between any two refs — not just the current branch and its upstream.</summary>
public sealed class GitMergeBaseTool(GitService git) : IClientTool
{
    public string Name => "git_merge_base";
    public string Description =>
        "Compare any two refs (branches, tags or hashes): their common ancestor, how many commits each has "
      + "that the other lacks, and whether the first is already fully contained in the second.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("a", "First ref — the one you are asking about (e.g. a feature branch)."),
        new("b", "Second ref — what to compare against (e.g. 'main')."),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var a = ToolArgs.Str(arguments, "a");
        var b = ToolArgs.Str(arguments, "b");
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return ToolResult.Error("Both 'a' and 'b' are required.");

        GitDivergence d;
        try { d = git.GetDivergence(a!, b!); }
        catch (ArgumentException ex) { return ToolResult.Error(ex.Message); }

        if (d.MergeBaseHash is null)
            return ToolResult.Ok("unrelated", $"{a} and {b} have no common ancestor — unrelated histories.");

        var text = $"merge base: {d.MergeBaseHash}\n"
                 + $"{a} is {d.Ahead ?? 0} commit(s) ahead of, and {d.Behind ?? 0} behind, {b}.\n"
                 + (d.IsAMergedIntoB
                        ? $"{a} is fully contained in {b} (nothing would be lost by deleting it)."
                        : $"{a} has work {b} does not.");

        return ToolResult.Ok($"{d.Ahead ?? 0} ahead / {d.Behind ?? 0} behind", text);
    }, ct);
}

/// <summary>Which refs can reach a commit — the "is this safe to delete" answer.</summary>
public sealed class GitContainsTool(GitService git) : IClientTool
{
    public string Name => "git_contains";
    public string Description =>
        "List the branches and tags that contain a given commit. Use this before deleting a branch, or to "
      + "prove that work still exists somewhere even though its branch or worktree is gone.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("revision", "The commit to look for — a hash, branch or tag."),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var revision = ToolArgs.Str(arguments, "revision");
        if (string.IsNullOrWhiteSpace(revision)) return ToolResult.Error("A revision is required.");

        IReadOnlyList<GitRefMention> refs;
        try { refs = git.GetRefsContaining(revision!); }
        catch (ArgumentException ex) { return ToolResult.Error(ex.Message); }

        if (refs.Count == 0)
            return ToolResult.Ok("no refs", $"No branch or tag contains {revision} — it is reachable only from the reflog.");

        var sb = new StringBuilder();
        foreach (var r in refs.OrderBy(r => r.IsTag).ThenBy(r => r.IsRemote).ThenBy(r => r.Name, StringComparer.Ordinal))
            sb.Append(r.IsTag ? "  tag    " : r.IsRemote ? "  remote " : "  branch ").Append(r.Name).Append('\n');

        return ToolResult.Ok($"{refs.Count} ref(s)", $"Contained in:\n{sb.ToString().TrimEnd()}");
    }, ct);
}

/// <summary>Per-line authorship.</summary>
public sealed class GitBlameTool(GitService git) : IClientTool
{
    public string Name => "git_blame";
    public string Description =>
        "Show who last changed each line of a file, and in which commit. Narrow to a line range for a "
      + "focused answer; follow up with git_show on a hash to read why the change was made.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("path",       "Repository-relative path of the file to blame."),
        new("start_line", "First line to report (1-based).", Required: false, Type: "integer"),
        new("end_line",   "Last line to report (1-based).", Required: false, Type: "integer"),
        new("revision",   "Blame as of this revision (default HEAD).", Required: false),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var path = ToolArgs.Str(arguments, "path");
        if (string.IsNullOrWhiteSpace(path)) return ToolResult.Error("A file path is required.");

        var start    = ToolArgs.Int(arguments, "start_line", 0);
        var end      = ToolArgs.Int(arguments, "end_line", 0);
        var revision = ToolArgs.Str(arguments, "revision") is { Length: > 0 } r ? r : "HEAD";

        IReadOnlyList<GitBlameLine> lines;
        try
        {
            lines = git.GetBlame(path!, start > 0 ? start : null, end > 0 ? end : null, revision);
        }
        catch (ArgumentException ex) { return ToolResult.Error(ex.Message); }

        if (lines.Count == 0) return ToolResult.Ok("no lines", "Nothing to blame in that range.");

        var sb = new StringBuilder();
        foreach (var l in lines)
            sb.Append(l.Hash).Append("  ").Append(l.When.ToString("yyyy-MM-dd")).Append("  ")
              .Append(l.Author).Append("  ").Append(l.Line).Append(": ").Append(l.Text).Append('\n');

        return ToolResult.Ok($"{lines.Count} line(s)", GitToolArgs.Cap(sb.ToString()));
    }, ct);
}

/// <summary>Pickaxe search — when a string entered or left the codebase.</summary>
public sealed class GitSearchHistoryTool(GitService git) : IClientTool
{
    public string Name => "git_search_history";
    public string Description =>
        "Search HISTORY rather than the current checkout: find commits whose diff added or removed lines "
      + "containing some text. Answers 'when did we start/stop doing X'. Scope with a path to keep it fast.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("pattern",     "Text to look for in added/removed diff lines."),
        new("path",        "Limit the search to this file/folder path — strongly recommended.", Required: false),
        new("max_commits", "How many commits back to scan (default 200, max 1000).", Required: false, Type: "integer"),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var pattern = ToolArgs.Str(arguments, "pattern");
        if (string.IsNullOrWhiteSpace(pattern)) return ToolResult.Error("A search pattern is required.");

        var scanned = Math.Clamp(ToolArgs.Int(arguments, "max_commits", 200), 1, 1000);
        var matches = git.SearchHistory(pattern!, ToolArgs.Str(arguments, "path"), scanned);

        if (matches.Count == 0)
            return ToolResult.Ok("no matches",
                $"No commit in the last {scanned} added or removed a line containing '{pattern}'.");

        var sb = new StringBuilder();
        foreach (var m in matches)
            sb.Append(m.Commit.Hash).Append("  ").Append(m.Commit.When.ToString("yyyy-MM-dd"))
              .Append("  +").Append(m.Added).Append(" -").Append(m.Removed)
              .Append("  ").Append(m.Commit.Subject)
              .Append("  [").Append(string.Join(", ", m.Paths.Take(3))).Append("]\n");

        return ToolResult.Ok($"{matches.Count} commit(s)", GitToolArgs.Cap(sb.ToString()));
    }, ct);
}

/// <summary>Stashes and the reflog — where work hides when no branch points at it.</summary>
public sealed class GitRecoveryTool(GitService git) : IClientTool
{
    public string Name => "git_recovery";
    public string Description =>
        "List saved stashes and HEAD's reflog — the two places work still exists after a branch delete, "
      + "reset, or a worktree being removed. Use this before concluding that anything was lost.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("count", "How many reflog entries to return (default 30).", Required: false, Type: "integer"),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var count   = Math.Clamp(ToolArgs.Int(arguments, "count", 30), 1, 200);
        var stashes = git.GetStashes();
        var reflog  = git.GetReflog(count);

        var sb = new StringBuilder();

        sb.Append("Stashes:\n");
        if (stashes.Count == 0) sb.Append("  (none)\n");
        else foreach (var s in stashes)
            sb.Append("  stash@{").Append(s.Index).Append("}  ").Append(s.Hash).Append("  ").Append(s.Message).Append('\n');

        sb.Append("\nHEAD reflog:\n");
        if (reflog.Count == 0) sb.Append("  (none)\n");
        else foreach (var e in reflog)
            sb.Append("  ").Append(e.Hash).Append("  ").Append(e.When.ToString("yyyy-MM-dd HH:mm"))
              .Append("  ").Append(e.Message).Append('\n');

        return ToolResult.Ok($"{stashes.Count} stash(es), {reflog.Count} reflog entr(ies)",
                             GitToolArgs.Cap(sb.ToString()));
    }, ct);
}

/// <summary>Every linked worktree, with the state that decides whether removing it would lose anything.</summary>
public sealed class GitWorktreesTool(GitService git) : IClientTool
{
    public string Name => "git_worktrees";
    public string Description =>
        "List the repository's linked worktrees with their branch, whether each is merged into the mainline, "
      + "pushed, or has uncommitted changes. Note that removing a worktree deletes its folder, NOT its branch "
      + "— work there is not lost just because the folder is gone.";
    public IReadOnlyList<ClientToolParameter> Parameters => [];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.Run(() =>
    {
        var worktrees = git.GetWorktrees();
        if (worktrees.Count == 0)
            return ToolResult.Ok("no worktrees", "This repository has no linked worktrees.");

        var sb = new StringBuilder();
        foreach (var w in worktrees)
        {
            sb.Append(w.Name).Append("  [").Append(w.Branch).Append(']');
            if (w.IsBroken) sb.Append("  BROKEN (dangling registration)");
            else
            {
                sb.Append(w.IsMerged ? "  merged" : "  UNMERGED");
                sb.Append(w.IsPushed ? ", pushed" : ", not pushed");
                if (w.HasUncommittedChanges)
                    sb.Append($", {w.StagedCount} staged / {w.ModifiedCount} modified");
            }
            sb.Append("\n    ").Append(w.Path).Append('\n');
        }

        return ToolResult.Ok($"{worktrees.Count} worktree(s)", sb.ToString().TrimEnd());
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
