namespace Nexaflow.Core.Models;

/// <summary>
/// Carries the (PageKind, PageParams) tuple for shell-level page-open commands —
/// e.g. follow-links inside breadcrumbs, or any other component that wants to
/// route an open through the shell's <c>OpenPageCommand</c>.
/// </summary>
public sealed record OpenPageRequest(string PageKind, Dictionary<string, string>? PageParams = null);
