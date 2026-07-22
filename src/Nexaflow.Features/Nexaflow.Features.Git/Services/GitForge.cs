using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexaflow.Features.Git.Services;

/// <summary>Which hosting service a remote points at. Anything unrecognised is <see cref="Unknown"/>.</summary>
public enum GitForgeKind { Unknown, GitHub, Bitbucket }

/// <summary>
/// A repository as the forge names it. Parsed from a remote URL, which is the only forge configuration
/// Nexaflow needs — there is nothing for the user to set up beyond the credential they already store for
/// push/fetch.
/// </summary>
public sealed record GitForgeRepo(GitForgeKind Kind, string Host, string Owner, string Name)
{
    public string Slug => $"{Owner}/{Name}";
}

/// <summary>A pull request, flattened to the fields worth showing without a browser.</summary>
public sealed record GitPullRequest(
    int             Number,
    string          Title,
    string          State,
    string?         Author,
    string?         SourceBranch,
    string?         TargetBranch,
    DateTimeOffset? Updated,
    string?         Url);

/// <summary>An issue. Deliberately the same shape as a PR minus the branches — they read alike in a list.</summary>
public sealed record GitIssue(
    int             Number,
    string          Title,
    string          State,
    string?         Author,
    DateTimeOffset? Updated,
    string?         Url);

/// <summary>
/// Reads pull requests and issues from GitHub or Bitbucket over their REST APIs.
/// </summary>
/// <remarks>
/// <para>
/// Authentication reuses whatever the system credential manager already holds for the remote — the same
/// source <see cref="GitCredentialHelper"/> serves push/fetch — so there is no separate token to configure.
/// GitHub takes a bearer token; Bitbucket takes basic auth, which is also how its <c>x-token-auth</c> access
/// tokens work. A public repository answers unauthenticated, so a missing credential is not fatal.
/// </para>
/// <para>
/// The HTTP transport is injected so the JSON→record mapping is testable without a network: the parsing is
/// where the bugs live, not the socket.
/// </para>
/// </remarks>
public sealed class GitForgeClient
{
    /// <summary>Sends a request and yields the response — the seam tests replace with canned JSON.</summary>
    public delegate Task<HttpResponseMessage> Transport(HttpRequestMessage request, CancellationToken ct);

    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly Transport _send;
    private readonly Func<string, GitCredential?> _credentials;

    public GitForgeClient(Func<string, GitCredential?> credentials, Transport? send = null)
    {
        _credentials = credentials;
        _send = send ?? ((req, ct) => Shared.SendAsync(req, ct));
    }

    // ── Remote URL → forge identity ───────────────────────────────────────

