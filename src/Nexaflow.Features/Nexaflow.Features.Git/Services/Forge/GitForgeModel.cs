using System.Text.Json.Nodes;

namespace Nexaflow.Features.Git.Services.Forge;

/// <summary>
/// A repository as its forge names it. <see cref="Owner"/>/<see cref="Name"/> covers GitHub and Bitbucket;
/// <see cref="Project"/> exists for forges that put a third level in between — Azure DevOps addresses a repo
/// as <c>org/project/_git/repo</c>, so a two-part identity could not express it.
/// </summary>
/// <param name="ProviderId">Which provider owns this repo (see <see cref="IGitForgeProvider.Id"/>).</param>
public sealed record GitForgeRepo(
    string  ProviderId,
    string  Host,
    string  Owner,
    string  Name,
    string? Project = null)
{
    /// <summary>Human-readable identity, for messages. URL building belongs to the provider, not here.</summary>
    public string Slug => Project is null ? $"{Owner}/{Name}" : $"{Owner}/{Project}/{Name}";
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
/// Null-tolerant readers for forge JSON. Every forge payload is someone else's schema that can change without
/// notice, so a missing or mistyped field yields a default rather than throwing — one odd row must never cost
/// the whole page.
/// </summary>
public static class ForgeJson
{
    public static string? Str(JsonObject? o, string key)
    {
        try { return o?[key]?.GetValue<string>(); }
        catch (Exception) { return null; }   // present but not a string
    }

    public static int Int(JsonObject? o, string key) =>
        o?[key] is { } n && int.TryParse(n.ToString(), out var i) ? i : 0;

    public static DateTimeOffset? Date(JsonObject? o, string key) =>
        DateTimeOffset.TryParse(o?[key]?.ToString(), out var d) ? d : null;

    /// <summary>A nested object, or null — saves a cast at every call site.</summary>
    public static JsonObject? Obj(JsonObject? o, string key) => o?[key] as JsonObject;
}
