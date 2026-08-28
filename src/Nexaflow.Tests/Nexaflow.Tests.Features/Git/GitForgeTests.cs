using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Nexaflow.Features.Git.Services;
using Nexaflow.Features.Git.Services.Forge;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Git;

/// <summary>
/// Forge integration: turning a remote URL into a repository identity, and forge JSON into records. Both are
/// pure, so they are tested without a network — the HTTP transport is injected, and the JSON is the shape the
/// forges actually return. The provider abstraction means each forge's parsing and mapping is exercised on its
/// own implementation.
/// </summary>
[TestClass]
public class GitForgeTests
{
    // A client with the built-in providers and a null-credential resolver, for the pure Parse/mapping paths.
    private static GitForgeClient Client(GitForgeClient.Transport? send = null) =>
        new(credentials: _ => null, send: send);

    // ── Remote URL → forge identity (git-ai-act-forge) ─────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-forge")]
    public void Parse_HandlesHttpsScpAndSshRemotes()
    {
        var forge = Client();
        foreach (var url in new[]
                 {
                     "https://github.com/smile-forge/nexaflow.git",
                     "https://github.com/smile-forge/nexaflow",
                     "git@github.com:smile-forge/nexaflow.git",
                     "ssh://git@github.com/smile-forge/nexaflow.git",
                 })
        {
            var repo = forge.Parse(url);
            Assert.IsNotNull(repo, $"failed to parse {url}");
            Assert.AreEqual("github", repo!.ProviderId, url);
            Assert.AreEqual("smile-forge/nexaflow", repo.Slug, url);
        }
    }

    [TestMethod]
    [CoversNode("git-ai-act-forge")]
    public void Parse_RecognisesBitbucket_AndRejectsUnsupportedHosts()
    {
        var forge = Client();

        var bb = forge.Parse("https://bitbucket.org/team/repo.git");
        Assert.AreEqual("bitbucket", bb!.ProviderId);
        Assert.AreEqual("team/repo", bb.Slug);

        Assert.IsNull(forge.Parse("https://gitlab.com/team/repo.git"), "no provider handles this host yet");
        Assert.IsNull(forge.Parse("https://github.com/onlyowner"), "GitHub needs exactly owner/repo");
        Assert.IsNull(forge.Parse(""));
        Assert.IsNull(forge.Parse(null));
    }

    [TestMethod]
    [CoversNode("git-ai-act-forge")]
    public void TrySplit_SeparatesHostAndPath_AcrossUrlShapes()
    {
        Assert.IsTrue(GitForgeClient.TrySplit("git@dev.azure.com:org/proj/_git/repo.git", out var host, out var path));
        Assert.AreEqual("dev.azure.com", host);
        Assert.AreEqual("org/proj/_git/repo", path, "the .git suffix is stripped, the rest of the path preserved");

        // The splitter is forge-neutral: it separates host/path even for a host no provider yet claims,
        // which is exactly what lets a new provider be added without touching the client.
        Assert.IsFalse(GitForgeClient.TrySplit("not a url", out _, out _));
    }

    // ── Provider registry ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-forge")]
    public void Registry_MatchesByHost_AndLooksUpById()
    {
        Assert.AreEqual("github",    GitForgeRegistry.For("github.com")!.Id);
        Assert.AreEqual("bitbucket", GitForgeRegistry.For("bitbucket.org")!.Id);
        Assert.IsNull(GitForgeRegistry.For("dev.azure.com"), "no provider claims Azure DevOps yet");

        Assert.AreEqual("GitHub", GitForgeRegistry.ById("github")!.DisplayName);
        Assert.IsNull(GitForgeRegistry.ById("nope"));
    }

    [TestMethod]
    [CoversNode("git-ai-act-forge")]
    public void ANewProvider_PluggedIn_IsUsedWithoutTouchingTheClient()
    {
        // Proves the extension point: a provider for a brand-new host works purely by being passed in.
        var forge = new GitForgeClient(credentials: _ => null, providers: [new FakeAzureProvider()]);

        var repo = forge.Parse("https://dev.azure.com/org/project/_git/repo");
        Assert.IsNotNull(repo);
        Assert.AreEqual("azure", repo!.ProviderId);
        Assert.AreEqual("project", repo.Project, "the third path level survives, which owner/repo alone could not carry");
        Assert.AreEqual("repo", repo.Name);
    }

