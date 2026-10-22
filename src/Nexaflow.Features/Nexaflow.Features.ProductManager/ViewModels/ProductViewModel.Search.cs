using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.ProductManager.ViewModels;

/// <summary>
/// The Product tab as an <see cref="ISearchable"/> page: "?" searches the whole knowledge graph — node
/// names and the source behind them — and opens the results as their own tab.
/// <para>
/// A new tab rather than a filter on the sunburst, because the sunburst draws the <em>product tree</em> and
/// the graph is that tree crossed with the whole repo. A result may be a feature, a type, a file or a line
/// of code, and three of those have nowhere to sit on a sunburst — filtering it to "the features that
/// matched" would quietly answer a much smaller question than the one asked. This is the same shape the
/// file browser uses: search here, results open where results belong.
/// </para>
/// </summary>
public partial class ProductViewModel : ISearchable
{
    public string SearchTargetDescription =>
        $"the knowledge graph of this product — node names (features, types, members, files) and the "
      + $"source behind them";

    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (request.HasNameOnlyTerms)
            return Task.FromResult(SearchOutcome.Unsupported(Str.Get("ProductManager.Search.Outcome.NameFilters")));

        var term = request.Text.Trim();
        if (request.Terms.Count == 0 || term.Length == 0)
            return Task.FromResult(SearchOutcome.Unsupported(Str.Get("ProductManager.Search.Outcome.Empty")));

        if (Services.GraphTextSearch.TryCompile(request) is not { } regex)
            return Task.FromResult(SearchOutcome.Unsupported(Str.Format("ProductManager.Search.Outcome.InvalidRegexFormat", request.Text)));

        if (display)
        {
            _shell.OpenTab(ProductSearchTabRegistration.StaticPageKind, new Dictionary<string, string>
            {
                ["path"]  = ProductRoot,
                ["query"] = SearchSyntax.Format(request),
            });
        }

        return Task.FromResult(NameHits(request, regex));
    }

    /// <summary>
    /// The node-name half of the search, answered here so the agent gets something back without a page
    /// having to exist yet.
    /// <para>
    /// Names only, and the outcome says so: the source grep is the expensive half and belongs on the results
    /// page, where it runs off the UI thread with a cap. Reporting a name-only count as though it were the
    /// whole answer is what <see cref="SearchOutcome.Narrowed"/> is for.
    /// </para>
    /// </summary>
    private SearchOutcome NameHits(SearchRequest request, System.Text.RegularExpressions.Regex regex)
    {
        var graph = new ProductStore(ProductRoot).LoadGraph();
        if (graph is null)
            return SearchOutcome.None(Str.Get("ProductManager.Search.Outcome.NoGraph"));

        var hits = Services.GraphTextSearch.Names(graph, request, regex).Take(SearchHitCap)
            .Select(n => new SearchHit(n.Id, $"[{n.Type}] {n.Label ?? n.Id}", n.Id))
            .ToList();

        var term = request.Text.Trim();
        return hits.Count == 0
            ? SearchOutcome.None(Str.Format("ProductManager.Search.Outcome.NoNameFormat", term))
            : SearchOutcome.Narrowed(hits, Str.Format("ProductManager.Search.Outcome.NamesFormat", hits.Count, term));
    }

    private const int SearchHitCap = 200;
}
