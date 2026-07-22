using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexaflow.Features.Git.Services.Forge;

/// <summary>
/// Reads pull requests and issues from whichever forge hosts the repository. Contains <b>no</b> knowledge of
/// any particular service: host matching, URL shapes, authentication and JSON mapping all come from an
/// <see cref="IGitForgeProvider"/>, so adding a forge never touches this class.
/// </summary>
/// <remarks>
/// <para>
/// Authentication reuses whatever the system credential manager already holds for the remote — the same
/// source <see cref="GitCredentialHelper"/> serves push/fetch — so there is no separate token to configure.
/// A public repository answers unauthenticated, so a missing credential is not an error.
/// </para>
/// <para>
/// The HTTP transport is injected: the JSON→record mapping is where the bugs live, not the socket, and this
/// makes that mapping testable without a network.
/// </para>
/// </remarks>
public sealed class GitForgeClient
{
    /// <summary>Sends a request and yields the response — the seam tests replace with canned JSON.</summary>
    public delegate Task<HttpResponseMessage> Transport(HttpRequestMessage request, CancellationToken ct);

    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly Transport _send;
    private readonly Func<string, GitCredential?> _credentials;
    private readonly IReadOnlyList<IGitForgeProvider> _providers;

    /// <param name="credentials">Resolves a credential for a remote URL, or null when none is stored.</param>
    /// <param name="send">HTTP transport; defaults to a shared <see cref="HttpClient"/>.</param>
    /// <param name="providers">Forge providers to consider; defaults to <see cref="GitForgeRegistry.BuiltIn"/>.</param>
    public GitForgeClient(Func<string, GitCredential?> credentials,
                          Transport? send = null,
                          IEnumerable<IGitForgeProvider>? providers = null)
    {
        _credentials = credentials;
        _send        = send ?? ((req, ct) => Shared.SendAsync(req, ct));
        _providers   = [.. providers ?? GitForgeRegistry.BuiltIn];
    }

    /// <summary>The providers this client will consider — what "which forges are supported" means at runtime.</summary>
    public IReadOnlyList<IGitForgeProvider> Providers => _providers;

    // ── Remote URL → forge identity ───────────────────────────────────────

    /// <summary>
    /// Parses a git remote URL into a repository identity by asking each provider in turn. Handles the three
    /// shapes a remote takes — <c>https://host/path</c>, <c>git@host:path</c> and <c>ssh://git@host/path</c> —
    /// then delegates the path itself, because forges disagree about what a repo path looks like.
    /// Returns null when no provider claims the host, or the claiming provider rejects the path.
    /// </summary>
    public GitForgeRepo? Parse(string? remoteUrl)
    {
        if (!TrySplit(remoteUrl, out var host, out var path)) return null;
        return GitForgeRegistry.For(host, _providers)?.ParseRepository(host, path);
    }

    /// <summary>Splits a remote URL into host and repository path, normalising away any <c>.git</c> suffix.</summary>
    internal static bool TrySplit(string? remoteUrl, out string host, out string path)
    {
        host = string.Empty;
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(remoteUrl)) return false;

        var url = remoteUrl.Trim();

        if (url.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            // scp-style: git@host:owner/repo.git — no scheme, and a colon where a slash would be.
            var at    = url.IndexOf('@');
            var colon = url.IndexOf(':', at);
            if (colon < 0) return false;
            host = url[(at + 1)..colon];
            path = url[(colon + 1)..];
        }
        else
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            host = uri.Host;
            path = uri.AbsolutePath;
        }

        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];

        return host.Length > 0 && path.Length > 0;
    }

    // ── Queries ───────────────────────────────────────────────────────────

    /// <summary>Pull requests for the repo. <paramref name="state"/> is the neutral open/closed/all.</summary>
    public async Task<IReadOnlyList<GitPullRequest>> GetPullRequestsAsync(
        GitForgeRepo repo, string remoteUrl, string state, int count, CancellationToken ct)
    {
        if (ProviderFor(repo) is not { } provider) return [];
        var json = await GetAsync(provider, remoteUrl, provider.PullRequestsUrl(repo, state, count), ct);
        return json is null ? [] : provider.MapPullRequests(json);
    }

    /// <summary>Issues for the repo.</summary>
    public async Task<IReadOnlyList<GitIssue>> GetIssuesAsync(
        GitForgeRepo repo, string remoteUrl, string state, int count, CancellationToken ct)
    {
        if (ProviderFor(repo) is not { } provider) return [];
        var json = await GetAsync(provider, remoteUrl, provider.IssuesUrl(repo, state, count), ct);
        return json is null ? [] : provider.MapIssues(json);
    }

    /// <summary>The provider that produced a repo record — by id, so a stale record can't be misrouted.</summary>
    public IGitForgeProvider? ProviderFor(GitForgeRepo repo) => GitForgeRegistry.ById(repo.ProviderId, _providers);

    private async Task<JsonNode?> GetAsync(IGitForgeProvider provider, string remoteUrl, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Nexaflow");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // A public repo answers without credentials, so a null here is not an error.
        if (_credentials(remoteUrl) is { } cred) provider.Authenticate(request, cred);

        using var response = await _send(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        var body = await response.Content.ReadAsStringAsync(ct);
        try { return JsonNode.Parse(body); }
        catch (JsonException) { return null; }
    }
}
