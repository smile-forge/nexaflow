namespace Nexaflow.Features.Common;

/// <summary>
/// Describes one parameter a page kind accepts in its <c>pageParams</c> dictionary. Advertised by
/// <see cref="IPageRegistration.Parameters"/> so the shell can tell the AI (and other callers) which
/// pages are openable and what each one needs.
/// </summary>
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