    // ── JSON → records: GitHub (git-ai-act-forge-prs / -issues) ────────────────

    private const string GitHubPrJson = """
        [
          {
            "number": 153, "title": "Git AI tools", "state": "open",
            "user":  { "login": "Jones-Adam" },
            "head":  { "ref": "claude/git-ai-tier1" },
            "base":  { "ref": "main" },
            "updated_at": "2026-07-22T10:00:00Z",
            "html_url": "https://github.com/smile-forge/nexaflow/pull/153"
          }
        ]
        """;

    [TestMethod]
    [CoversNode("git-ai-act-forge-prs")]
    public void GitHubProvider_MapsPrHeadAndBaseBranches()
    {
        var prs = new GitHubForgeProvider().MapPullRequests(JsonNode.Parse(GitHubPrJson)!);

        Assert.AreEqual(1, prs.Count);
        Assert.AreEqual(153, prs[0].Number);
        Assert.AreEqual("Git AI tools", prs[0].Title);
        Assert.AreEqual("claude/git-ai-tier1", prs[0].SourceBranch);
        Assert.AreEqual("main", prs[0].TargetBranch);
        Assert.AreEqual("Jones-Adam", prs[0].Author);
        Assert.IsNotNull(prs[0].Updated);
    }

    [TestMethod]
    [CoversNode("git-ai-act-forge-issues")]
    public void GitHubProvider_DropsPullRequestsFromTheIssuesEndpoint()
    {
        // GitHub's /issues returns PRs too — they carry a pull_request member and must not be listed twice.
        const string json = """
            [
              { "number": 10, "title": "A real issue", "state": "open", "user": { "login": "ann" } },
              { "number": 11, "title": "Actually a PR", "state": "open", "user": { "login": "bob" },
                "pull_request": { "url": "https://api.github.com/repos/x/y/pulls/11" } }
            ]
            """;

        var issues = new GitHubForgeProvider().MapIssues(JsonNode.Parse(json)!);

        Assert.AreEqual(1, issues.Count);
        Assert.AreEqual("A real issue", issues[0].Title);
    }

    // ── JSON → records: Bitbucket ──────────────────────────────────────────────

    private const string BitbucketPrJson = """
        {
          "values": [
            {
              "id": 7, "title": "Add codec", "state": "OPEN",
              "author": { "display_name": "Sam" },
              "source":      { "branch": { "name": "feature/codec" } },
              "destination": { "branch": { "name": "develop" } },
              "updated_on": "2026-07-20T09:30:00Z",
              "links": { "html": { "href": "https://bitbucket.org/team/repo/pull-requests/7" } }
            }
          ]
        }
        """;

    [TestMethod]
    [CoversNode("git-ai-act-forge-prs")]
    public void BitbucketProvider_ReadsItsNestedBranchShape()
    {
        var prs = new BitbucketForgeProvider().MapPullRequests(JsonNode.Parse(BitbucketPrJson)!);

        Assert.AreEqual(1, prs.Count);
        Assert.AreEqual(7, prs[0].Number);
        Assert.AreEqual("feature/codec", prs[0].SourceBranch, "source.branch.name, not head.ref");
        Assert.AreEqual("develop", prs[0].TargetBranch);
        Assert.AreEqual("Sam", prs[0].Author);
    }

    [TestMethod]
    [CoversNode("git-ai-act-forge")]
    public void MalformedPayloads_YieldNothingRatherThanThrowing()
    {
        var gh = new GitHubForgeProvider();
        var bb = new BitbucketForgeProvider();

        // Empty and wrong-shaped payloads are normal (a 200 with no results; a page with no "values").
        Assert.AreEqual(0, gh.MapPullRequests(JsonNode.Parse("[]")!).Count);
        Assert.AreEqual(0, bb.MapPullRequests(JsonNode.Parse("{}")!).Count);
        Assert.AreEqual(0, gh.MapPullRequests(JsonNode.Parse("{}")!).Count);   // object, not the expected array

        // An entry missing every field maps to defaults rather than throwing — one bad row must not lose the page.
        var sparse = gh.MapIssues(JsonNode.Parse("[{}]")!);
        Assert.AreEqual(1, sparse.Count);
        Assert.AreEqual(0, sparse[0].Number);
        Assert.AreEqual(string.Empty, sparse[0].Title);
    }

