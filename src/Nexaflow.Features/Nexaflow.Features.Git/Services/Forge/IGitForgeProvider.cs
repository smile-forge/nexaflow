using System.Net.Http;
using System.Text.Json.Nodes;

namespace Nexaflow.Features.Git.Services.Forge;

/// <summary>
/// Everything that differs between one hosting service and another. Supporting a new forge — Azure DevOps,
/// GitLab, Gitea — is a single implementation of this interface plus one line in
/// <see cref="GitForgeRegistry"/>; nothing in <see cref="GitForgeClient"/> or the tools needs to change.
/// </summary>
/// <remarks>
/// The five things that actually vary, and why each is here rather than in the client:
/// <list type="bullet">
///   <item><b>Host matching and path parsing</b> — Azure DevOps addresses a repo as <c>org/project/_git/repo</c>,
///         so even splitting the URL is provider-specific.</item>
///   <item><b>Endpoints</b> — different hosts, paths and state vocabularies (<c>open</c> vs <c>OPEN</c>).</item>
///   <item><b>Authentication</b> — GitHub takes a bearer token, Bitbucket and Azure DevOps take basic auth.</item>
///   <item><b>Envelope</b> — a bare array, or a page object wrapping <c>values</c>.</item>
///   <item><b>Field names</b> — <c>head.ref</c> versus <c>source.branch.name</c>.</item>
/// </list>
/// </remarks>
public interface IGitForgeProvider
{
    /// <summary>Stable identifier used in <see cref="GitForgeRepo.ProviderId"/> and in messages.</summary>
    string Id { get; }

    /// <summary>What a user would call it ("GitHub", "Azure DevOps").</summary>
    string DisplayName { get; }

    /// <summary>Whether this provider serves the given remote host (e.g. "github.com", "dev.azure.com").</summary>
    bool Handles(string host);

    /// <summary>
    /// Splits a remote's path into a repository identity, or null when the path isn't one this provider
    /// recognises. <paramref name="path"/> arrives already trimmed of leading/trailing slashes and any
    /// <c>.git</c> suffix; the scheme and host have been removed.
    /// </summary>
    GitForgeRepo? ParseRepository(string host, string path);

    /// <summary>The API URL listing pull requests. <paramref name="state"/> is open/closed/all, forge-neutral.</summary>
    string PullRequestsUrl(GitForgeRepo repo, string state, int count);

    /// <summary>The API URL listing issues.</summary>
    string IssuesUrl(GitForgeRepo repo, string state, int count);

    /// <summary>
    /// Applies the credential to an outgoing request. Called only when one was found — every provider must
    /// still answer for public repositories with no credential at all.
    /// </summary>
    void Authenticate(HttpRequestMessage request, GitCredential credential);

    /// <summary>Maps a pull-request listing payload. Never throws: a malformed page yields what it can.</summary>
    IReadOnlyList<GitPullRequest> MapPullRequests(JsonNode json);

    /// <summary>Maps an issue listing payload. Never throws.</summary>
    IReadOnlyList<GitIssue> MapIssues(JsonNode json);
}

/// <summary>
/// The forges Nexaflow knows about. Deliberately a plain list rather than reflection-based discovery: forge
/// providers ship in this assembly, and an explicit list is the thing a reader greps for when asking
/// "what do we support?".
/// </summary>
public static class GitForgeRegistry
{
    /// <summary>Every built-in provider, in match order.</summary>
    public static IReadOnlyList<IGitForgeProvider> BuiltIn { get; } =
    [
        new GitHubForgeProvider(),
        new BitbucketForgeProvider(),
        // Add a provider here (and a node under git-ai-act-forge-providers) to support another forge.
    ];

    /// <summary>The first provider claiming <paramref name="host"/>, or null.</summary>
    public static IGitForgeProvider? For(string host, IEnumerable<IGitForgeProvider>? providers = null) =>
        (providers ?? BuiltIn).FirstOrDefault(p => p.Handles(host));

    /// <summary>Looks a provider up by <see cref="IGitForgeProvider.Id"/>.</summary>
    public static IGitForgeProvider? ById(string id, IEnumerable<IGitForgeProvider>? providers = null) =>
        (providers ?? BuiltIn).FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
