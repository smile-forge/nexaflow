namespace Nexaflow.Core.Models;

/// <summary>
/// One openable page offered in the ribbon editor's "add page" dropdown — a registration's
/// <c>CanBeContextItem</c> page or one of its <c>GetTopLevelPages</c> variants (e.g. a named Windows
/// folder). Carries just what the dropdown shows and what a new <see cref="RibbonItem"/> needs,
/// including the <see cref="PageParams"/> that distinguish variants of the same page kind.
/// </summary>
public sealed record RibbonCatalogEntry(
    string Title, string Icon, string PageKind, Dictionary<string, string>? PageParams = null);
