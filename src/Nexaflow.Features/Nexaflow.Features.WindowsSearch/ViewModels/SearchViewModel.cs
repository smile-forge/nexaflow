using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Search;
using Nexaflow.IO.Common;
using Nexaflow.Search;
using Nexaflow.Features.WindowsSearch.Services;
using System.Collections.ObjectModel;
using System.Data.OleDb;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Nexaflow.Features.WindowsSearch.ViewModels;

public sealed partial class SearchViewModel : ObservableObject, IPageViewModel, ISearchable
{
    [ObservableProperty] private string             _searchQuery  = string.Empty;
    [ObservableProperty] private string             _searchRoot   = string.Empty;
    [ObservableProperty] private bool               _isSearching;
    [ObservableProperty] private string             _statusText   = string.Empty;
    [ObservableProperty] private int                _resultCount;
    [ObservableProperty] private SearchResultEntry? _selectedEntry;
    [ObservableProperty] private bool               _hasSelection;

    partial void OnSelectedEntryChanged(SearchResultEntry? value)
    {
        HasSelection = value is not null;
        OpenLocationCommand.NotifyCanExecuteChanged();
        OpenFileCommand.NotifyCanExecuteChanged();
    }

    public ObservableCollection<SearchResultEntry> Results { get; } = [];

    /// <summary>True when this tab has somewhere to search — a root, or the This-PC drive set (which
    /// leaves <see cref="SearchRoot"/> empty). Mirrors the gate in <see cref="RunSearchAsync"/> so callers
    /// (the refine handler) don't mistake a cross-drive search for an unscoped tab.</summary>
    public bool HasSearchScope => !string.IsNullOrEmpty(SearchRoot) || _drives.Count > 0;

    /// <summary>Set by <see cref="SearchTabRegistration"/> so this VM can keep tab meta in sync.</summary>
    public Page? Tab { get; set; }

    private readonly IShellServices      _shellServices;
    private readonly IReadOnlyList<string> _drives;
    private string _baseQuery  = string.Empty;
    private ParsedQuery? _lastParsed;
    private CancellationTokenSource? _cts;

    // Set while the active query is a regex. AQS can only be widened to cover a pattern, so the rows it
    // returns are re-filtered through the real regex before the user ever sees them.
    private SearchRequest? _postFilter;

    // The query as parsed, kept structured. SearchQuery is only its rendering — deriving state from that
    // string instead means a re-run re-parses the index SEED, not what the user asked for.
    private SearchRequest? _activeRequest;

    // How the last result set was obtained, and how much of it came back — the banner leads with this so a
    // count is never presented without saying what produced it.
    private SearchOrigin _lastOrigin   = SearchOrigin.Index;
    private int          _lastReturned;
    private bool         _lastTruncated;

    /// <summary>Rows shown for a query the index answered exactly.</summary>
    private const int DefaultResultCap = 500;

    /// <summary>Rows pulled when a post-filter will discard most of them. The cap has to sit past the
    /// filtering, not before it, or matches are lost to truncation rather than to the query.</summary>
    private const int CandidateFetchCap = 5000;

    private string OriginPrefix =>
        VerificationPlanner.OriginPrefix(_lastOrigin, _lastReturned, _lastTruncated);

    public SearchViewModel(string query, string root, IReadOnlyList<string> drives, IShellServices shellServices)
    {
        _searchQuery   = query;
        _searchRoot    = root;
        _drives        = drives;
        _shellServices = shellServices;
    }

    [RelayCommand]
    private async Task RunSearch(CancellationToken ct) => await RunSearchAsync(ct);

