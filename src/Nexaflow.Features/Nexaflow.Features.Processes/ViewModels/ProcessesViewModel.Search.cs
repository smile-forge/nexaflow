using Nexaflow.Features.Common.Search;
using Nexaflow.Search;

namespace Nexaflow.Features.Processes.ViewModels;

/// <summary>
/// The process list as an <see cref="ISearchable"/> page. "?" is the search box the toolbar already has —
/// same five fields (name, PID, company, description, path), same flat-match-list presentation — but run
/// through the shared query parser, so a regex or a quoted phrase means here exactly what it means in a
/// text tab. The box shows the query it was given, so a search the AI ran is visible and dismissible
/// rather than an unexplained filter.
/// <para>
/// Deliberately not a separate highlight layer: the box IS this page's search, and a second one that
/// filtered differently would leave two answers to "what am I looking at" on screen at once.
/// </para>
/// <para>
/// Every method here marshals through <see cref="IShellServices.RunOnUiAsync{T}"/>, including the
/// non-displaying read: <c>_rowsByPid</c> is rebuilt by the 1s refresh tick on the UI thread, and the
/// agent calls in from its own.
/// </para>
/// </summary>
public sealed partial class ProcessesViewModel : ISearchable
{
    // Set when the filter came from a "?" query or the agent, rather than the toolbar box.
    private SearchRequest? _filterRequest;
    private HashSet<int>?  _pinnedPids;
    private bool _suppressFilterReset;

    public string SearchTargetDescription =>
        "the running processes on this machine, by name, PID, company, description or image path";

    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        // A process image has a path, but a filename glob is a file-browser constraint: applying it to the
        // image path would answer "which processes run from a *.exe" — a question this list doesn't ask.
        if (request.HasNameOnlyTerms)
            return Task.FromResult(SearchOutcome.Unsupported(
                "Filename filters don't apply to the process list — search by name, PID, company or path."));

        if (!request.TryValidate(out var invalid))
            return Task.FromResult(SearchOutcome.Unsupported($"Invalid regular expression: {invalid}"));

        return _shell.RunOnUiAsync(() => Task.FromResult(RunSearch(request, display)));
    }

    private SearchOutcome RunSearch(SearchRequest request, bool display)
    {
        var matches = _rowsByPid.Values.Where(r => SearchFields(r).Any(request.Matches))
                                       .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                                       .ThenBy(r => r.Pid)
                                       .ToList();

        if (display) ApplyFilter(request, pinned: null, SearchSyntax.Format(request));

        return matches.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(HitsFor(matches));
    }

    /// <summary>Pins the list to exactly the processes the agent chose, by PID.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var pids = hits.Select(h => int.TryParse(h.Id, out var pid) ? pid : -1)
                       .Where(pid => pid >= 0)
                       .ToHashSet();
        if (pids.Count == 0) return Task.FromResult(false);

        return _shell.RunOnUiAsync(() =>
        {
            ApplyFilter(request: null, pinned: pids, $"{pids.Count} selected");
            return Task.FromResult(true);
        });
    }

    /// <summary>Installs a filter without letting <see cref="OnFilterTextChanged"/> tear it straight back
    /// down — the box's text and the rule behind it are set together, not one triggering the other.</summary>
    private void ApplyFilter(SearchRequest? request, HashSet<int>? pinned, string boxText)
    {
        _suppressFilterReset = true;
        FilterText     = boxText;
        _filterRequest = request;
        _pinnedPids    = pinned;
        _suppressFilterReset = false;
        Resort();
    }

    private static IReadOnlyList<SearchHit> HitsFor(IEnumerable<ProcessRowViewModel> rows) =>
        rows.Select(r => new SearchHit(
                r.Pid.ToString(),
                $"{r.Name} ({r.Pid})",
                string.Join(" — ", new[] { r.Description, r.Company, r.Path }.Where(s => s.Length > 0))))
            .ToList();
}
