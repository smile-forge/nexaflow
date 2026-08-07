using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Json.Models;
using Nexaflow.Features.Json.Services;
using Nexaflow.Search;
using System.Globalization;

namespace Nexaflow.Features.Json.ViewModels;

/// <summary>
/// The JSON tab as a searchable page.
/// <para>
/// A small document is scanned in memory; a windowed one is streamed off the UI thread through
/// <see cref="JsonTextScanner"/>, so <c>?</c> searches the whole FILE rather than the few hundred
/// realised nodes. A hit is the <b>top-level item whose subtree contains the match</b>, identified by its
/// depth-1 index — the one id that survives the window evicting a node, and the language the byte-offset
/// index and the batch loader already speak. A nested match is therefore reported on its depth-1 ancestor,
/// which is the only thing the loader can address and reveal.
/// </para>
/// <para>Orthogonal to the <c>$</c> JSONPath handler: that answers a structural address, this answers
/// "where does this text appear".</para>
/// </summary>
internal sealed partial class JsonViewModel : ISearchable
{
    /// <summary>Hits handed to the agent — and, because every hit is revealable, also the ceiling on how
    /// many items a single search will mark.</summary>
    private const int SearchHitCap = 200;

    /// <summary>Above this we refuse rather than grind: the scan deserialises and re-serialises every
    /// top-level item, so a multi-gigabyte file would tie the page up with no way to cancel from the UI.</summary>
    private const long MaxScanBytes = 512L * 1024 * 1024;

    private readonly JsonTextScanner _scanner = new(new JsonFileLoader());

    // ── Visible search state (the toolbar chip binds these) ───────────────────

    [ObservableProperty] private bool   _isSearchActive;
    [ObservableProperty] private int    _searchMatchCount;
    [ObservableProperty] private bool   _isSearchTruncated;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    private int[] _matchIndices = [];    // depth-1 indices, ascending
    private int   _currentMatchIndex = -1;

    /// <summary>Where each match's containing chunk starts, from the last streamed scan. The viewer's own
    /// byte-offset index only knows offsets it has already loaded, so this is what lets a reveal seek
    /// straight to a hit thousands of items away instead of walking there a batch at a time.</summary>
    private readonly Dictionary<int, (int FirstIndex, long Offset)> _matchSeeds = [];

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription => IsLargeFile
        ? $"the JSON file '{FileName}' — every top-level item, streamed from disk"
        : $"the JSON document '{FileName}' — every key and value";

    public float ScoreQuery(string input) => SearchScoring.TermCount(input) switch
    {
        1 => 0.9f,
        2 => 0.8f,
        3 => 0.6f,
        4 => 0.2f,
        _ => 0f,
    };

    public async Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (Root is null) return SearchOutcome.Unsupported("No JSON document is loaded yet.");
        if (!TextSearchMatcher.TryCreate(request, out var matcher, out var error))
            return SearchOutcome.Unsupported(error);

        List<JsonTextScanner.ItemMatch> matches;
        if (IsLargeFile)
        {
            if (_fileSize > MaxScanBytes)
                return SearchOutcome.Unsupported(
                    $"'{FileName}' is {FileSizeText} — too large to search in one pass. " +
                    "Narrow it with a JSONPath ($) query first.");

            // The file offset of the first depth-1 item — the loader records it as index 0 of the
            // sparse byte-offset index on load.
            var (_, startOffset) = GetBestOffsetWithIndex(0);

            var scan = await _scanner.ScanAsync(
                FilePath, startOffset, _fileSize, Root is JsonArrayNodeModel,
                matcher!, SearchHitCap, ct);
            matches = scan.Matches;

            _matchSeeds.Clear();
            foreach (var m in matches) _matchSeeds[m.Index] = (m.ChunkFirstIndex, m.ChunkOffset);
        }
        else
        {
            matches = ScanLoadedTree(matcher!, ct);
        }

        ct.ThrowIfCancellationRequested();

        var truncated = matches.Count >= SearchHitCap;
        var hits = matches
            .Select(m => new SearchHit(m.Index.ToString(CultureInfo.InvariantCulture), m.Label, m.Preview))
            .ToList();

        if (display)
            await _shellServices.RunOnUiAsync(async () =>
            {
                await ShowAsync(matches, SearchSyntax.Format(request), truncated, ct);
                return true;
            });

