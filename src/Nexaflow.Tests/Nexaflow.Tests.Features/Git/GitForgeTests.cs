using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Nexaflow.Features.Git.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Git;

/// <summary>
/// Forge integration: turning a remote URL into a repository identity, and forge JSON into records. Both
/// halves are pure, so they are tested without a network — the HTTP transport is injected, and the JSON is
/// the shape GitHub and Bitbucket actually return.
/// </summary>
/// <remarks>
/// Deliberately no live-network test. What breaks in practice is URL parsing and field mapping (GitHub's
/// <c>head.ref</c> versus Bitbucket's <c>source.branch.name</c>), and both are covered here deterministically.
/// </remarks>
[TestClass]
public class GitForgeTests
{
    // ── Remote URL → forge identity ───────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-forge")]
    public void Parse_HandlesHttpsScpAndSshRemotes()
    {
        foreach (var url in new[]
                 {
                     "https://github.com/smile-forge/nexaflow.git",
                     "https://github.com/smile-forge/nexaflow",
                     "git@github.com:smile-forge/nexaflow.git",
                     "ssh://git@github.com/smile-forge/nexaflow.git",
                 })
        {
            var repo = GitForgeClient.Parse(url);
            Assert.IsNotNull(repo, $"failed to parse {url}");
            Assert.AreEqual(GitForgeKind.GitHub, repo!.Kind, url);
            Assert.AreEqual("smile-forge/nexaflow", repo.Slug, url);
        }
    }

    [TestMethod]
    [CoversNode("git-ai-act-forge")]
    public void Parse_RecognisesBitbucket_AndRejectsEverythingElse()
    {
        var bb = GitForgeClient.Parse("https://bitbucket.org/team/repo.git");
        Assert.AreEqual(GitForgeKind.Bitbucket, bb!.Kind);
        Assert.AreEqual("team/repo", bb.Slug);

        Assert.IsNull(GitForgeClient.Parse("https://gitlab.com/team/repo.git"), "unsupported host");
        Assert.IsNull(GitForgeClient.Parse("https://github.com/onlyowner"), "not a repository path");
        Assert.IsNull(GitForgeClient.Parse(""));
        Assert.IsNull(GitForgeClient.Parse(null));
    }

    // ── JSON → records ────────────────────────────────────────────────────────

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
    public void MapPullRequests_GitHub_ReadsHeadAndBaseBranches()
    {
        var prs = GitForgeClient.MapPullRequests(GitForgeKind.GitHub, JsonNode.Parse(GitHubPrJson)!);

        Assert.AreEqual(1, prs.Count);
        Assert.AreEqual(153, prs[0].Number);
        Assert.AreEqual("Git AI tools", prs[0].Title);
        Assert.AreEqual("claude/git-ai-tier1", prs[0].SourceBranch);
        Assert.AreEqual("main", prs[0].TargetBranch);
        Assert.AreEqual("Jones-Adam", prs[0].Author);
        Assert.IsNotNull(prs[0].Updated);
    }

    [TestMethod]
    [CoversNode("git-ai-act-forge-prs")]
    public void MapPullRequests_Bitbucket_ReadsItsNestedBranchShape()
    {
        var prs = GitForgeClient.MapPullRequests(GitForgeKind.Bitbucket, JsonNode.Parse(BitbucketPrJson)!);

        Assert.AreEqual(1, prs.Count);
        Assert.AreEqual(7, prs[0].Number);
        Assert.AreEqual("feature/codec", prs[0].SourceBranch, "source.branch.name, not head.ref");
        Assert.AreEqual("develop", prs[0].TargetBranch);
        Assert.AreEqual("Sam", prs[0].Author);
    }

