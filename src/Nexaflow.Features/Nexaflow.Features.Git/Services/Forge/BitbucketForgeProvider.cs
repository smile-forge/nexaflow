using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Nexaflow.Features.Git.Services.Forge;

/// <summary>Bitbucket Cloud.</summary>
public sealed class BitbucketForgeProvider : IGitForgeProvider
{
    public string Id => "bitbucket";
    public string DisplayName => "Bitbucket";

    public bool Handles(string host) => host.Contains("bitbucket", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Bitbucket addresses a repo as <c>workspace/repo</c>. A UI URL may carry more segments
    /// (<c>workspace/repo/src/main/…</c>), so the first two are taken rather than requiring exactly two.
    /// </summary>
    public GitForgeRepo? ParseRepository(string host, string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? new GitForgeRepo(Id, host, parts[0], parts[1]) : null;
    }

    public string PullRequestsUrl(GitForgeRepo repo, string state, int count) =>
        $"https://api.bitbucket.org/2.0/repositories/{repo.Owner}/{repo.Name}/pullrequests"
      + $"?{PrStateQuery(state)}&pagelen={count}";

    public string IssuesUrl(GitForgeRepo repo, string state, int count) =>
        $"https://api.bitbucket.org/2.0/repositories/{repo.Owner}/{repo.Name}/issues?pagelen={count}";

    /// <summary>
    /// Bitbucket spells states in caps and expresses "any" as a repeated parameter, so the whole query
    /// fragment is built here rather than a single value being substituted.
    /// </summary>
    private static string PrStateQuery(string state) => state?.ToLowerInvariant() switch
    {
        "closed" => "state=MERGED&state=DECLINED",
        "all"    => "state=OPEN&state=MERGED&state=DECLINED",
        _        => "state=OPEN"
    };

    /// <summary>
    /// Bitbucket uses basic auth. Its repository/project/workspace access tokens authenticate with the fixed
    /// username <c>x-token-auth</c>, which is what the pull fallback already stores.
    /// </summary>
    public void Authenticate(HttpRequestMessage request, GitCredential credential)
    {
        var basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{credential.Username}:{credential.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    public IReadOnlyList<GitPullRequest> MapPullRequests(JsonNode json)
    {
        var list = new List<GitPullRequest>();
        foreach (var item in Values(json))
        {
            if (item is not JsonObject o) continue;
            list.Add(new GitPullRequest(
                ForgeJson.Int(o, "id"),
                ForgeJson.Str(o, "title") ?? string.Empty,
                ForgeJson.Str(o, "state") ?? string.Empty,
                ForgeJson.Str(ForgeJson.Obj(o, "author"), "display_name"),
                ForgeJson.Str(ForgeJson.Obj(ForgeJson.Obj(o, "source"), "branch"), "name"),
                ForgeJson.Str(ForgeJson.Obj(ForgeJson.Obj(o, "destination"), "branch"), "name"),
                ForgeJson.Date(o, "updated_on"),
                ForgeJson.Str(ForgeJson.Obj(ForgeJson.Obj(o, "links"), "html"), "href")));
        }
        return list;
    }

    public IReadOnlyList<GitIssue> MapIssues(JsonNode json)
    {
        var list = new List<GitIssue>();
        foreach (var item in Values(json))
        {
            if (item is not JsonObject o) continue;
            list.Add(new GitIssue(
                ForgeJson.Int(o, "id"),
                ForgeJson.Str(o, "title") ?? string.Empty,
                ForgeJson.Str(o, "state") ?? string.Empty,
                ForgeJson.Str(ForgeJson.Obj(o, "reporter"), "display_name"),
                ForgeJson.Date(o, "updated_on"),
                ForgeJson.Str(ForgeJson.Obj(ForgeJson.Obj(o, "links"), "html"), "href")));
        }
        return list;
    }

    /// <summary>Bitbucket wraps every listing in a page object; the rows live under <c>values</c>.</summary>
    private static JsonArray Values(JsonNode json) => json["values"] as JsonArray ?? [];
}