        if (matches.Count == 0) return SearchOutcome.None();

        return truncated
            ? SearchOutcome.Found(hits, matches.Count,
                $"Stopped after the first {SearchHitCap} matching items — there may be more.")
            : SearchOutcome.Found(hits, matches.Count);
    }

    /// <summary>Small-file path: the whole tree is already in memory, so serialise each top-level item
    /// and test it — the same "a hit is the item containing the match" rule as the streamed path.</summary>
    private List<JsonTextScanner.ItemMatch> ScanLoadedTree(TextSearchMatcher matcher, CancellationToken ct)
    {
        var found    = new List<JsonTextScanner.ItemMatch>();
        var children = GetChildren(Root);
        if (children is null) return found;

        for (var i = 0; i < children.Count && found.Count < SearchHitCap; i++)
        {
            ct.ThrowIfCancellationRequested();
            var child = children[i];
            if (child is VirtualJsonNodeModel) continue;

            var body = JsonNodeSerializer.Serialize(child);
            var text = child.Key is null ? body : $"\"{child.Key}\": {body}";
            if (!matcher.Matches(text)) continue;

            // No chunk seed: a small document is entirely realised, so nothing ever needs loading to
            // reveal it.
            found.Add(new JsonTextScanner.ItemMatch(
                i, 0, 0, child.DisplayKey, text.Length <= 200 ? text : text[..200] + "…"));
        }
        return found;
    }

    public async Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var indices = hits.Select(h => int.TryParse(h.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : -1)
                          .Where(i => i >= 0)
                          .Distinct()
                          .OrderBy(i => i)
                          .ToArray();
        if (indices.Length == 0) return false;

        return await _shellServices.RunOnUiAsync(async () =>
        {
            _matchIndices      = indices;
            _currentMatchIndex = 0;
            SearchMatchCount   = indices.Length;
            IsSearchTruncated  = false;
            IsSearchActive     = true;
            MarkSearchHits();
            await RevealDepth1Async(indices[0], ct);
            return true;
        });
    }

    // ── Display (UI thread) ───────────────────────────────────────────────────

    private async Task ShowAsync(IReadOnlyList<JsonTextScanner.ItemMatch> matches, string term,
                                 bool truncated, CancellationToken ct)
    {
        _matchIndices      = [.. matches.Select(m => m.Index)];
        _currentMatchIndex = _matchIndices.Length > 0 ? 0 : -1;
        SearchMatchCount   = matches.Count;
        IsSearchTruncated  = truncated;
        CurrentSearchTerm  = term;
        IsSearchActive     = true;   // true even at zero matches: "no matches for X" is a result to show

        MarkSearchHits();
        if (_matchIndices.Length > 0) await RevealDepth1Async(_matchIndices[0], ct);
    }

    /// <summary>Washes the depth-1 rows that matched. Only the realised ones can carry the mark; the rest
    /// pick it up as <see cref="MarkSearchHits"/> re-runs after each batch lands.</summary>
    private void MarkSearchHits()
    {
        var hits = new HashSet<int>(_matchIndices);
        foreach (var item in DisplayItems)
        {
            if (item.Depth != 1) { item.IsSearchHit = false; continue; }
            var idx = Depth1IndexOf(item);
            item.IsSearchHit = idx >= 0 && hits.Contains(idx);
        }
    }

    /// <summary>A depth-1 row's file-order index — see <see cref="Depth1ItemAt"/> for why an array element
    /// is answered from the node itself rather than from its position.</summary>
    private int Depth1IndexOf(JsonDisplayItem item)
    {
        if (item is JsonVirtualDisplayItem { RootChildIndex: >= 0 } v) return v.RootChildIndex;
        if (item.Node is null) return -1;
        if (item.Node is VirtualJsonNodeModel virt) return virt.Index ?? -1;
        if (Root is JsonArrayNodeModel) return item.Node.Index ?? -1;
        if (IsLargeFile && _loadWindowStart != 0) return -1;

        var seen = 0;
        foreach (var d in DisplayItems)
        {
            if (d.Depth != 1 || d.Node is null or VirtualJsonNodeModel) continue;
            if (ReferenceEquals(d, item)) return seen;
            seen++;
        }
        return -1;
    }

    /// <summary>
    /// Brings depth-1 item <paramref name="rootChildIndex"/> onto the display list — loading the batch
    /// that contains it when the window has moved past it — then selects and scrolls to it.
    /// <para>Without this, a search could count matches it has no way to show: both
    /// <c>SelectAndScrollToNode</c> and <c>EvaluateJsonPath</c> silently do nothing for an unloaded node.</para>
    /// </summary>
    internal async Task<bool> RevealDepth1Async(int rootChildIndex, CancellationToken ct)
    {
        if (TrySelectDepth1(rootChildIndex)) return true;
        if (!IsLargeFile) return false;

        // One attempt only: a known-failed offset must not loop. LoadFromIndexAsync guards itself with
        // _loadInProgress, so wait for any in-flight scroll load rather than racing it.
        for (var i = 0; i < 50 && _loadInProgress; i++) await Task.Delay(10, ct);
        if (_loadInProgress) return false;

        // Teach the loader where this item actually lives before asking for it. Without the seed,
        // GetBestOffsetWithIndex falls back to the nearest earlier offset it happens to know — which for a
        // hit thousands of items out is the front batch, so the load would land nowhere near it.
        if (_matchSeeds.TryGetValue(rootChildIndex, out var seed))
            _byteOffsetIndex[seed.FirstIndex] = seed.Offset;

        await LoadFromIndexAsync(rootChildIndex);
        MarkSearchHits();
        return TrySelectDepth1(rootChildIndex);
    }

    private bool TrySelectDepth1(int rootChildIndex)
    {
        var item = Depth1ItemAt(rootChildIndex);
        if (item is null) return false;

        SelectedDisplayItem = item;
        ScrollToItemRequested?.Invoke(this, item);
        return true;
    }

    /// <summary>
    /// The realised depth-1 row at a file-order index, or null when it is not currently loaded.
    /// <para>Deliberately NOT computed as "position minus <c>_loadWindowStart</c>": revealing a hit loads a
    /// batch that can sit far from the existing run, leaving the loaded set non-contiguous with a sentinel
    /// in the gap — so position stops tracking file order. An array element carries its own file index, so
    /// use that. An object's keys carry none, so fall back to position, which is exact only while the
    /// window still starts at the top of the file.</para>
    /// </summary>
    private JsonDisplayItem? Depth1ItemAt(int rootChildIndex)
    {
        if (rootChildIndex < 0) return null;

        if (Root is JsonArrayNodeModel)
            return DisplayItems.FirstOrDefault(
                d => d.Depth == 1 && d.Node is not (null or VirtualJsonNodeModel)
                                  && d.Node.Index == rootChildIndex);

        if (IsLargeFile && _loadWindowStart != 0) return null;

        var seen = 0;
        foreach (var d in DisplayItems)
        {
            if (d.Depth != 1 || d.Node is null or VirtualJsonNodeModel) continue;
            if (seen++ == rootChildIndex) return d;
        }
        return null;
    }

    // ── Navigation / dismissal (the toolbar chip) ─────────────────────────────

    public bool HasSearchMatches => SearchMatchCount > 0;

    /// <summary>"+" when the scan stopped at the cap, so the count never reads as an exact total.</summary>
    public string SearchCountSuffix => IsSearchTruncated ? "+" : string.Empty;

    partial void OnSearchMatchCountChanged(int value) => OnPropertyChanged(nameof(HasSearchMatches));
    partial void OnIsSearchTruncatedChanged(bool value) => OnPropertyChanged(nameof(SearchCountSuffix));

    [RelayCommand]
    private Task FindNextMatchAsync() => StepMatchAsync(+1);

    [RelayCommand]
    private Task FindPreviousMatchAsync() => StepMatchAsync(-1);

    private async Task StepMatchAsync(int delta)
    {
        if (_matchIndices.Length == 0) return;
        _currentMatchIndex = (_currentMatchIndex + delta + _matchIndices.Length) % _matchIndices.Length;
        await RevealDepth1Async(_matchIndices[_currentMatchIndex], CancellationToken.None);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _matchIndices      = [];
        _currentMatchIndex = -1;
        SearchMatchCount   = 0;
        IsSearchTruncated  = false;
        CurrentSearchTerm  = string.Empty;
        IsSearchActive     = false;
        foreach (var item in DisplayItems) item.IsSearchHit = false;
    }
}
