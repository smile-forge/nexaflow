using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Git.Services;
using Nexaflow.Features.Git.Services.Forge;

namespace Nexaflow.Features.Git.ClientTools;

/// <summary>
/// Shared plumbing for the forge tools: resolve the repository's origin remote, work out which forge it is,
/// and turn "no remote / not a forge we speak to" into an explanation rather than an empty result.
/// </summary>
internal static class ForgeContext
{
    /// <summary>The origin remote's URL (falling back to the first remote), or null when there is none.</summary>
    public static string? RemoteUrl(GitService git)
    {
        var remotes = git.GetRemotes();
        return (remotes.FirstOrDefault(r => r.Name == "origin") ?? remotes.FirstOrDefault())?.Url;
    }

    /// <summary>
    /// Resolves the forge repository, or yields the error a caller should surface. Kept as one place so both
    /// tools explain an unsupported host identically — and so the message names the forges actually
    /// registered rather than a hard-coded pair that would rot as providers are added.
    /// </summary>
    public static bool TryResolve(GitService git, GitForgeClient forge,
                                  out GitForgeRepo repo, out string? url, out ToolResult error)
    {
        repo  = null!;
        error = default!;
        url   = RemoteUrl(git);

        if (string.IsNullOrWhiteSpace(url))
        {
            error = ToolResult.Error("This repository has no remote, so there is no forge to query.");
            return false;
        }

        if (forge.Parse(url) is not { } parsed)
        {
            var supported = string.Join(", ", forge.Providers.Select(p => p.DisplayName));
            error = ToolResult.Error($"'{url}' is not hosted on a forge Nexaflow can query (supported: {supported}).");
            return false;
        }

        repo = parsed;
        return true;
    }
}

/// <summary>Pull requests from the hosting forge.</summary>
public sealed class GitPullRequestsTool(GitService git, GitForgeClient forge) : IClientTool
{
    public string Name => "git_pull_requests";
    public string Description =>
        "List pull requests from the repository's hosting service (currently GitHub or Bitbucket). PR titles "
      + "are far better release-note material than raw commit subjects, and a PR is often the only surviving "
      + "record of why a branch exists. Requires network access; authentication reuses the stored git credential.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("state", "Which to return: 'open' (default), 'closed', or 'all'.", Required: false),
        new("count", "How many to return (default 30, max 100).", Required: false, Type: "integer"),
        new("query", "Only return PRs whose title contains this text.", Required: false),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (!ForgeContext.TryResolve(git, forge, out var repo, out var url, out var error)) return error;

        var state = ToolArgs.Str(arguments, "state") is { Length: > 0 } s ? s : "open";
        var count = Math.Clamp(ToolArgs.Int(arguments, "count", 30), 1, 100);
        var query = ToolArgs.Str(arguments, "query");

        IReadOnlyList<GitPullRequest> prs;
        try { prs = await forge.GetPullRequestsAsync(repo, url!, state, count, ct); }
        catch (Exception ex) { return ToolResult.Error($"Could not reach {repo.Host}: {ex.Message}"); }

        if (!string.IsNullOrWhiteSpace(query))
            prs = [.. prs.Where(p => p.Title.Contains(query!, StringComparison.OrdinalIgnoreCase))];

        if (prs.Count == 0)
            return ToolResult.Ok("no pull requests",
                $"No {state} pull requests on {repo.Slug}" + (query is null ? "." : $" matching '{query}'."));

        var sb = new StringBuilder();
        foreach (var p in prs)
        {
            sb.Append('#').Append(p.Number).Append("  [").Append(p.State).Append("]  ").Append(p.Title);
            if (p.SourceBranch is not null) sb.Append("\n    ").Append(p.SourceBranch).Append(" → ").Append(p.TargetBranch);
            if (p.Author is not null)       sb.Append("   by ").Append(p.Author);
            if (p.Url is not null)          sb.Append("\n    ").Append(p.Url);
            sb.Append('\n');
        }

        return ToolResult.Ok($"{prs.Count} pull request(s) on {repo.Slug}", GitToolArgs.Cap(sb.ToString()));
    }
}

/// <summary>Issues from the hosting forge.</summary>
public sealed class GitIssuesTool(GitService git, GitForgeClient forge) : IClientTool
{
    public string Name => "git_issues";
    public string Description =>
        "List issues from the repository's hosting service (GitHub or Bitbucket). Requires network access; "
      + "authentication reuses the stored git credential.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("state", "Which to return: 'open' (default), 'closed', or 'all'.", Required: false),
        new("count", "How many to return (default 30, max 100).", Required: false, Type: "integer"),
        new("query", "Only return issues whose title contains this text.", Required: false),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (!ForgeContext.TryResolve(git, forge, out var repo, out var url, out var error)) return error;

        var state = ToolArgs.Str(arguments, "state") is { Length: > 0 } s ? s : "open";
        var count = Math.Clamp(ToolArgs.Int(arguments, "count", 30), 1, 100);
        var query = ToolArgs.Str(arguments, "query");

        IReadOnlyList<GitIssue> issues;
        try { issues = await forge.GetIssuesAsync(repo, url!, state, count, ct); }
        catch (Exception ex) { return ToolResult.Error($"Could not reach {repo.Host}: {ex.Message}"); }

        if (!string.IsNullOrWhiteSpace(query))
            issues = [.. issues.Where(i => i.Title.Contains(query!, StringComparison.OrdinalIgnoreCase))];

        if (issues.Count == 0)
            return ToolResult.Ok("no issues",
                $"No {state} issues on {repo.Slug}" + (query is null ? "." : $" matching '{query}'."));

        var sb = new StringBuilder();
        foreach (var i in issues)
        {
            sb.Append('#').Append(i.Number).Append("  [").Append(i.State).Append("]  ").Append(i.Title);
            if (i.Author is not null) sb.Append("   by ").Append(i.Author);
            if (i.Url is not null)    sb.Append("\n    ").Append(i.Url);
            sb.Append('\n');
        }

        return ToolResult.Ok($"{issues.Count} issue(s) on {repo.Slug}", GitToolArgs.Cap(sb.ToString()));
    }
}