    [TestMethod]
    [CoversNode("git-ai-act-forge-issues")]
    public void MapIssues_GitHub_DropsPullRequestsFromTheIssuesEndpoint()
    {
        // GitHub's /issues returns PRs too — they carry a pull_request member and must not be listed twice.
        const string json = """
            [
              { "number": 10, "title": "A real issue", "state": "open", "user": { "login": "ann" } },
              { "number": 11, "title": "Actually a PR", "state": "open", "user": { "login": "bob" },
                "pull_request": { "url": "https://api.github.com/repos/x/y/pulls/11" } }
            ]
            """;

        var issues = GitForgeClient.MapIssues(GitForgeKind.GitHub, JsonNode.Parse(json)!);

        Assert.AreEqual(1, issues.Count);
        Assert.AreEqual("A real issue", issues[0].Title);
    }

    [TestMethod]
    [CoversNode("git-ai-act-forge")]
    public void MalformedPayload_YieldsNothingRatherThanThrowing()
    {
        // Empty and wrong-shaped payloads are normal (a 200 with no results, a page with no "values").
        Assert.AreEqual(0, GitForgeClient.MapPullRequests(GitForgeKind.GitHub, JsonNode.Parse("[]")!).Count);
        Assert.AreEqual(0, GitForgeClient.MapPullRequests(GitForgeKind.Bitbucket, JsonNode.Parse("{}")!).Count);
        Assert.AreEqual(0, GitForgeClient.MapPullRequests(GitForgeKind.GitHub, JsonNode.Parse("{}")!).Count);

        // An entry missing every field maps to defaults rather than throwing — one bad row must not lose the page.
        var sparse = GitForgeClient.MapIssues(GitForgeKind.GitHub, JsonNode.Parse("[{}]")!);
        Assert.AreEqual(1, sparse.Count);
        Assert.AreEqual(0, sparse[0].Number);
        Assert.AreEqual(string.Empty, sparse[0].Title);
    }

    // ── Request shaping + transport ───────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-forge-prs")]
    public async Task GetPullRequestsAsync_SendsABearerTokenToGitHub_AndParsesTheResult()
    {
        HttpRequestMessage? seen = null;
        var client = new GitForgeClient(
            credentials: _ => new GitCredential("x-token-auth", "SECRET"),
            send: (req, _) =>
            {
                seen = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(GitHubPrJson)
                });
            });

        var repo = GitForgeClient.Parse("https://github.com/smile-forge/nexaflow.git")!;
        var prs  = await client.GetPullRequestsAsync(repo, "https://github.com/smile-forge/nexaflow.git",
                                                     "open", 30, CancellationToken.None);

        Assert.AreEqual(1, prs.Count);
        Assert.AreEqual("Bearer", seen!.Headers.Authorization!.Scheme);
        Assert.AreEqual("SECRET", seen.Headers.Authorization.Parameter);
        StringAssert.Contains(seen.RequestUri!.ToString(), "/repos/smile-forge/nexaflow/pulls");
    }

    [TestMethod]
    [CoversNode("git-ai-act-forge")]
    public async Task AFailedResponse_YieldsAnEmptyList_NotAnException()
    {
        var client = new GitForgeClient(
            credentials: _ => null,   // public repo / no stored credential
            send: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var repo = GitForgeClient.Parse("https://github.com/x/y")!;
        var prs  = await client.GetPullRequestsAsync(repo, "https://github.com/x/y", "open", 5, CancellationToken.None);

        Assert.AreEqual(0, prs.Count);
    }

    [TestMethod]
    [CoversNode("git-ai-act-forge")]
    public async Task NoStoredCredential_StillSendsTheRequest_Unauthenticated()
    {
        HttpRequestMessage? seen = null;
        var client = new GitForgeClient(
            credentials: _ => null,
            send: (req, _) =>
            {
                seen = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                });
            });

        var repo = GitForgeClient.Parse("https://github.com/x/y")!;
        await client.GetPullRequestsAsync(repo, "https://github.com/x/y", "open", 5, CancellationToken.None);

        Assert.IsNull(seen!.Headers.Authorization, "a public repository must not require a credential");
    }
}
