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

public sealed partial class SearchViewModel : ObservableObject, IPageViewModel, ISearchable, IDisposable
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
    public Page? Tab
    {
        get;
        set { field = value; SyncTab(); }
    }

    /// <summary>Query characters kept in the tab label before it is elided.</summary>
    public const int TabQueryChars = 14;

    /// <summary>
    /// The tab label for a query — as much of it as fits, elided. No magnifier: the tab strip already
    /// renders <see cref="Page.Icon"/>, so putting one here shows two.
    /// </summary>
    public static string TabTitleFor(string query) =>
        string.IsNullOrWhiteSpace(query)
            ? "Search"
            : query.Length > TabQueryChars ? query[..TabQueryChars] + "…" : query;

    /// <summary>
    /// Pushes the current query out to the tab — title, breadcrumbs and the params a reopened tab is
    /// rebuilt from.
    /// <para>
    /// Driven by <see cref="SearchQuery"/> changing rather than called from each search path, because
    /// there are now several: a fresh run, a refinement, a re-scan, accepting "run as a new search", and
    /// the header field being edited. Wiring them individually is how the breadcrumb ended up showing the
    /// query the tab opened with rather than the one it is showing.
    /// </para>
    /// </summary>
    private void SyncTab()
    {
        if (Tab is null) return;

        Tab.Title      = TabTitleFor(SearchQuery);
        Tab.PageParams = new()
        {
            ["query"]  = SearchQuery,
            ["root"]   = SearchRoot,
            ["drives"] = string.Join(";", _drives),
        };

        Tab.Breadcrumbs.Clear();

        // The scope as its own crumb, and a clickable one: a search under C:\temp should offer the way back
        // to C:\temp. FileBreadcrumbs owns what a directory crumb points at, so this matches every other
        // feature's trail rather than inventing a second convention.
        if (!string.IsNullOrEmpty(SearchRoot))
            Tab.Breadcrumbs.Add(FileBreadcrumbs.ForDirectory(SearchRoot));
        else
            Tab.Breadcrumbs.Add(new BreadcrumbSegment { Label = _drives.Count > 0 ? "This PC" : "Search" });

        Tab.Breadcrumbs.Add(new BreadcrumbSegment { Label = $"Query : {SearchQuery}" });
    }

    partial void OnSearchQueryChanged(string value) => SyncTab();

    private readonly IShellServices      _shellServices;
    private readonly IReadOnlyList<string> _drives;
    private string _baseQuery  = string.Empty;
    private ParsedQuery? _lastParsed;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// How long an index query runs before the wait is worth mentioning. Most answer well inside this, and
    /// a banner that appears on every search is noise that trains you to ignore it.
    /// </summary>
    private static readonly TimeSpan SlowSearchThreshold = TimeSpan.FromSeconds(3);

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

        // Once the scan is this location's answer, a re-run is a re-scan. Going back to the index would
        // show an empty list and offer a scan the user has already asked for.
        if (_scanChosen && CanScan)
        {
            await RunScanAsync();
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
        request.Terms.Any(t => t.Kind == SearchTermKind.Regex || t.HasWildcards);

    private void ShowQueryProblem(string message)
    {
        Results.Clear();
        ResultCount        = 0;
        StatusText         = "Nothing searched.";
        VerificationPhase  = VerifyPhase.Done;
        VerificationBanner = message;
    }

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

    // ── The folder scan ───────────────────────────────────────────────────────

    /// <summary>Somewhere real to walk. "This PC" qualifies now that the scan is the user's own informed
    /// choice — the reason it used to be excluded was that a whole-drive walk could start on its own.</summary>
    private bool CanScan =>
        (!string.IsNullOrEmpty(SearchRoot) && Directory.Exists(SearchRoot)) || _drives.Count > 0;

    /// <summary>
    /// How many rows a folder scan will add before it stops.
    /// <para>
    /// Far above the index's own cap: that one bounds a query the user can refine and re-run in seconds,
    /// while a scan may have spent minutes reading the tree and stopping it early throws that work away.
    /// A limit still exists because every row is a live UI element, and an unbounded list on a broad query
    /// over a large tree is how the window stops responding. When it bites, the banner says so — a
    /// truncated list that claims to be complete is worse than a smaller one that admits it.
    /// </para>
    /// </summary>
    private const int ScanResultCap = 10_000;

    private CancellationTokenSource? _scanCts;

    /// <summary>
    /// True once the user has chosen to scan this location.
    /// <para>
    /// The choice sticks for the life of the tab. Asking the index again would find the same nothing, offer
    /// the same scan, and put the same question back in front of someone who has already answered it — so
    /// every later query here goes straight to the scan.
    /// </para>
    /// </summary>
    private bool _scanChosen;

    /// <summary>
    /// True after a scan was stopped part-way. Stopping leaves a partial result set and no obvious way
    /// back — retyping the query in the header works, but only if you already know that — so the banner
    /// offers to run it again.
    /// </summary>
    [ObservableProperty] private bool _canRescan;

    /// <summary>Every root a scan should cover — the tab's folder, or each drive for "This PC".</summary>
    private IReadOnlyList<string> ScanRoots =>
        string.IsNullOrEmpty(SearchRoot) ? _drives : [SearchRoot];

    /// <summary>
    /// Walks the location, reading files, showing each match the moment it is found. Slow by nature, so
    /// results stream in rather than arriving all at once at the end.
    /// </summary>
    [RelayCommand]
    private Task ScanFolder() => RunScanAsync();

    /// <summary>
    /// The scan itself, separate from the command that fronts it.
    /// <para>
    /// A refinement re-enters the scan while the command is mid-execution. Routing that through
    /// <c>ScanFolderCommand.ExecuteAsync</c> makes the operation depend on the command's own execution
    /// state — which is a UI affordance's business, not the operation's. Kept apart so re-entry is a plain
    /// method call with no command semantics in the way.
    /// </para>
    /// </summary>
    private async Task RunScanAsync()
    {
        if (_activeRequest is not { Terms.Count: > 0 } request || !CanScan) return;

        _scanChosen = true;

        _scanCts?.Cancel();
        var cts  = new CancellationTokenSource();
        _scanCts = cts;
        var ct   = cts.Token;

        // A scan produces its own result set from scratch. Anything already listed was selected by a
        // different query — the broader one this is replacing, or the index query that found nothing.
        Results.Clear();
        ResultCount = 0;

        IsSearching        = true;
        CanRescan          = false;
        VerificationPhase  = VerifyPhase.Scanning;
        VerificationBanner = VerificationPlanner.Scanning(0).Banner;
        StatusText         = "Scanning…";

        var found = 0;
        try
        {
            foreach (var root in ScanRoots)
            {
                ct.ThrowIfCancellationRequested();

                await WindowsSearchService.WalkAsync(request, root, ScanResultCap, hit =>
                {
                    // Off the walk's thread and onto the UI's — the feature never touches a dispatcher
                    // itself, and a hit arriving mid-enumeration must not race the list.
                    _ = _shellServices.RunOnUiAsync(() =>
                    {
                        // A superseded scan can still have callbacks queued behind it. Without this they
                        // land in the list belonging to the query that replaced them.
                        if (ct.IsCancellationRequested) return;

                        Results.Add(hit);
                        ResultCount        = Results.Count;
                        VerificationBanner = VerificationPlanner.Scanning(++found).Banner;
                    });
                }, ct);
            }

            if (!IsCurrentScan(cts)) return;

            var done = VerificationPlanner.AfterScan(found, truncated: found >= ScanResultCap);
            VerificationPhase  = done.Phase;
            VerificationBanner = done.Banner;
        }
        catch (OperationCanceledException)
        {
            // Superseded rather than stopped: the scan that replaced this one owns the banner now, and
            // "Scan stopped" landing on top of its "Scanning…" would report the wrong thing entirely.
            if (!IsCurrentScan(cts)) return;

            var stopped = VerificationPlanner.AfterScan(found, cancelled: true);
            VerificationPhase  = stopped.Phase;
            VerificationBanner = stopped.Banner;
            CanRescan          = true;
        }
        catch (Exception ex)
        {
            if (!IsCurrentScan(cts)) return;

            VerificationPhase  = VerifyPhase.Done;
            VerificationBanner = $"Scan failed: {ex.Message}";
        }
        finally
        {
            // Same reason: a superseded scan must not clear the busy flag or rewrite the status line for
            // the scan still running.
            if (IsCurrentScan(cts))
            {
                IsSearching = false;
                _lastOrigin = SearchOrigin.FolderScan;
                StatusText  = ResultCount == 0
                    ? "No results."
                    : $"{ResultCount} result{(ResultCount == 1 ? "" : "s")}";
            }
        }
    }

    /// <summary>True while <paramref name="cts"/> is still the scan this page is running. False once a
    /// newer scan has taken over, or the page has been disposed.</summary>
    private bool IsCurrentScan(CancellationTokenSource cts) => ReferenceEquals(_scanCts, cts);

    /// <summary>True when what the user is looking at came from a folder scan — one still running, or one
    /// that has finished.</summary>
    private bool ScanOwnsTheResults =>
        _scanChosen || VerificationPhase == VerifyPhase.Scanning || _lastOrigin == SearchOrigin.FolderScan;

    /// <summary>
    /// Narrows a scan by running a tighter one. The walk takes its query up front, so a refinement cannot
    /// be applied to a scan already in flight — it is superseded and restarted with the combined terms.
    /// <para>
    /// Rows already found are discarded rather than filtered down: they were selected by the looser query,
    /// and keeping the subset that survives would present a partly-scanned tree as a fully-scanned one.
    /// </para>
    /// </summary>
    private async Task RefineByRescan(SearchRequest refinement)
    {
        var combined = _activeRequest is { Terms.Count: > 0 } previous
            ? previous with { Terms = [.. previous.Terms, .. refinement.Terms] }
            : refinement;

        _activeRequest = combined;
        _postFilter    = NeedsPostFilter(combined) ? combined : null;
        SearchQuery    = SearchSyntax.Format(combined);

        await RunScanAsync();
    }

    /// <summary>
    /// Stops a running scan, or declines the offer of one.
    /// <para>
    /// Declining retires the banner entirely rather than leaving a "not scanned" note: the user answered
    /// the question, and a banner that stays put after being dismissed reads as one that didn't listen. A
    /// scan that was actually stopped is different — it looked at part of the tree, and what it found is
    /// worth reporting.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void StopScan()
    {
        _scanCts?.Cancel();

        if (VerificationPhase == VerifyPhase.OfferScan)
        {
            VerificationPhase  = VerifyPhase.None;
            VerificationBanner = string.Empty;
        }
    }

    // ── Refining an empty result set ──────────────────────────────────────────

    /// <summary>The refinement that had nothing to narrow, held until the user says what to do with it.</summary>
    private SearchRequest? _pendingNewSearch;

    /// <summary>Runs the refinement as a fresh search, replacing the query rather than narrowing it.</summary>
    [RelayCommand]
    private async Task RunAsNewSearch()
    {
        if (_pendingNewSearch is not { } request) return;
        _pendingNewSearch = null;

        // Replaces rather than merges: the previous query found nothing, so carrying it forward would
        // guarantee the new one finds nothing either.
        SearchQuery = SearchSyntax.Format(request);
        await RunSearchAsync(CancellationToken.None);
    }

    /// <summary>Leaves the empty result set alone.</summary>
    [RelayCommand]
    private void DeclineNewSearch()
    {
        _pendingNewSearch  = null;
        VerificationPhase  = VerifyPhase.None;
        VerificationBanner = string.Empty;
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

        // A search can arrive with rows and end with none: the index returned candidates and verification
        // rejected every one. That leaves the same empty list as a query the index never answered, so it
        // gets the same offer — deciding this only on the row count as it ARRIVED left the user looking at
        // "returned 1 file, 0 confirmed", an empty list, and no way forward.
        if (OfferScanIfNothingToShow(afterSweep: true)) return;

        // "Confirmed" means proven — NOT the row count, which still includes everything unsettled and
        // reads as a far stronger claim than it is.
        var plan = VerificationPlanner.AfterSweep(VerifiedCount, PendingCount, UnreadableCount, UncertainCount, OriginPrefix);
        VerificationPhase  = plan.Phase;
        VerificationBanner = plan.Banner;
    }

    /// <summary>
    /// Offers the folder scan when the search has left nothing on screen, whether it started empty or was
    /// emptied by verification. True when the offer was made.
    /// <para>
    /// Keyed on what the user can SEE, not on what the index returned — those differ by exactly the rows
    /// the post-filter throws away, which is the case that used to fall through the gap.
    /// </para>
    /// </summary>
    /// <summary>
    /// Describes the result set in terms of what the indexer actually covers here — and offers the folder
    /// scan where it would tell the user something the index cannot.
    /// </summary>
    private void ApplyCoverageBanner()
    {
        if (!CanScan)
        {
            VerificationPhase = VerifyPhase.None;
            return;
        }

        // An indexer that could not be reached is a different claim from one that covers nothing, and only
        // the first is worth naming as a fault.
        if (_lastOrigin == SearchOrigin.IndexUnavailable)
        {
            var unavailable = VerificationPlanner.OfferScan(SearchOrigin.IndexUnavailable);
            VerificationPhase  = unavailable.Phase;
            VerificationBanner = unavailable.Banner;
            return;
        }

        // Coverage is a question about ONE location. Across drives there is no single answer, so the
        // reader is not asked and the wording falls back to what the search itself did.
        var coverage = string.IsNullOrEmpty(SearchRoot)
            ? IndexCoverageKind.Unknown
            : _coverage.Coverage(SearchRoot);

        var plan = VerificationPlanner.ForCoverage(coverage, Results.Count > 0);
        VerificationPhase  = plan.Phase;
        VerificationBanner = plan.Banner;
    }

    private bool OfferScanIfNothingToShow(bool afterSweep = false)
    {
        if (Results.Count > 0 || !CanScan) return false;

        var offer = afterSweep
            ? VerificationPlanner.OfferScanAfterSweep(OriginPrefix)
            : VerificationPlanner.OfferScan(_lastOrigin);

        VerificationPhase  = offer.Phase;
        VerificationBanner = offer.Banner;
        return true;
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

        // A new query supersedes a scan of the old one. Left running, its callbacks would keep appending
        // rows to a list that now belongs to a different question.
        _scanCts?.Cancel();
        _scanCts = null;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct = _cts.Token;

        IsSearching = true;
        StatusText  = "Searching…";
        Results.Clear();
        ResultCount = 0;

        // Its own token, cancelled the moment this search ends. Linked to ct alone was not enough: on a
        // NORMAL finish nothing cancels ct, so the notice still fired three seconds later — and if a scan
        // had started by then it wrote "waiting for the index" over the scan's own banner.
        var notice = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = AnnounceIfSlow(notice.Token);

        try
        {
            // When a post-filter is going to discard most rows, the index has to be asked for far more than
            // the user will see — otherwise the cap truncates BEFORE filtering and real matches simply fall
            // off the end. That is what made "/ma(ths)/" lose two files "/maths/" had found.
            var fetch = _postFilter is null ? DefaultResultCap : CandidateFetchCap;

            IReadOnlyList<SearchResultEntry> entries;
            if (_drives.Count > 0 && string.IsNullOrEmpty(SearchRoot))
            {
                // Across drives the index is the only thing asked. A whole-machine scan is still possible,
                // but only if the user asks for it after seeing the offer — it is not somewhere to arrive
                // by accident. The origin is taken from the result, not assumed: an indexer that is down
                // returns empty from every drive, and calling that "the index searched" hides the one
                // fact that would let the user do something about it.
                var across  = await WindowsSearchService.SearchAcrossWithOriginAsync(parsed, _drives, ct, fetch);
                entries     = across.Entries;
                _lastOrigin = across.Origin;
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

            // Rows that might not be real outrank how complete the search was: settle them first, and let
            // CompleteSweep have the last word on the banner.
            if (_postFilter is not null && Candidates.Count > 0) BeginVerification();
            else ApplyCoverageBanner();
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

            // A dead end otherwise: the index is the one thing that can't answer this, and the scan is
            // the one thing that can.
            if (CanScan)
            {
                var offer = VerificationPlanner.OfferScan(SearchOrigin.IndexUnavailable);
                VerificationPhase  = offer.Phase;
                VerificationBanner = offer.Banner;
            }
        }
        catch (Exception ex)
        {
            StatusText  = $"Search error: {ex.Message}";
            ResultCount = 0;
        }
        finally
        {
            notice.Cancel();
            IsSearching = false;

            // The waiting notice is the only phase nothing else replaces — every other exit from here
            // writes its own banner. Leaving it up would say we are still waiting when we are not.
            if (VerificationPhase == VerifyPhase.Searching)
            {
                VerificationPhase  = VerifyPhase.None;
                VerificationBanner = string.Empty;
            }
        }
    }

    /// <summary>
    /// Says who we are waiting for once the index has been thinking for a while. Reported through the
    /// banner — the page's one state machine — rather than as a separate control: two independent pieces
    /// of state meant two things could be on screen at once, and they were.
    /// </summary>
    private async Task AnnounceIfSlow(CancellationToken ct)
    {
        try { await Task.Delay(SlowSearchThreshold, ct); }
        catch (OperationCanceledException) { return; }   // finished, or superseded

        if (ct.IsCancellationRequested || !IsSearching) return;

        // The delay resumes off the UI thread when there is no context to capture, so this goes through
        // the shell rather than assuming one — features never touch a dispatcher themselves.
        await _shellServices.RunOnUiAsync(() =>
        {
            if (ct.IsCancellationRequested || !IsSearching) return;

            var plan = VerificationPlanner.WaitingForIndex();
            VerificationPhase  = plan.Phase;
            VerificationBanner = plan.Banner;
        });
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
            "Search files using Windows Search. Terms are AND-ed; use | inside a term for alternatives " +
            "(*.txt|*.md). Supports plain words matched WHOLE (needle does not match needless), quoted " +
            "phrases (\"annual report\"), file globs matched against the name and the text (*.xml, " +
            "report*), regular expressions between slashes (/ma(ths|gic)/), and Advanced Query Syntax " +
            "property constraints (kind:document, size:>1mb, author:john, modified:last week) — anything " +
            "Explorer's search box accepts.",
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
            }),

        new DelegateClientTool(
            "index_coverage",
            "Reports whether Windows indexes a folder, and the crawl-scope rules that decide it. " +
            "Use this to answer 'why isn't this indexed?' or 'why did the search find nothing?' — the " +
            "rules explain a result the search itself can only show. Read-only: it reports the machine's " +
            "indexing configuration and never changes it.",
            [new ClientToolParameter(
                "path",
                "Folder to explain. Defaults to the folder this search tab is scoped to.",
                Required: false)],
            ToolSafety.SafeOperation,
            (arguments, ct) =>
            {
                var path = arguments["path"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(path)) path = SearchRoot;

                if (string.IsNullOrWhiteSpace(path))
                    return Task.FromResult(ToolResult.Error(
                        "This search covers several drives, so there is no single location to report on. " +
                        "Name a folder to explain."));

                var coverage = _coverage.Coverage(path);
                var rules    = _coverage.RulesFor(path);

                var summary = coverage switch
                {
                    IndexCoverageKind.Full    => $"'{path}' is fully indexed.",
                    IndexCoverageKind.Partial => $"'{path}' is indexed, but part of it is excluded.",
                    IndexCoverageKind.None    => $"'{path}' is not indexed by Windows.",
                    _                         => $"Windows Search could not be asked about '{path}'.",
                };

                // The rules ARE the explanation — a verdict on its own just restates what the user saw.
                var detail = rules.Count == 0
                    ? "No crawl-scope rule mentions this path, so it is covered (or not) by the indexer's defaults."
                    : string.Join("\n", rules.Select(r => "  " + r));

                return Task.FromResult(ToolResult.Ok(
                    summary,
                    $"{summary}\n\nCrawl-scope rules affecting it:\n{detail}"));
            })
    ];

    // ── ISearchable ───────────────────────────────────────────────────────────

    /// <summary>The AQS parser this page's queries are built against — shared by the recogniser that
    /// spots a property constraint and the parser that turns it into SQL, so both agree on what the
    /// index will actually enforce.</summary>
    private readonly AqsTranslator _aqs = new();

    /// <summary>Reads how much of this location the indexer covers, so an empty result can be reported as
    /// what it actually means rather than as "not found".</summary>
    private readonly IndexCoverageReader _coverage = new();

    /// <summary>This page searches files, so filename globs and index property constraints both mean
    /// something here.</summary>
    public IReadOnlyList<ISearchTermRecognizer> TermRecognizers =>
        [new GlobTermRecognizer(), new AqsTermRecognizer(_aqs)];

    public string SearchTargetDescription =>
        $"files matching the current search '{SearchQuery}'" +
        (string.IsNullOrEmpty(SearchRoot) ? " across this PC" : $" under '{SearchRoot}'");

    /// <summary>Refining is the overwhelmingly likely intent on a page that is already showing search
    /// results, so a well-formed query claims the input outright rather than competing with the agent.</summary>
    public const float RefineScore = 0.9f;

    /// <summary>
    /// Someone looking at search results who types a search-shaped query almost always means "narrow this".
    /// One term is the clearest case; every extra term reads a little more like a sentence and a little
    /// less like a filter, so the score decays rather than cutting off — leaving genuine prose to the agent
    /// without a threshold that snaps.
    /// </summary>
    /// <remarks>Floored, because even a wordy refinement here is likelier than the same words on a page
    /// with nothing to refine.</remarks>
    public static float ScoreRefinement(int termCount) =>
        termCount <= 0 ? 0f : Math.Max(0.5f, RefineScore - 0.1f * (termCount - 1));

    /// <summary>
    /// Deliberately does NOT bow out while a search or scan is running.
    /// <para>
    /// It used to return zero, which sent a refinement typed during a long folder scan to the AI instead —
    /// and a scan is precisely when the user is sat watching results arrive and deciding to narrow them.
    /// Whether the page is busy is a question for whoever handles the query, not for whether it belongs
    /// here.
    /// </para>
    /// </summary>
    public float ScoreQuery(string input)
    {
        // Parsed with this page's own recognisers, so a glob, a quoted phrase or a /regex/ counts as the
        // single term the user typed rather than as however many words it happens to contain.
        var terms = SearchSyntax.ParseTerms(input, TermRecognizers);
        return terms.Count > 0
            ? ScoreRefinement(terms.Count)
            : SearchQueryScorer.Score(input);
    }

    public async Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (!request.TryValidate(out var invalid))
            return SearchOutcome.Unsupported($"Invalid pattern: {invalid}");

        if (!HasSearchScope)
            return SearchOutcome.Unsupported("This search tab has no scope to search.");

        // Results that came from a folder scan can only be narrowed by another folder scan. Re-querying the
        // index would ask the one source that already had nothing to say about this location — which is
        // why the scan ran in the first place — and answer a narrower question with an emptier list.
        if (display && ScanOwnsTheResults)
        {
            await RefineByRescan(request);
            return SearchOutcome.None();
        }

        // Narrowing nothing yields nothing. Answering with the same empty list would look like the query
        // was ignored, so the likely intent — the same search, fresh, in the same place — is offered.
        if (display && Results.Count == 0)
        {
            _pendingNewSearch = request;
            var ask = VerificationPlanner.OfferNewSearch(SearchSyntax.Format(request));
            VerificationPhase  = ask.Phase;
            VerificationBanner = ask.Banner;
            return SearchOutcome.None();
        }

        // A refinement narrows on its terms' seeds, then exactly via the post-filter.
        var refined = SearchQueryParser.FromTerms(request.Terms, _aqs);
        if (refined is null)
            return SearchOutcome.Unsupported(UnseedableNote(request.Text));

        // Merged from the combined TERMS, for the same reason as MergeAndSearchAsync: re-parsing the
        // rendered base query reads a constraint or a pattern as literal characters to look for.
        var combined = _activeRequest is { Terms.Count: > 0 } previous
            ? (IReadOnlyList<SearchTerm>)[.. previous.Terms, .. request.Terms]
            : request.Terms;

        var merged = SearchQueryParser.FromTerms(combined, _aqs) ?? refined;

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

    /// <summary>
    /// Stops everything this tab started. The shell disposes a page's ViewModel when the tab closes, so
    /// without this a folder scan would keep walking the disk — and keep reading files — for a tab nobody
    /// is looking at any more, with no way left to stop it.
    /// </summary>
    public void Dispose()
    {
        _cts?.Cancel();
        _verifyCts?.Cancel();
        _scanCts?.Cancel();

        _cts       = null;
        _verifyCts = null;
        _scanCts   = null;

        _aqs.Dispose();
        _coverage.Dispose();
    }
}
