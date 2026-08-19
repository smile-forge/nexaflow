namespace Nexaflow.Features.Common;

/// <summary>
/// Describes one parameter a page kind accepts in its <c>pageParams</c> dictionary. Advertised by
/// <see cref="IPageRegistration.Parameters"/> so the shell can tell the AI (and other callers) which
/// pages are openable and what each one needs.
/// </summary>
/// <param name="Name">The key as it appears in the <c>pageParams</c> dictionary.</param>
/// <param name="Description">
/// What the value means, written for a caller who cannot see the page — the AI reads this to decide what
/// to pass, so it should say the shape expected (a full path, a 1-based page number, pipe-separated paths)
/// rather than restating the name.
/// </param>
/// <param name="Required">
/// Whether the page can be opened without it. An optional parameter is one the page has a sensible answer
/// for when it is absent, not one it merely tolerates.
/// </param>
/// <param name="Identity">
/// Whether this parameter says <em>which document</em> the tab is, rather than <em>where in it</em>
/// you are looking. The shell dedups open tabs on identity parameters only: a request that differs
/// solely in its non-identity parameters — a byte offset, a selected node — re-points the open tab
/// through <see cref="IPageView.Reinitialize"/> instead of opening a second tab on the same document.
/// Identity by default, so a page that says nothing keeps the exact-match behaviour. A parameter the
/// registration never declares is treated as identity too.
/// </param>
public sealed record PageParameter(string Name, string Description, bool Required = true,
                                   bool Identity = true);
