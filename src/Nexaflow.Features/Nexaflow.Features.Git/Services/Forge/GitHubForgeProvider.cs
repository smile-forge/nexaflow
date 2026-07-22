using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Nexaflow.Features.Git.Services.Forge;

/// <summary>GitHub (and GitHub Enterprise hosts whose name contains "github").</summary>
public sealed class GitHubForgeProvider : IGitForgeProvider
{
    public string Id => "github";
    public string DisplayName => "GitHub";

    public bool Handles(string host) => host.Contains("github", StringComparison.OrdinalIgnoreCase);

    /// <summary>GitHub is always <c>owner/repo</c>; anything deeper is a URL to something other than a repo.</summary>
    public GitForgeRepo? ParseRepository(string host, string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? new GitForgeRepo(Id, host, parts[0], parts[1]) : null;
    }

    public string PullRequestsUrl(GitForgeRepo repo, string state, int count) =>
        $"https://api.github.com/repos/{repo.Owner}/{repo.Name}/pulls?state={State(state)}&per_page={count}";

    public string IssuesUrl(GitForgeRepo repo, string state, int count) =>
        $"https://api.github.com/repos/{repo.Owner}/{repo.Name}/issues?state={State(state)}&per_page={count}";

    private static string State(string state) => state?.ToLowerInvariant() switch
    {
        "closed" => "closed",
        "all"    => "all",
        _        => "open"
    };

    /// <summary>GitHub takes the token as a bearer; the username is irrelevant to it.</summary>
    public void Authenticate(HttpRequestMessage request, GitCredential credential) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Password);

    public IReadOnlyList<GitPullRequest> MapPullRequests(JsonNode json)
    {
        var list = new List<GitPullRequest>();
        foreach (var item in json as JsonArray ?? [])
        {
            if (item is not JsonObject o) continue;
            list.Add(new GitPullRequest(
                ForgeJson.Int(o, "number"),
                ForgeJson.Str(o, "title") ?? string.Empty,
                ForgeJson.Str(o, "state") ?? string.Empty,
                ForgeJson.Str(ForgeJson.Obj(o, "user"), "login"),
                ForgeJson.Str(ForgeJson.Obj(o, "head"), "ref"),
                ForgeJson.Str(ForgeJson.Obj(o, "base"), "ref"),
                ForgeJson.Date(o, "updated_at"),
                ForgeJson.Str(o, "html_url")));
        }
        return list;
    }

    public IReadOnlyList<GitIssue> MapIssues(JsonNode json)
    {
        var list = new List<GitIssue>();
        foreach (var item in json as JsonArray ?? [])
        {
            if (item is not JsonObject o) continue;

            // GitHub's issues endpoint also returns pull requests; they carry a pull_request member. Without
            // this, every PR would be listed twice — once by git_pull_requests and once by git_issues.
            if (o.ContainsKey("pull_request")) continue;

            list.Add(new GitIssue(
                ForgeJson.Int(o, "number"),
                ForgeJson.Str(o, "title") ?? string.Empty,
                ForgeJson.Str(o, "state") ?? string.Empty,
                ForgeJson.Str(ForgeJson.Obj(o, "user"), "login"),
                ForgeJson.Date(o, "updated_at"),
                ForgeJson.Str(o, "html_url")));
        }
        return list;
    }
}
