namespace Nexaflow.Core.Models;

/// <summary>
/// One openable page kind offered in the ribbon editor's "add page" dropdown — the subset of
/// <see cref="Features.Common.IPageRegistration"/>s whose <c>CanBeContextItem</c> is true (pages that
/// open standalone with no required params). Carries just what the dropdown shows and what a new
/// <see cref="RibbonItem"/> needs.
/// </summary>
public sealed record RibbonCatalogEntry(string PageKind, string Title, string Icon);