    // ── Request shaping + transport ────────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-forge-prs")]
    public async Task GetPullRequestsAsync_SendsABearerTokenToGitHub_AndParsesTheResult()
    {
        HttpRequestMessage? seen = null;
        var forge = new GitForgeClient(
            credentials: _ => new GitCredential("x-token-auth", "SECRET"),
            send: (req, _) =>
            {
                seen = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(GitHubPrJson)
                });
            });

        var repo = forge.Parse("https://github.com/smile-forge/nexaflow.git")!;
        var prs  = await forge.GetPullRequestsAsync(repo, "https://github.com/smile-forge/nexaflow.git",
                                                    "open", 30, CancellationToken.None);

        Assert.AreEqual(1, prs.Count);
        Assert.AreEqual("Bearer", seen!.Headers.Authorization!.Scheme);
        Assert.AreEqual("SECRET", seen.Headers.Authorization.Parameter);
        StringAssert.Contains(seen.RequestUri!.ToString(), "/repos/smile-forge/nexaflow/pulls");
    }

    [TestMethod]
    [CoversNode("git-ai-act-forge-prs")]
    public async Task GetPullRequestsAsync_SendsBasicAuthToBitbucket()
    {
        HttpRequestMessage? seen = null;
        var forge = new GitForgeClient(
            credentials: _ => new GitCredential("x-token-auth", "TOKEN"),
            send: (req, _) =>
            {
                seen = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BitbucketPrJson)
                });
            });

        var repo = forge.Parse("https://bitbucket.org/team/repo.git")!;
        var prs  = await forge.GetPullRequestsAsync(repo, "https://bitbucket.org/team/repo.git",
                                                    "open", 30, CancellationToken.None);

        Assert.AreEqual(1, prs.Count);
        Assert.AreEqual("Basic", seen!.Headers.Authorization!.Scheme, "Bitbucket uses basic auth, not a bearer token");
        StringAssert.Contains(seen.RequestUri!.ToString(), "/repositories/team/repo/pullrequests");
    }

    [TestMethod]
    [CoversNode("git-ai-act-forge")]
    public async Task AFailedResponse_YieldsAnEmptyList_NotAnException()
    {
        var forge = Client(send: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var repo = forge.Parse("https://github.com/x/y")!;
        var prs  = await forge.GetPullRequestsAsync(repo, "https://github.com/x/y", "open", 5, CancellationToken.None);

        Assert.AreEqual(0, prs.Count);
    }

    [TestMethod]
    [CoversNode("git-ai-act-forge")]
    public async Task NoStoredCredential_StillSendsTheRequest_Unauthenticated()
    {
        HttpRequestMessage? seen = null;
        var forge = new GitForgeClient(
            credentials: _ => null,
            send: (req, _) =>
            {
                seen = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                });
            });

        var repo = forge.Parse("https://github.com/x/y")!;
        await forge.GetPullRequestsAsync(repo, "https://github.com/x/y", "open", 5, CancellationToken.None);

        Assert.IsNull(seen!.Headers.Authorization, "a public repository must not require a credential");
    }

    /// <summary>
    /// A stand-in Azure DevOps provider, used only to prove the extension point: a three-level repo path
    /// (<c>org/project/_git/repo</c>) parses without any change to the client or the registry.
    /// </summary>
    private sealed class FakeAzureProvider : IGitForgeProvider
    {
        public string Id => "azure";
        public string DisplayName => "Azure DevOps";
        public bool Handles(string host) => host.Contains("azure", StringComparison.OrdinalIgnoreCase);

        public GitForgeRepo? ParseRepository(string host, string path)
        {
            // org/project/_git/repo
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var git   = Array.IndexOf(parts, "_git");
            return git > 0 && git + 1 < parts.Length
                ? new GitForgeRepo(Id, host, parts[0], parts[git + 1], Project: parts[git - 1])
                : null;
        }

        public string PullRequestsUrl(GitForgeRepo r, string s, int c) => "https://example/pr";
        public string IssuesUrl(GitForgeRepo r, string s, int c) => "https://example/issues";
        public void Authenticate(HttpRequestMessage req, GitCredential cred) { }
        public IReadOnlyList<GitPullRequest> MapPullRequests(JsonNode json) => [];
        public IReadOnlyList<GitIssue> MapIssues(JsonNode json) => [];
    }
}