    public async Task RunSearchAsync(CancellationToken externalCt)
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) ||
            (string.IsNullOrEmpty(SearchRoot) && _drives.Count == 0))
        {
            StatusText = "Enter a search term.";
            return;
        }

        // The query may arrive in the AI bar's syntax — "/pattern/" from a browser handoff, say — so it is
        // parsed the same way here as it would be if typed, rather than taken as literal AQS.
        var request = SearchSyntax.ParseRequest(SearchQuery, TermRecognizers);
        if (!request.TryValidate(out var invalid))
        {
            ShowQueryProblem($"Invalid pattern: {invalid}");
            return;
        }

        // Anything beyond a single plain term needs the post-filter to apply the real query afterwards;
        // the index only ever narrows.
        _activeRequest = request;
        _postFilter    = NeedsPostFilter(request) ? request : null;

        var parsed = SearchQueryParser.FromTerms(request.Terms, _aqs);
        if (parsed is null)
        {
            // Nothing to ask the index. Saying so beats an empty list the user reads as "no such files".
            ShowQueryProblem(UnseedableNote(request.Text));
            return;
        }

        _baseQuery  = parsed.RawInput;
        _lastParsed = parsed;
        await ExecuteSearch(_lastParsed, externalCt);
    }

    /// <summary>True when the index query is only a superset — a regex, a glob, or several terms — so the
    /// rows it returns still have to be judged against the real query.</summary>
    private static bool NeedsPostFilter(SearchRequest request) =>
        request.Terms.Count > 1 ||
        request.Terms.Any(t => t.Kind == SearchTermKind.Regex);

    private void ShowQueryProblem(string message)
    {
        Results.Clear();
        ResultCount        = 0;
        StatusText         = "Nothing searched.";
        VerificationPhase  = VerifyPhase.Done;
        VerificationBanner = message;
    }

    /// <summary>
    /// The index term seeding <paramref name="request"/>'s pattern — the longest literal every match must
    /// contain, searched as an ordinary term so file CONTENTS are covered too. Null when the pattern has no
    /// such literal, which the caller reports rather than dragging back the whole corpus.
    /// </summary>
    private static string? IndexQueryFor(SearchRequest request)   // single-pattern helper, kept for the corpus path
        => AqsRegexTranslator.ToIndexQuery(request.Text);

    /// <summary>Message for a pattern with nothing to seed the index on.</summary>
    private static string UnseedableNote(string pattern) =>
        $"/{pattern}/ has no literal text to search on, so the index can't narrow it. " +
        "Add some literal characters to the pattern, or search a smaller folder.";

    /// <summary>Re-runs the last query (including any merged refinements) without re-parsing.</summary>
    public async Task RefreshAsync()
    {
        if (_lastParsed is not null)
            await ExecuteSearch(_lastParsed, CancellationToken.None);
        else
            await RunSearchAsync(CancellationToken.None);
    }

    /// <summary>
    /// Merges <paramref name="refinement"/> with the original query using AND and
    /// re-queries Windows Search. Does not filter client-side.
    /// </summary>
    public async Task MergeAndSearchAsync(string refinement)
        => await MergeAndSearchAsync(SearchSyntax.ParseRequest(refinement, TermRecognizers));

    /// <summary>
    /// Merges a refinement into the running query. The combined TERMS are kept, not just the merged index
    /// clause: <see cref="SearchQuery"/> holds the query as the user would type it, so re-running the tab
    /// re-parses back to the same request. Round-tripping through the index seed instead would quietly
    /// lose the regex — and with it the post-filter, leaving every row marked proven.
    /// </summary>
    public async Task MergeAndSearchAsync(SearchRequest refinement)
    {
        var combined = _activeRequest is { Terms.Count: > 0 } previous
            ? previous with { Terms = [.. previous.Terms, .. refinement.Terms] }
            : refinement;

        // Built from the combined TERMS, not by re-parsing the base query's text. The rendered text is
        // lossy on exactly the terms that need the index most: legacy parsing reads "kind:document" or
        // "/ma(ths)/" as literal characters to look for, narrowing the query to the files that contain
        // that punctuation — i.e. none. Same reason the post-filter is kept structured above.
        var merged = SearchQueryParser.FromTerms(combined.Terms, _aqs)
                     ?? SearchQueryParser.Parse(_baseQuery);

        _activeRequest = combined;
        _postFilter    = NeedsPostFilter(combined) ? combined : null;
        SearchQuery    = SearchSyntax.Format(combined);
        _lastParsed    = merged;
        await ExecuteSearch(merged, CancellationToken.None);

        if (Tab is not null)
        {
            var q  = SearchQuery;
            var r  = SearchRoot;
            var qs = q.Length > 12 ? q[..12] + "…" : q;
            var rl = string.IsNullOrEmpty(r)
                ? (_drives.Count > 0 ? "This PC" : "Search")
                : Path.GetFileName(r.TrimEnd('\\', '/'));
            Tab.Title = qs;
            Tab.PageParams = new() { ["query"] = q, ["root"] = r, ["drives"] = string.Join(";", _drives) };
            Tab.Breadcrumbs.Clear();
            Tab.Breadcrumbs.Add(new BreadcrumbSegment { Label = rl });
            Tab.Breadcrumbs.Add(new BreadcrumbSegment { Label = $"Query : {qs}" });
        }
    }

    // ── Content verification ──────────────────────────────────────────────────
    //
    // A regex over a file corpus is answered in two stages, because AQS can narrow by name but cannot
    // evaluate a pattern against file CONTENTS. Stage one is the index: rows appear immediately, with the
    // ones whose name matches already proven and the rest marked as candidates. Stage two reads those
    // candidates in the background and settles each in place.

    [ObservableProperty] private VerifyPhase _verificationPhase = VerifyPhase.None;
    [ObservableProperty] private string      _verificationBanner = string.Empty;
    [ObservableProperty] private int         _verifiedCount;
    [ObservableProperty] private int         _pendingCount;

    private CancellationTokenSource? _verifyCts;

    private List<SearchResultEntry> Candidates =>
        Results.Where(r => r.State == SearchHitState.Candidate).ToList();

    // Counts are derived from the rows every time rather than incremented as the sweep reports. An
    // increment double-counts the moment a row is swept twice (which "check them" does by design), and a
    // count that drifts from the list is worse than no count.
    private void RefreshCounts()
    {
        VerifiedCount = Results.Count(r => r.State == SearchHitState.Verified);
        PendingCount  = Results.Count(r => r.State == SearchHitState.Candidate);
    }

    private int UnreadableCount => Results.Count(r => r.State == SearchHitState.Unreadable);
    private int UncertainCount  => Results.Count(r => r.State == SearchHitState.Uncertain);

    /// <summary>Sort key placing what we know above what we're unsure of.</summary>
    internal static int ConfidenceRank(SearchResultEntry e) => e.State switch
    {
        SearchHitState.Verified   => 0,   // proven
        SearchHitState.Uncertain  => 1,   // found, but in bytes we couldn't decode properly
        SearchHitState.Candidate  => 2,   // not looked at yet
        SearchHitState.Unreadable => 3,   // looked at, couldn't tell
        _                         => 4,   // rejected — on its way out
    };

    // Re-sorts in place after a sweep so settled rows rise and unresolved ones sink, without rebuilding the
    // collection (which would drop the user's selection and scroll position).
    private void ResortByConfidence()
    {
        var ordered = Results.OrderBy(ConfidenceRank).ToList();
        for (var target = 0; target < ordered.Count; target++)
        {
            var current = Results.IndexOf(ordered[target]);
            if (current != target) Results.Move(current, target);
        }
    }

    // Decides whether to sweep now, ask first, or do nothing at all.
    private void BeginVerification()
    {
        _verifyCts?.Cancel();
        _verifyCts = null;

        if (_postFilter is null)
        {
            VerificationPhase = VerifyPhase.None;
            return;
        }

        var candidates = Candidates;
        RefreshCounts();

        var plan = VerificationPlanner.ForNewResults(VerifiedCount, candidates.Count, OriginPrefix);
        VerificationPhase  = plan.Phase;
        VerificationBanner = plan.Banner;

        if (plan.SweepNow > 0)
            StartSweep(candidates.Take(plan.SweepNow).ToList());
    }

    /// <summary>Checks the candidates the user was asked about.</summary>
    [RelayCommand]
    private void VerifyRemaining() => StartSweep(Candidates);

    /// <summary>Leaves the remaining candidates as "might match" rather than reading them.</summary>
    [RelayCommand]
    private void SkipVerification()
    {
        _verifyCts?.Cancel();
        var plan = VerificationPlanner.AfterSkip(VerifiedCount, Candidates.Count);
        VerificationPhase  = plan.Phase;
        VerificationBanner = plan.Banner;
    }

    private void StartSweep(List<SearchResultEntry> candidates)
    {
        if (candidates.Count == 0) return;

        _verifyCts?.Cancel();
        var cts = _verifyCts = new CancellationTokenSource();
        var request = _postFilter;
        if (request is null) return;

        VerificationPhase  = VerifyPhase.Running;
        VerificationBanner = $"Possible matches found — verifying {candidates.Count}…";

        var verifier = new SearchVerifier(DiscoverExtractors(_shellServices));
        var hits     = candidates.ToDictionary(c => c.FilePath, StringComparer.OrdinalIgnoreCase);

        // Fire-and-forget: the sweep reports each row as it lands, so nothing waits on the whole pass.
        _ = Task.Run(async () =>
        {
            try
            {
                await verifier.VerifyAllAsync(
                    candidates.Select(c => new SearchHit(c.FilePath, c.FileName) { Source = c.FilePath }).ToList(),
                    request,
                    async (hit, state) =>
                    {
                        if (!hits.TryGetValue(hit.Id, out var row)) return;
                        // Row state is bound, so the flip has to happen on the UI thread.
                        await _shellServices.RunOnUiAsync(() =>
                        {
                            row.State = state;
                            RefreshCounts();
                        });
                    },
                    cts.Token);

                if (!cts.IsCancellationRequested)
                    await _shellServices.RunOnUiAsync(CompleteSweep);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"[WindowsSearch] verification failed: {ex.Message}"); }
        }, cts.Token);
    }

    // Rejected rows stay visible (struck through) while the sweep runs so the count settles where the user
    // can see it, then leave once it finishes — the list never reshuffles under an active cursor.
    private void CompleteSweep()
    {
        foreach (var rejected in Results.Where(r => r.State == SearchHitState.Rejected).ToList())
            Results.Remove(rejected);

        ResultCount = Results.Count;
        RefreshCounts();
        ResortByConfidence();

        // "Confirmed" means proven — NOT the row count, which still includes everything unsettled and
        // reads as a far stronger claim than it is.
        var plan = VerificationPlanner.AfterSweep(VerifiedCount, PendingCount, UnreadableCount, UncertainCount, OriginPrefix);
        VerificationPhase  = plan.Phase;
        VerificationBanner = plan.Banner;
    }

    // Format-aware extractors live in whichever feature owns the format; none is required, since the
    // verifier falls back to reading the file as text.
    private static IReadOnlyList<IFileTextExtractor> DiscoverExtractors(IShellServices shell) =>
        (shell.DiscoverImplementations<IFileTextExtractor>() ?? [])
        .Select(t => { try { return Activator.CreateInstance(t) as IFileTextExtractor; } catch { return null; } })
        .Where(e => e is not null)
        .Select(e => e!)
        .ToList();

    private async Task ExecuteSearch(ParsedQuery parsed, CancellationToken externalCt)
    {
        _cts?.Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct = _cts.Token;

        IsSearching = true;
        StatusText  = "Searching…";
        Results.Clear();
        ResultCount = 0;

        try
        {
            // When a post-filter is going to discard most rows, the index has to be asked for far more than
            // the user will see — otherwise the cap truncates BEFORE filtering and real matches simply fall
            // off the end. That is what made "/ma(ths)/" lose two files "/maths/" had found.
            var fetch = _postFilter is null ? DefaultResultCap : CandidateFetchCap;

            IReadOnlyList<SearchResultEntry> entries;
            if (_drives.Count > 0 && string.IsNullOrEmpty(SearchRoot))
            {
                // "This PC" is index-only — walking whole drives isn't practical.
                entries       = await WindowsSearchService.SearchAcrossAsync(parsed, _drives, ct, fetch);
                _lastOrigin   = SearchOrigin.Index;
            }
            else
            {
                var found   = await WindowsSearchService.SearchWithOriginAsync(parsed, SearchRoot, ct, fetch);
                entries     = found.Entries;
                _lastOrigin = found.Origin;
            }
            _lastReturned = entries.Count;
            _lastTruncated = entries.Count >= fetch;
            ct.ThrowIfCancellationRequested();

            // AQS was widened to cover the pattern. A name match settles a row outright; a row that only
            // survived the widening might still match on CONTENT, which no index query can decide — it is
            // shown straight away as a candidate and settled by the background pass below. Dropping those
            // rows is what previously made a content pattern look like an empty folder.
            if (_postFilter is { } rx)
            {
                foreach (var e in entries)
                {
                    e.State = SearchVerifier.ClassifyByName(new SearchHit(e.FilePath, e.FileName), rx);

                    // A folder has no contents to search — its name IS the whole answer, so it is never
                    // "might match": either the pattern matches the name or the folder doesn't match.
                    if (e.IsFolder && e.State == SearchHitState.Candidate)
                        e.State = SearchHitState.Rejected;
                }

                // Most-certain first, so what we know beats what we're guessing at. Within a confidence
                // band the index's own recency order is preserved.
                entries = entries.OrderBy(ConfidenceRank).ToList();
            }

            foreach (var e in entries) Results.Add(e);
            ResultCount = Results.Count;
            StatusText  = ResultCount == 0
                ? "No results."
                : $"{ResultCount} result{(ResultCount == 1 ? "" : "s")}";

            BeginVerification();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Search cancelled.";
        }
        catch (OleDbException ex)
        {
            StatusText  = "Windows Search service unavailable.";
            ResultCount = 0;
            Debug.WriteLine($"[WindowsSearch] OleDbException: {ex.Message}");
        }
        catch (Exception ex)
        {
            StatusText  = $"Search error: {ex.Message}";
            ResultCount = 0;
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenLocation()
    {
        if (SelectedEntry is null) return;
        var dir = Path.GetDirectoryName(SelectedEntry.FilePath);
        if (string.IsNullOrEmpty(dir)) return;
        _shellServices.OpenTab("FileSystem", new Dictionary<string, string>
        {
            ["mode"]  = "path",
            ["path"]  = dir,
            ["label"] = Path.GetFileName(dir.TrimEnd('\\', '/'))
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenFile()
    {
        if (SelectedEntry is null) return;
        try { Process.Start(new ProcessStartInfo(SelectedEntry.FilePath) { UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine($"[SearchView] Open file: {ex.Message}"); }
    }

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext()
    {
        // A This-PC search has an empty SearchRoot but searches every drive — keying "performed" off the
        // query (not the root) so a cross-drive search with results is no longer reported as "no search".
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return "Search tab: no search performed yet.";
        var scope = string.IsNullOrEmpty(SearchRoot)
            ? (_drives.Count > 0 ? "This PC (all drives)" : "no scope set")
            : $"'{SearchRoot}'";
        return $"Search tab: '{SearchQuery}' in {scope}. {ResultCount} result(s).";
    }

    /// <summary>The search scope — its root path (or a This-PC sentinel) — so two pinned Search tabs on
    /// different roots stay distinguishable (aspect-4 disambiguation).</summary>
    public string? GetSecurityContext() => string.IsNullOrEmpty(SearchRoot) ? "search:this-pc" : SearchRoot;

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new DelegateClientTool(
            "search",
            "Search files using Windows Search. Supports plain terms (budget report), " +
            "quoted phrases (\"annual report\"), file globs (*.xml, *.doc*, name.*), " +
            "property filters (kind:document, size:>1mb, author:john, date:>2024-01, " +
            "modified:2023, before:2024, after:2023-06), and boolean prefix syntax " +
            "(+required -excluded).",
            [new ClientToolParameter("query", "The search query to run.")],
            ToolSafety.SafeOperation,
            (arguments, ct) =>
            {
                var query = arguments["query"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(query))
                    return Task.FromResult(ToolResult.Error("No query provided."));

                SearchQuery = query;
                _ = RunSearchAsync(CancellationToken.None);
                return Task.FromResult(ToolResult.Ok($"searching for {query}", $"Started search for '{query}'."));
            })
    ];

    // ── ISearchable ───────────────────────────────────────────────────────────

    /// <summary>The AQS parser this page's queries are built against — shared by the recogniser that
    /// spots a property constraint and the parser that turns it into SQL, so both agree on what the
    /// index will actually enforce.</summary>
    private readonly AqsTranslator _aqs = new();

    /// <summary>This page searches files, so filename globs and index property constraints both mean
    /// something here.</summary>
    public IReadOnlyList<ISearchTermRecognizer> TermRecognizers =>
        [new GlobTermRecognizer(), new AqsTermRecognizer(_aqs)];

    public string SearchTargetDescription =>
        $"files matching the current search '{SearchQuery}'" +
        (string.IsNullOrEmpty(SearchRoot) ? " across this PC" : $" under '{SearchRoot}'");

    /// <summary>Globs and property filters are strong evidence of a search; prose is not. This is the one
    /// page whose backend can judge that properly, so it overrides the term-count default.</summary>
    public float ScoreQuery(string input) => IsSearching ? 0f : SearchQueryScorer.Score(input);

    public async Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (!request.TryValidate(out var invalid))
            return SearchOutcome.Unsupported($"Invalid pattern: {invalid}");

        if (!HasSearchScope)
            return SearchOutcome.Unsupported("This search tab has no scope to search.");

        // A refinement narrows on its terms' seeds, then exactly via the post-filter.
        var refined = SearchQueryParser.FromTerms(request.Terms, _aqs);
        if (refined is null)
            return SearchOutcome.Unsupported(UnseedableNote(request.Text));

        var merged = SearchQueryParser.Merge(SearchQueryParser.Parse(_baseQuery), refined);

        if (!display)
        {
            // Agent-side read: query the index directly, leaving the visible result list alone. An index
            // that errors or isn't running is "no matches" for the caller — the same treatment the visible
            // path gives it — not an exception escaping into the agent loop.
            IReadOnlyList<SearchResultEntry> entries;
            try
            {
                entries = _drives.Count > 0 && string.IsNullOrEmpty(SearchRoot)
                    ? await WindowsSearchService.SearchAcrossAsync(merged, _drives, ct)
                    : await WindowsSearchService.SearchAsync(merged, SearchRoot, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch { entries = []; }

            if (!NeedsPostFilter(request))
                return entries.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(HitsFor(entries));

            // The agent asked for data, so give it settled data: verify inline rather than handing back
            // "maybe" rows it has no way to resolve. The user-facing path is the one that shows candidates
            // first and settles them in the background.
            var verifier = new SearchVerifier(DiscoverExtractors(_shellServices));
            var settled  = new List<SearchHit>();
            foreach (var hit in HitsFor(entries))
            {
                if (await verifier.VerifyAsync(hit, request, ct) is SearchHitState.Verified)
                    settled.Add(hit with { State = SearchHitState.Verified });
            }
            return settled.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(settled);
        }

        // Hand over the parsed request, not its index seed — the merge keeps the terms so the post-filter
        // still has the real query afterwards.
        await MergeAndSearchAsync(request);

        // The page shows its own results and its own banner, so nothing goes back to the chat — a non-null
        // message here pops the AI response overlay open on top of the search the user just ran.
        return ResultCount == 0
            ? SearchOutcome.None()
            : SearchOutcome.Found(HitsFor(Results), ResultCount);
    }

    /// <summary>Narrows the visible rows to the agent's chosen files, keyed by full path.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var keep = hits.Select(h => h.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var drop = Results.Where(r => !keep.Contains(r.FilePath)).ToList();
        if (drop.Count == Results.Count) return Task.FromResult(false);

        foreach (var r in drop) Results.Remove(r);
        ResultCount = Results.Count;
        StatusText  = $"{ResultCount} of {ResultCount + drop.Count} result(s) shown.";
        return Task.FromResult(true);
    }

    private static IReadOnlyList<SearchHit> HitsFor(IEnumerable<SearchResultEntry> entries) =>
        entries.Select(e => new SearchHit(e.FilePath, e.FileName, e.Directory)).ToList();

    public IContext? GetContextObject()
    {
        if (SelectedEntry is not { } entry) return null;

        if (entry.IsFolder)
            return new FileSystemContext
            {
                RootPath      = entry.FilePath,
                CurrentPath   = entry.FilePath,
                SelectedItems = []
            };

        var dir = Path.GetDirectoryName(entry.FilePath);
        if (string.IsNullOrEmpty(dir)) return null;

        return new FileSystemContext
        {
            RootPath      = dir,
            CurrentPath   = dir,
            SelectedItems = [entry.FilePath]
        };
    }
}
