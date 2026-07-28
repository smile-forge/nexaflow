using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;

namespace Nexaflow.Core.Services;

/// <summary>
/// The shell's one search route: <c>?</c> searches whatever the active page is, provided that page
/// implements <see cref="ISearchable"/>. Replaces the per-feature search handlers — a page declares the
/// capability and gets AI-bar search, the status dot, and the agent's search tools for free.
/// <para>
/// Regex is a <em>mode of</em> search rather than a separate handler: <c>?/pattern/</c> (see
/// <see cref="SearchSyntax"/>). Making it explicit is the point — inferring "this looks regex-y" from
/// stray punctuation is what made <c>config.json cache</c> ambiguous.
/// </para>
/// </summary>
public sealed class SearchQueryHandler : IQueryHandler
{
    public string? Symbol => "?";

    public string Description =>
        "Searches the current page — its file, list, or results. Plain text matches literally; " +
        "/pattern/ runs a regular expression (add the c flag for case sensitivity).";

    public float CanProcess(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm is not ISearchable page) return 0f;
        if (SearchSyntax.ParseRequest(input, page.TermRecognizers).Terms.Count == 0) return 0f;

        // An explicit "?" is the user forcing a search — never second-guess it.
        if (prefixed) return 1f;

        // Bare prose belongs to the agent, which can still search via this page's tools.
        if (SearchScoring.LooksLikeProse(input)) return 0f;

        return Math.Clamp(page.ScoreQuery(input), 0f, 1f);
    }

    public async Task<string?> ProcessAsync(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm is not ISearchable page)
            return "This page doesn't support searching.";

        // Parsed with the page's own recognisers, so it only ever receives term kinds it can honour.
        var request = SearchSyntax.ParseRequest(input, page.TermRecognizers);
        if (request.Terms.Count == 0) return "Nothing to search for.";

        var outcome = await page.SearchAsync(request, display: true, CancellationToken.None);

        // A page that renders its own results says nothing here; only a failure or an explicit
        // message reaches the chat.
        return outcome.Message;
    }
}