    /// <summary>
    /// Parses a git remote URL into a forge repository. Handles the three shapes a remote actually takes:
    /// <c>https://host/owner/repo.git</c>, <c>git@host:owner/repo.git</c> and <c>ssh://git@host/owner/repo.git</c>.
    /// Returns null when the host isn't a forge this client speaks to.
    /// </summary>
    public static GitForgeRepo? Parse(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl)) return null;

        var url = remoteUrl.Trim();
        string host, path;

        if (url.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            // scp-style: git@host:owner/repo.git
            var at    = url.IndexOf('@');
            var colon = url.IndexOf(':', at);
            if (colon < 0) return null;
            host = url[(at + 1)..colon];
            path = url[(colon + 1)..];
        }
        else
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            host = uri.Host;
            path = uri.AbsolutePath;
        }

        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        var kind = host.Contains("github", StringComparison.OrdinalIgnoreCase)    ? GitForgeKind.GitHub
                 : host.Contains("bitbucket", StringComparison.OrdinalIgnoreCase) ? GitForgeKind.Bitbucket
                 : GitForgeKind.Unknown;
        if (kind == GitForgeKind.Unknown) return null;

        // Owner is the first segment; the repo is the last (Bitbucket workspaces can nest a project between).
        return new GitForgeRepo(kind, host, parts[0], parts[^1]);
    }

    // ── Queries ───────────────────────────────────────────────────────────

    /// <summary>Pull requests for the repo, newest first. <paramref name="state"/> is open/closed/all.</summary>
    public async Task<IReadOnlyList<GitPullRequest>> GetPullRequestsAsync(
        GitForgeRepo repo, string remoteUrl, string state, int count, CancellationToken ct)
    {
        var json = await GetAsync(repo, remoteUrl, PullRequestPath(repo, state, count), ct);
        return json is null ? [] : MapPullRequests(repo.Kind, json);
    }

    /// <summary>Issues for the repo, newest first. Bitbucket calls the same concept <c>issues</c>.</summary>
    public async Task<IReadOnlyList<GitIssue>> GetIssuesAsync(
        GitForgeRepo repo, string remoteUrl, string state, int count, CancellationToken ct)
    {
        var json = await GetAsync(repo, remoteUrl, IssuePath(repo, state, count), ct);
        return json is null ? [] : MapIssues(repo.Kind, json);
    }

    internal static string PullRequestPath(GitForgeRepo r, string state, int count) => r.Kind switch
    {
        GitForgeKind.GitHub => $"https://api.github.com/repos/{r.Slug}/pulls?state={GitHubState(state)}&per_page={count}",
        _                   => $"https://api.bitbucket.org/2.0/repositories/{r.Slug}/pullrequests?state={BitbucketPrState(state)}&pagelen={count}"
    };

    internal static string IssuePath(GitForgeRepo r, string state, int count) => r.Kind switch
    {
        // GitHub's issues endpoint also returns PRs; the mapper drops those.
        GitForgeKind.GitHub => $"https://api.github.com/repos/{r.Slug}/issues?state={GitHubState(state)}&per_page={count}",
        _                   => $"https://api.bitbucket.org/2.0/repositories/{r.Slug}/issues?pagelen={count}"
    };

    private static string GitHubState(string state) => state?.ToLowerInvariant() switch
    {
        "closed" => "closed",
        "all"    => "all",
        _        => "open"
    };

    private static string BitbucketPrState(string state) => state?.ToLowerInvariant() switch
    {
        "closed" => "MERGED",
        "all"    => "OPEN&state=MERGED&state=DECLINED",
        _        => "OPEN"
    };

    private async Task<JsonNode?> GetAsync(GitForgeRepo repo, string remoteUrl, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Nexaflow");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // A public repo answers without credentials, so a null here is not an error.
        if (_credentials(remoteUrl) is { } cred)
        {
            if (repo.Kind == GitForgeKind.GitHub)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cred.Password);
            }
            else
            {
                var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cred.Username}:{cred.Password}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            }
        }

        using var response = await _send(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        var body = await response.Content.ReadAsStringAsync(ct);
        try { return JsonNode.Parse(body); }
        catch (JsonException) { return null; }
    }

    // ── JSON → records ────────────────────────────────────────────────────

    internal static IReadOnlyList<GitPullRequest> MapPullRequests(GitForgeKind kind, JsonNode json)
    {
        var items = Items(kind, json);
        var list  = new List<GitPullRequest>();

        foreach (var item in items)
        {
            if (item is not JsonObject o) continue;

            list.Add(kind == GitForgeKind.GitHub
                ? new GitPullRequest(
                    Int(o, "number"), Str(o, "title") ?? "", Str(o, "state") ?? "",
                    Str(o["user"] as JsonObject, "login"),
                    Str(o["head"] as JsonObject, "ref"),
                    Str(o["base"] as JsonObject, "ref"),
                    Date(o, "updated_at"), Str(o, "html_url"))
                : new GitPullRequest(
                    Int(o, "id"), Str(o, "title") ?? "", Str(o, "state") ?? "",
                    Str(o["author"] as JsonObject, "display_name"),
                    Str((o["source"] as JsonObject)?["branch"] as JsonObject, "name"),
                    Str((o["destination"] as JsonObject)?["branch"] as JsonObject, "name"),
                    Date(o, "updated_on"),
                    Str((o["links"] as JsonObject)?["html"] as JsonObject, "href")));
        }
        return list;
    }

    internal static IReadOnlyList<GitIssue> MapIssues(GitForgeKind kind, JsonNode json)
    {
        var items = Items(kind, json);
        var list  = new List<GitIssue>();

        foreach (var item in items)
        {
            if (item is not JsonObject o) continue;

            // GitHub returns pull requests from the issues endpoint too — they carry a pull_request member.
            if (kind == GitForgeKind.GitHub && o.ContainsKey("pull_request")) continue;

            list.Add(kind == GitForgeKind.GitHub
                ? new GitIssue(Int(o, "number"), Str(o, "title") ?? "", Str(o, "state") ?? "",
                               Str(o["user"] as JsonObject, "login"), Date(o, "updated_at"), Str(o, "html_url"))
                : new GitIssue(Int(o, "id"), Str(o, "title") ?? "", Str(o, "state") ?? "",
                               Str(o["reporter"] as JsonObject, "display_name"), Date(o, "updated_on"),
                               Str((o["links"] as JsonObject)?["html"] as JsonObject, "href")));
        }
        return list;
    }

    /// <summary>GitHub returns a bare array; Bitbucket wraps the page in <c>values</c>.</summary>
    private static IEnumerable<JsonNode?> Items(GitForgeKind kind, JsonNode json) =>
        kind == GitForgeKind.GitHub
            ? json as JsonArray ?? []
            : (json["values"] as JsonArray) ?? [];

    private static string? Str(JsonObject? o, string key) => o?[key]?.GetValue<string>();

    private static int Int(JsonObject o, string key) =>
        o[key] is { } n && int.TryParse(n.ToString(), out var i) ? i : 0;

    private static DateTimeOffset? Date(JsonObject o, string key) =>
        DateTimeOffset.TryParse(o[key]?.ToString(), out var d) ? d : null;
}
