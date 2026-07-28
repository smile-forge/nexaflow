using Nexaflow.Search;

namespace Nexaflow.Features.Common.Search;

/// <summary>
/// "Show the user a file search over this scope." A page raises one through
/// <see cref="IShellServices.HandleObject"/> and whichever feature owns file search claims it and presents
/// the results — the raiser never learns which feature that is, exactly as the scratchpad hands off a
/// clicked URL without knowing the web feature exists.
/// </summary>
/// <param name="Query">The search itself — literal or regex, already parsed.</param>
/// <param name="Root">Folder tree to search. Empty means use <paramref name="Drives"/> instead.</param>
/// <param name="Drives">Drive roots to search when <paramref name="Root"/> is empty (a This-PC search).</param>
public sealed record FileSearchRequest(SearchRequest Query, string Root, IReadOnlyList<string> Drives)
{
    /// <summary>True when there is somewhere to search — no scope means nothing to hand off.</summary>
    public bool HasScope => !string.IsNullOrEmpty(Root) || Drives.Count > 0;
}
