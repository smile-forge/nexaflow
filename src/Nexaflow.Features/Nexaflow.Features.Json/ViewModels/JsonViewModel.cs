using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Json.Models;
using Nexaflow.Features.Json.Services;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Collections;
using Nexaflow.Visuals.Common.Formatting;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace Nexaflow.Features.Json.ViewModels;

internal sealed partial class JsonViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private readonly JsonFileLoader    _loader;
    private readonly JsonPathEvaluator _evaluator;
    private readonly IShellServices    _shellServices;

    // O(1) lookup from model node → its display item (loaded nodes only)
    private readonly Dictionary<JsonNodeModel, JsonTreeDisplayItem> _nodeItems = [];
    private JsonInlineContentDisplayItem? _activeInlineItem;
    private CancellationTokenSource? _loadCts;

    // ── Streaming / window state (large files only) ──────────────────────────
    // The display list is pre-populated with N_estimated virtual placeholders.
    // A sliding window of ≤ MaxWindowSize real model nodes is kept in memory.
    // RangeObservableCollection lets us add thousands of placeholders in one shot.

    private const int MaxWindowSize    = 300;   // max real depth-1 nodes in memory at once
    private const int LoadBatchSize    = 50;    // items per load batch
    private const int UnloadBatchSize  = 100;   // items unloaded at a time when window is full
    private const int MaxPrePopulated  = 10_000; // cap on pre-populated virtual rows

    private long _fileSize;
    private long _avgItemBytes    = 512;
    private int  _estimatedCount;              // estimated total depth-1 items (0 = unknown)
    private int  _loadWindowStart = 0;         // first real depth-1 node index in Root.Children
    private int  _loadWindowEnd   = 0;         // exclusive end (count of loaded nodes)

    // Sparse byte-offset index: depth-1 item index → absolute file byte offset.
    // Populated as we load batches; used to seek for both forward and backward loading.
    private readonly SortedList<int, long> _byteOffsetIndex = new();

    private bool _loadInProgress;

    // Failure tracking: avoid infinite retry storms when a parse fails (each retry
    // throws a fresh JsonReaderException → debug-output spam + user confusion).
    // Failed byte offsets are remembered per file load.
    private readonly HashSet<long> _failedOffsets = [];
    private bool _hasReportedLoadError;

    // ── Root-array table mode state ──────────────────────────────────────────
    // When true, depth-1 rows of the root array are rendered as table rows in
    // the outer DisplayList (instead of as tree rows). The pre-populated virtual
    // placeholders and load-on-scroll machinery are unchanged, so the table
    // inherits the tree's scrolling behavior.
    private bool _isRootTableMode;
    private IReadOnlyList<string> _tableColumns = [];

    // Visible viewport range (display list indices), updated by the view's scroll handler.
    // Defaults assume ~50-row viewport so the initial load auto-fills before any scroll event.
    private int _firstVisibleDisplayIdx;
    private int _lastVisibleDisplayIdx = 50;
    private const int PreloadRows = 50;   // load this many rows past the visible bottom

    public JsonViewModel(string filePath, JsonFileLoader loader,
                         JsonPathEvaluator evaluator, IShellServices shellServices)
    {
        _loader        = loader;
        _evaluator     = evaluator;
        _shellServices = shellServices;
        FilePath       = filePath;
        FileName       = Path.GetFileName(filePath);
    }

    // ── File state ───────────────────────────────────────────────────────────

    [ObservableProperty] private string         _filePath      = string.Empty;
    [ObservableProperty] private string         _fileName      = string.Empty;
    [ObservableProperty] private string         _fileSizeText  = string.Empty;
    [ObservableProperty] private int            _nodeCount;
    [ObservableProperty] private bool           _isLargeFile;
    [ObservableProperty] private bool           _isLoading;
    [ObservableProperty] private bool           _isModified;
    [ObservableProperty] private JsonNodeModel? _root;
    [ObservableProperty] private string?        _errorMessage;

    partial void OnFilePathChanged(string value) => FileName = Path.GetFileName(value);

    // ── Display list ─────────────────────────────────────────────────────────

    public RangeObservableCollection<JsonDisplayItem> DisplayItems           { get; } = [];
    public ObservableCollection<JsonBreadcrumbItem>  SelectedNodeBreadcrumbs { get; } = [];

    [ObservableProperty] private JsonDisplayItem? _selectedDisplayItem;

    public JsonNodeModel? SelectedNode
        => (SelectedDisplayItem as JsonTreeDisplayItem)?.Node
        ?? (SelectedDisplayItem as JsonTableRowDisplayItem)?.Node
        ?? (SelectedDisplayItem as JsonInlineContentDisplayItem)?.Node;

    public bool IsTreeModeActive  => !_isRootTableMode
                                  && (_activeInlineItem?.Node != SelectedNode || SelectedNode is null);
    public bool IsTextModeActive  => _activeInlineItem?.Node == SelectedNode
                                  && _activeInlineItem?.ViewMode == NodeViewMode.Text;
    public bool IsTableModeActive => _isRootTableMode && SelectedNode == Root
                                  || (_activeInlineItem?.Node == SelectedNode
                                      && _activeInlineItem?.ViewMode == NodeViewMode.Table);
    public bool IsTableModeAvailable => SelectedNode is JsonArrayNodeModel arr
                                     && arr.Children.OfType<JsonObjectNodeModel>().Any();

    /// <summary>True when there are still unloaded depth-1 positions in the display list.</summary>
    public bool HasVirtualItems
        => DisplayItems.OfType<JsonVirtualDisplayItem>().Any();

    public event EventHandler<JsonDisplayItem>? ScrollToItemRequested;

    partial void OnSelectedDisplayItemChanged(JsonDisplayItem? value)
    {
        OnPropertyChanged(nameof(SelectedNode));
        OnPropertyChanged(nameof(IsTreeModeActive));
        OnPropertyChanged(nameof(IsTextModeActive));
        OnPropertyChanged(nameof(IsTableModeActive));
        OnPropertyChanged(nameof(IsTableModeAvailable));
        RebuildBreadcrumbs(SelectedNode);

        // Fallback: if user explicitly clicks a virtual row, load it
        if (value is JsonVirtualDisplayItem vdi && vdi.Node is VirtualJsonNodeModel)
            _ = LoadVirtualItemAsync(vdi);
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    public async Task LoadAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(FilePath)) return;

        _loadCts?.Cancel();
        _loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        IsLoading    = true;
        ErrorMessage = null;
        _nodeItems.Clear();
        _activeInlineItem = null;
        DisplayItems.Clear();
        _byteOffsetIndex.Clear();
        _failedOffsets.Clear();
        _hasReportedLoadError = false;
        _loadWindowStart = 0;
        _loadWindowEnd   = 0;
        _estimatedCount  = 0;

        try
        {
            var result = await _loader.LoadAsync(FilePath, _loadCts.Token);
            if (result.ErrorMessage is not null)
            {
                ErrorMessage = result.ErrorMessage;
                return;
            }

            Root         = result.Root;
            NodeCount    = result.NodeCount;
            IsLargeFile  = result.IsLargeFile;
            FileSizeText = FormatSize(result.FileSizeBytes);
            _fileSize    = result.FileSizeBytes;

            if (Root is not null)
            {
                InsertNodeItem(0, Root, depth: 0);
                ExpandRootWithStreaming(Root);
            }

            if (result.IsLargeFile)
            {
                // Anchor index 0 → byte position of item 0's first byte. Without this,
                // a backward load to the top would call LoadVirtualChunkAsync with
                // offset 0 (before BOM / opening bracket) → guaranteed parse failure.
                if (result.RootContentOffset > 0)
                    _byteOffsetIndex[0] = result.RootContentOffset;

                // Estimate and pre-populate in background so initial render isn't blocked
                _ = EstimateAndPrepopulateAsync(_loadCts.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally { IsLoading = false; }
    }

    // After initial load, estimate the total item count from the first+last objects,
    // then pre-populate the display list with virtual placeholders up to that count.
    // This gives the scrollbar the correct size without loading the whole file.
    private async Task EstimateAndPrepopulateAsync(CancellationToken ct)
    {
        try
        {
            var isArray  = Root is JsonArrayNodeModel;
            var estimate = await _loader.EstimateAsync(FilePath, _fileSize, isArray, ct);
            if (estimate is null || ct.IsCancellationRequested) return;

            _avgItemBytes   = estimate.AvgItemBytes;
            _estimatedCount = estimate.EstimatedCount;

            // Already on the UI thread (the await above resumed here); update the display directly.
            if (ct.IsCancellationRequested) return;
            {
                // How many depth-1 display items are already in the list?
                var alreadyInDisplay = CountDepth1InDisplay();

                // How many more virtual placeholders should we add?
                var target  = Math.Min(_estimatedCount, MaxPrePopulated);
                var toAdd   = target - alreadyInDisplay;
                if (toAdd <= 0) return;

                // Find the display index after the last depth-1 item
                var insertAt = FindInsertIndexAfterLastDepth1();

                var placeholders = Enumerable.Range(alreadyInDisplay, toAdd)
                    .Select(idx => (JsonDisplayItem)new JsonVirtualDisplayItem
                    {
                        Depth          = 1,
                        RootChildIndex = idx,
                    });

                // Temporarily add all items (bulk notifies as a single Reset)
                // We insert at the right position by building a slice and rebuilding
                // the tail of the collection.
                var tail = DisplayItems.Skip(insertAt).ToList();
                while (DisplayItems.Count > insertAt)
                    DisplayItems.RemoveAt(DisplayItems.Count - 1);

                DisplayItems.AddRange(placeholders);
                DisplayItems.AddRange(tail.Cast<JsonDisplayItem>());

                OnPropertyChanged(nameof(HasVirtualItems));

                // Kick off loading so the initial viewport fills without a scroll
                ScheduleNextLoadIfNeeded();
            }
        }
        catch (OperationCanceledException) { }
        catch { /* best-effort */ }
    }

    // Expand the root node.  For large files, the root's children already have a mix
    // of real nodes and a trailing VirtualJsonNodeModel sentinel; put them all in the
    // display list at depth 1 (real → tree items, sentinel → virtual display item).
    private void ExpandRootWithStreaming(JsonNodeModel root)
    {
        if (!_nodeItems.TryGetValue(root, out var rootItem)) return;
        if (rootItem.IsExpanded) return;
        rootItem.IsExpanded = true;

        var insertAt = DisplayItems.IndexOf(rootItem) + 1;
        var children = GetChildren(root) ?? [];
        var idx      = 0;

        foreach (var child in children)
        {
            if (child is VirtualJsonNodeModel v)
            {
                var vdi = new JsonVirtualDisplayItem { Node = v, Depth = 1 };
                DisplayItems.Insert(insertAt++, vdi);
                // Record the byte offset for the start of this batch
                if (v.ByteOffset > 0 && idx > 0)
                    _byteOffsetIndex[idx] = v.ByteOffset;
            }
            else
            {
                insertAt = InsertNodeItem(insertAt, child, 1);
                idx++;
            }
        }

        // Record how many real nodes were loaded
        _loadWindowEnd = children.Count(c => c is not VirtualJsonNodeModel);

        // Record offset for first load batch
        if (_byteOffsetIndex.Count == 0 && children.OfType<VirtualJsonNodeModel>().FirstOrDefault() is { } sentinel)
            _byteOffsetIndex[_loadWindowEnd] = sentinel.ByteOffset;
    }

    // ── Virtual node loading ──────────────────────────────────────────────────

    /// <summary>
    /// Records the currently visible display-list index range.  Called by the view
    /// on every ScrollChanged so the VM knows where the user is.
    /// </summary>
    internal void SetVisibleRange(int firstDisplayIdx, int lastDisplayIdx)
    {
        _firstVisibleDisplayIdx = firstDisplayIdx;
        _lastVisibleDisplayIdx  = lastDisplayIdx;
    }

    /// <summary>
    /// Loads the next batch IF there's a virtual placeholder in or near the visible
    /// viewport. Viewport-aware: picks the closest placeholder to the viewport (so
    /// scrolling up loads upward, scrolling down loads downward), and never retries
    /// known-failed offsets.
    /// </summary>
    internal void TriggerVirtualLoads()
    {
        if (_loadInProgress) return;
        // Gate by viewport so chain-loads stop once the visible band is satisfied —
        // otherwise we'd happily load all 10k placeholders into memory.
        if (!HasVirtualItemsInPreloadRange()) return;

        var target = FindNextLoadTarget();
        if (target is null) return;

        if (target.Node is VirtualJsonNodeModel v)
        {
            if (v.IsLoading || v.IsLoadFailed) return;
            _ = LoadVirtualItemAsync(target);
        }
        else if (target.RootChildIndex >= 0)
        {
            _ = LoadFromIndexAsync(target.RootChildIndex);
        }
    }

    /// <summary>
    /// Find the JsonVirtualDisplayItem closest to the viewport that hasn't already
    /// failed to load. Bounded to the viewport+preload band: if every virtual in
    /// the band is unloadable (all map to failed offsets), we return null instead
    /// of sweeping to far-off placeholders — that would trigger wrong-direction
    /// loads and cascade failures.
    /// </summary>
    private JsonVirtualDisplayItem? FindNextLoadTarget()
    {
        if (DisplayItems.Count == 0) return null;

        var start = Math.Max(0, _firstVisibleDisplayIdx - PreloadRows);
        var end   = Math.Min(DisplayItems.Count - 1, _lastVisibleDisplayIdx + PreloadRows);
        if (start > end) return null;

        var anchor = Math.Clamp(
            (_firstVisibleDisplayIdx + _lastVisibleDisplayIdx) / 2, start, end);

        var maxRadius = Math.Max(anchor - start, end - anchor);
        for (var r = 0; r <= maxRadius; r++)
        {
            var down = anchor + r;
            if (down <= end && IsLoadableVirtual(DisplayItems[down]))
                return (JsonVirtualDisplayItem)DisplayItems[down];

            var up = anchor - r;
            if (r > 0 && up >= start && IsLoadableVirtual(DisplayItems[up]))
                return (JsonVirtualDisplayItem)DisplayItems[up];
        }
        return null;
    }

    private bool IsLoadableVirtual(JsonDisplayItem item)
    {
        if (item is not JsonVirtualDisplayItem vdi) return false;
        if (vdi.Node is VirtualJsonNodeModel v)
            return !v.IsLoading && !v.IsLoadFailed;
        if (vdi.RootChildIndex >= 0)
        {
            // For pre-pop placeholders, check the offset they'd resolve to
            var (_, offset) = GetBestOffsetWithIndex(vdi.RootChildIndex);
            return !_failedOffsets.Contains(offset);
        }
        return false;
    }

    private bool HasVirtualItemsInPreloadRange()
    {
        // Scan from the top of the visible area down to (lastVisible + PreloadRows).
        var start = Math.Max(0, _firstVisibleDisplayIdx);
        var end   = Math.Min(DisplayItems.Count - 1, _lastVisibleDisplayIdx + PreloadRows);
        for (var i = start; i <= end; i++)
        {
            if (DisplayItems[i] is JsonVirtualDisplayItem) return true;
        }
        return false;
    }

    // Defer to the UI thread so the load chain unwinds the current call stack instead of recursing.
    private void ScheduleNextLoadIfNeeded()
        => _ = _shellServices.RunOnUiAsync(TriggerVirtualLoads);

    // Loads the next sequential batch (triggered by VirtualJsonNodeModel sentinel).
    private async Task LoadVirtualItemAsync(JsonVirtualDisplayItem item)
    {
        if (item.Node is not VirtualJsonNodeModel virtualNode) return;
        if (virtualNode.IsLoading || virtualNode.IsLoadFailed) return;
        if (_failedOffsets.Contains(virtualNode.ByteOffset))
        {
            virtualNode.IsLoadFailed = true;
            return;
        }
        _loadInProgress = true;
        virtualNode.IsLoading = true;
        var loadSucceeded = false;
        try
        {
            var parent   = virtualNode.Parent;
            if (parent is null) return;
            var isArray  = parent is JsonArrayNodeModel;

            var (batch, nextOffset) = await _loader.LoadVirtualChunkAsync(
                FilePath, virtualNode.ByteOffset, virtualNode.EndOffset, isArray, CancellationToken.None);
            if (batch.Count == 0)
            {
                RecordFailure(virtualNode.ByteOffset);
                virtualNode.IsLoadFailed = true;
                return;
            }

            AbsorbBatchIntoWindow(virtualNode, batch, item, nextOffset);
            loadSucceeded = true;
        }
        catch (Exception ex)
        {
            RecordFailure(virtualNode.ByteOffset, ex);
            virtualNode.IsLoadFailed = true;
        }
        finally
        {
            virtualNode.IsLoading = false;
            _loadInProgress = false;
            // Only chain the next load when we made progress; otherwise stop so we
            // don't reschedule into the same failing offset forever.
            if (loadSucceeded) ScheduleNextLoadIfNeeded();
        }
    }

    // Loads a batch starting at a known or estimated root-child index.
    // Used for: pre-populated placeholder rows that have no sentinel node.
    private async Task LoadFromIndexAsync(int requestedIndex)
    {
        _loadInProgress = true;
        var loadSucceeded = false;
        long offset = 0;
        try
        {
            var isArray = Root is JsonArrayNodeModel;

            // CRITICAL: use the index that corresponds to the actual offset we'll
            // read from, not the requested index. GetBestOffsetWithIndex returns
            // (actualIdx, offset) where actualIdx <= requestedIndex; parsing yields
            // items starting at actualIdx, so that's where they must be absorbed.
            int actualIdx;
            (actualIdx, offset) = GetBestOffsetWithIndex(requestedIndex);

            if (_failedOffsets.Contains(offset)) return;

            var (batch, nextOffset) = await _loader.LoadVirtualChunkAsync(
                FilePath, offset, _fileSize, isArray, CancellationToken.None);
            if (batch.Count == 0)
            {
                RecordFailure(offset);
                return;
            }

            AbsorbBatchFromIndex(actualIdx, batch, nextOffset);
            loadSucceeded = true;
        }
        catch (Exception ex)
        {
            RecordFailure(offset, ex);
        }
        finally
        {
            _loadInProgress = false;
            if (loadSucceeded) ScheduleNextLoadIfNeeded();
        }
    }

    private void RecordFailure(long offset, Exception? ex = null)
    {
        _failedOffsets.Add(offset);
        if (_hasReportedLoadError) return;
        _hasReportedLoadError = true;
        var detail = ex is null ? string.Empty : $" ({ex.GetType().Name}: {ex.Message})";
        _shellServices.ShowError(
            $"Failed to load part of '{FileName}' at byte offset {offset}. " +
            $"Some sections of the file may not display.{detail}");
    }

    // Replaces the sentinel's display item with real tree items and a new sentinel (if more remain).
    private void AbsorbBatchIntoWindow(
        VirtualJsonNodeModel           sentinel,
        List<(string? key, JsonNode? node)> batch,
        JsonVirtualDisplayItem         displayItem,
        long                           nextOffset)
    {
        var parent   = sentinel.Parent;
        if (parent is null) return;
        var siblings = GetChildren(parent);
        if (siblings is null) return;

        var sibIdx     = siblings.IndexOf(sentinel);
        if (sibIdx < 0) return;
        siblings.RemoveAt(sibIdx);

        var startIndex = sentinel.Index ?? _loadWindowEnd;
        var newNodes   = BuildBatchNodes(batch, parent, startIndex);

        foreach (var node in newNodes)
            siblings.Add(node);

        VirtualJsonNodeModel? nextSentinel = null;
        if (nextOffset < _fileSize - 10)
        {
            nextSentinel = new VirtualJsonNodeModel
            {
                Parent     = parent,
                Index      = startIndex + batch.Count,
                ByteOffset = nextOffset,
                EndOffset  = _fileSize,
            };
            siblings.Add(nextSentinel);
        }

        // Replace the sentinel display item + any matching pre-populated placeholders
        var dispIdx = DisplayItems.IndexOf(displayItem);
        if (dispIdx >= 0) DisplayItems.RemoveAt(dispIdx);
        else dispIdx = FindInsertIndexForBatch(startIndex);

        foreach (var node in newNodes)
            dispIdx = InsertNodeItem(dispIdx, node, displayItem.Depth);

        // The pre-populated placeholder at positions [startIndex..startIndex+batch.Count) can now
        // be removed since real tree items occupy those positions.
        RemovePrePopPlaceholders(startIndex, startIndex + batch.Count);

        if (nextSentinel is not null)
            DisplayItems.Insert(dispIdx, new JsonVirtualDisplayItem { Node = nextSentinel, Depth = displayItem.Depth });

        _byteOffsetIndex[startIndex]              = sentinel.ByteOffset;
        _byteOffsetIndex[startIndex + batch.Count] = nextOffset;
        _loadWindowEnd = Math.Max(_loadWindowEnd, startIndex + batch.Count);

        OnPropertyChanged(nameof(HasVirtualItems));
        MaybeUnloadAbove();
    }

    // Absorbs a batch loaded at a specific index (no sentinel—uses pre-populated placeholders).
    private void AbsorbBatchFromIndex(
        int                            startIndex,
        List<(string? key, JsonNode? node)> batch,
        long                           nextOffset)
    {
        var parent = Root;
        if (parent is null) return;
        var siblings = GetChildren(parent);
        if (siblings is null) return;

        var newNodes = BuildBatchNodes(batch, parent, startIndex);

        // Insert new nodes into the model's children at the right position.
        // For the forward case (startIndex == _loadWindowEnd), we append before any trailing sentinel.
        // For the backward case (startIndex < _loadWindowStart), we prepend.
        if (startIndex >= _loadWindowEnd)
        {
            // Remove trailing VirtualJsonNodeModel sentinel if present
            if (siblings.LastOrDefault() is VirtualJsonNodeModel oldSentinel)
                siblings.Remove(oldSentinel);

            foreach (var node in newNodes) siblings.Add(node);

            if (nextOffset < _fileSize - 10)
                siblings.Add(new VirtualJsonNodeModel
                {
                    Parent     = parent,
                    Index      = startIndex + batch.Count,
                    ByteOffset = nextOffset,
                    EndOffset  = _fileSize,
                });

            _loadWindowEnd = startIndex + batch.Count;
        }
        else if (startIndex < _loadWindowStart)
        {
            // Backward: prepend new nodes before the leading sentinel if any
            var leadSentinel = siblings.FirstOrDefault() as VirtualJsonNodeModel;
            var insertAt     = leadSentinel is not null ? 1 : 0;
            for (var i = newNodes.Count - 1; i >= 0; i--)
                siblings.Insert(insertAt, newNodes[i]);
            if (leadSentinel is not null) siblings.RemoveAt(0);

            _loadWindowStart = startIndex;
        }

        // Replace pre-populated virtual placeholders in the display list
        var dispInsert = FindInsertIndexForBatch(startIndex);
        foreach (var (node, i) in newNodes.Select((n, idx) => (n, startIndex + idx)))
        {
            RemovePrePopPlaceholders(i, i + 1);
            dispInsert = InsertNodeItem(dispInsert, node, 1);
        }

        _byteOffsetIndex[startIndex]              = GetBestOffset(startIndex);
        _byteOffsetIndex[startIndex + batch.Count] = nextOffset;

        OnPropertyChanged(nameof(HasVirtualItems));
        MaybeUnloadAbove();
    }

    // ── Window helpers ────────────────────────────────────────────────────────

    private List<JsonNodeModel> BuildBatchNodes(
        List<(string? key, JsonNode? node)> batch,
        JsonNodeModel parent, int startIndex)
    {
        var isArray = parent is JsonArrayNodeModel;
        return batch.Select((b, i) =>
        {
            var nc = 0;
            return JsonFileLoader.BuildModelFromJsonNode(
                b.node, parent, b.key,
                isArray ? startIndex + i : (int?)null,
                ref nc);
        }).ToList();
    }

    private void MaybeUnloadAbove()
    {
        var loaded = _loadWindowEnd - _loadWindowStart;
        if (loaded <= MaxWindowSize) return;

        // Viewport-aware: unload from the side that's farthest from where the user is
        // looking. If we always unload from the top, scrolling back up would discard
        // freshly-loaded items immediately. The viewport's approximate root-child
        // index tells us which side is "far".
        var viewportIdx = GetViewportRootChildIndex();
        if (viewportIdx < 0)
        {
            var unloadUpTo = _loadWindowStart + UnloadBatchSize;
            UnloadRange(_loadWindowStart, unloadUpTo);
            return;
        }

        var distToTop    = viewportIdx - _loadWindowStart;
        var distToBottom = _loadWindowEnd - viewportIdx;

        if (distToTop >= distToBottom)
            UnloadRange(_loadWindowStart, _loadWindowStart + UnloadBatchSize);
        else
            UnloadTailRange(_loadWindowEnd - UnloadBatchSize, _loadWindowEnd);
    }

    /// <summary>Find the root-child index of the first loaded depth-1 item in the viewport.</summary>
    private int GetViewportRootChildIndex()
    {
        var start = Math.Max(0, _firstVisibleDisplayIdx);
        var end   = Math.Min(DisplayItems.Count - 1, _lastVisibleDisplayIdx);
        for (var i = start; i <= end; i++)
        {
            var item = DisplayItems[i];
            if (item.Depth != 1) continue;
            switch (item)
            {
                case JsonVirtualDisplayItem vdi when vdi.RootChildIndex >= 0:
                    return vdi.RootChildIndex;
                case JsonTreeDisplayItem tdi when tdi.Node is not null && tdi.Node.Parent == Root:
                    return GetRootChildIndex(tdi.Node);
                case JsonTableRowDisplayItem trd when trd.Node is not null && trd.Node.Parent == Root:
                    return GetRootChildIndex(trd.Node);
            }
        }
        return -1;
    }

    // Unloads the high end of the window (when the user is far from the bottom).
    private void UnloadTailRange(int fromIndex, int toIndexExclusive)
    {
        var parent = Root;
        if (parent is null) return;
        var siblings = GetChildren(parent);
        if (siblings is null) return;

        for (var idx = toIndexExclusive - 1; idx >= fromIndex; idx--)
        {
            var sibPos = idx - _loadWindowStart;
            if (sibPos < 0 || sibPos >= siblings.Count) continue;

            var child = siblings[sibPos];
            if (child is VirtualJsonNodeModel) continue;

            // Collapse any expanded subtree first
            if (_nodeItems.TryGetValue(child, out var treeItem) && treeItem.IsExpanded)
                CollapseNode(child, treeItem);

            // Replace tree/table item with a virtual placeholder
            JsonDisplayItem? existing = null;
            if (_nodeItems.TryGetValue(child, out treeItem))
                existing = treeItem;
            else if (_isRootTableMode)
            {
                for (var di = 0; di < DisplayItems.Count; di++)
                    if (DisplayItems[di] is JsonTableRowDisplayItem row && row.Node == child)
                    { existing = DisplayItems[di]; break; }
            }

            if (existing is not null)
            {
                var dispIdx = DisplayItems.IndexOf(existing);
                if (dispIdx >= 0)
                {
                    DisplayItems[dispIdx] = new JsonVirtualDisplayItem
                    {
                        Depth          = 1,
                        RootChildIndex = idx,
                    };
                }
                if (treeItem is not null) _nodeItems.Remove(child);
            }
        }

        // Remove the model nodes from the tail; replace with a trailing sentinel
        // so future scrolling forward knows where to resume.
        var countToRemove = toIndexExclusive - fromIndex;
        var endByte = _byteOffsetIndex.GetValueOrDefault(toIndexExclusive, _fileSize);

        // Drop trailing real model nodes from the tail
        for (var i = 0; i < countToRemove && siblings.Count > 0; i++)
        {
            var last = siblings[^1];
            if (last is VirtualJsonNodeModel) { siblings.RemoveAt(siblings.Count - 1); continue; }
            siblings.RemoveAt(siblings.Count - 1);
        }

        // Re-add a trailing sentinel so future forward loads can resume from endByte
        if (endByte < _fileSize - 2)
        {
            siblings.Add(new VirtualJsonNodeModel
            {
                Parent     = parent,
                Index      = fromIndex,
                ByteOffset = endByte,
                EndOffset  = _fileSize,
            });
        }

        _loadWindowEnd = fromIndex;
    }

    private void UnloadRange(int fromIndex, int toIndexExclusive)
    {
        var parent = Root;
        if (parent is null) return;
        var siblings = GetChildren(parent);
        if (siblings is null) return;

        // Collapse and replace tree display items with pre-populated virtual placeholders
        for (var idx = fromIndex; idx < toIndexExclusive; idx++)
        {
            // The real node for depth-1 index `idx` is at position (idx - _loadWindowStart)
            // in siblings (assuming no leading sentinel yet).
            var sibPos = idx - _loadWindowStart;
            if (sibPos < 0 || sibPos >= siblings.Count) break;

            var child = siblings[sibPos];
            if (child is VirtualJsonNodeModel) continue;

            // Collapse if expanded (removes children from display list)
            if (_nodeItems.TryGetValue(child, out var treeItem) && treeItem.IsExpanded)
                CollapseNode(child, treeItem);

            // Replace the tree display item with a pre-populated virtual placeholder
            if (_nodeItems.TryGetValue(child, out treeItem))
            {
                var dispIdx = DisplayItems.IndexOf(treeItem);
                if (dispIdx >= 0)
                {
                    DisplayItems[dispIdx] = new JsonVirtualDisplayItem
                    {
                        Depth          = 1,
                        RootChildIndex = idx,
                    };
                }
                _nodeItems.Remove(child);
            }
            else if (_isRootTableMode)
            {
                // In root table mode, the depth-1 item is a JsonTableRowDisplayItem
                // (not tracked in _nodeItems). Find it by node identity.
                for (var di = 0; di < DisplayItems.Count; di++)
                {
                    if (DisplayItems[di] is JsonTableRowDisplayItem row && row.Node == child)
                    {
                        DisplayItems[di] = new JsonVirtualDisplayItem
                        {
                            Depth          = 1,
                            RootChildIndex = idx,
                        };
                        break;
                    }
                }
            }
        }

        // Remove the model nodes from the front of siblings, replacing with a sentinel
        var countToRemove = toIndexExclusive - fromIndex;
        // Record byte offset before removing
        var startByte = _byteOffsetIndex.GetValueOrDefault(fromIndex, (long)(fromIndex * _avgItemBytes));

        for (var i = 0; i < countToRemove && siblings.Count > 0; i++)
            if (siblings[0] is not VirtualJsonNodeModel)
                siblings.RemoveAt(0);

        // Insert a leading sentinel so HasVirtualNodes detects the gap (prevents save)
        siblings.Insert(0, new VirtualJsonNodeModel
        {
            Parent             = parent,
            ByteOffset         = startByte,
            EndOffset          = _fileSize,
            EstimatedItemCount = countToRemove,
        });

        _loadWindowStart = toIndexExclusive;
    }

    // ── Byte offset lookup ────────────────────────────────────────────────────

    private long GetBestOffset(int index) => GetBestOffsetWithIndex(index).offset;

    /// <summary>
    /// Returns the largest known (index, offset) pair with index ≤ requested.
    /// If none is known, returns (0, linearEstimate). The actual index matters
    /// because parsing starts at that index, not the requested one.
    /// </summary>
    private (int idx, long offset) GetBestOffsetWithIndex(int requestedIndex)
    {
        for (var i = _byteOffsetIndex.Count - 1; i >= 0; i--)
        {
            if (_byteOffsetIndex.Keys[i] <= requestedIndex)
                return (_byteOffsetIndex.Keys[i], _byteOffsetIndex.Values[i]);
        }
        return (0, (long)(requestedIndex * _avgItemBytes));
    }

    // ── Display list position helpers ─────────────────────────────────────────

    private int CountDepth1InDisplay()
        => DisplayItems.Count(i => i.Depth == 1);

    private int FindInsertIndexAfterLastDepth1()
    {
        for (var i = DisplayItems.Count - 1; i >= 0; i--)
            if (DisplayItems[i].Depth == 1) return i + 1;
        return DisplayItems.Count;
    }

    private void RemovePrePopPlaceholders(int fromRootChildIdx, int toRootChildIdxExclusive)
    {
        for (var i = DisplayItems.Count - 1; i >= 0; i--)
        {
            if (DisplayItems[i] is JsonVirtualDisplayItem vdi
                && vdi.RootChildIndex >= fromRootChildIdx
                && vdi.RootChildIndex < toRootChildIdxExclusive)
            {
                DisplayItems.RemoveAt(i);
            }
        }
    }

    private int FindInsertIndexForBatch(int rootChildIndex)
    {
        // Find the display index just before root child rootChildIndex
        for (var i = 0; i < DisplayItems.Count; i++)
        {
            var item = DisplayItems[i];
            if (item.Depth != 1) continue;
            if (item is JsonTableHeaderDisplayItem) continue;

            int itemRootIdx = item switch
            {
                JsonTreeDisplayItem     tdi => GetRootChildIndex(tdi.Node),
                JsonTableRowDisplayItem trd => GetRootChildIndex(trd.Node),
                JsonVirtualDisplayItem  vdi when vdi.RootChildIndex >= 0 => vdi.RootChildIndex,
                _ => -1,
            };

            if (itemRootIdx >= rootChildIndex) return i;
        }
        return FindInsertIndexAfterLastDepth1();
    }

    private int GetRootChildIndex(JsonNodeModel node)
    {
        var children = GetChildren(Root);
        if (children is null) return -1;
        var i = 0;
        foreach (var child in children)
        {
            if (child == node) return _loadWindowStart + i;
            if (child is not VirtualJsonNodeModel) i++;
        }
        return -1;
    }

    // ── Display list management ───────────────────────────────────────────────

    private int InsertNodeItem(int insertAt, JsonNodeModel node, int depth)
    {
        // In root table mode, depth-1 children render as table rows instead of tree rows.
        if (_isRootTableMode && depth == 1 && node.Parent == Root)
        {
            DisplayItems.Insert(insertAt, BuildTableRow(node));
            return insertAt + 1;
        }

        var item = new JsonTreeDisplayItem
        {
            Node        = node,
            Depth       = depth,
            HasChildren = HasChildNodes(node),
            IsExpanded  = false,
            KeyLabel    = BuildKeyLabel(node),
            TypeLabel   = BuildTypeLabel(node),
            IsValue     = node is JsonValueNodeModel,
            ValueKind   = (node as JsonValueNodeModel)?.ValueKind ?? JsonValueKind.Undefined,
        };
        DisplayItems.Insert(insertAt, item);
        _nodeItems[node] = item;
        return insertAt + 1;
    }

    [RelayCommand]
    private void ToggleExpand(JsonNodeModel node)
    {
        if (!_nodeItems.TryGetValue(node, out var item)) return;
        if (item.IsExpanded) CollapseNode(node, item);
        else                 ExpandNode(node, item);
    }

    private void ExpandNode(JsonNodeModel node, JsonTreeDisplayItem? item = null)
    {
        item ??= _nodeItems.GetValueOrDefault(node);
        if (item is null || item.IsExpanded) return;
        item.IsExpanded = true;
        var insertAt = DisplayItems.IndexOf(item) + 1;
        foreach (var child in GetChildren(node) ?? [])
        {
            if (child is VirtualJsonNodeModel v)
            {
                DisplayItems.Insert(insertAt, new JsonVirtualDisplayItem { Node = v, Depth = item.Depth + 1 });
                insertAt++;
            }
            else
            {
                insertAt = InsertNodeItem(insertAt, child, item.Depth + 1);
            }
        }
    }

    private void CollapseNode(JsonNodeModel node, JsonTreeDisplayItem? item = null)
    {
        item ??= _nodeItems.GetValueOrDefault(node);
        if (item is null || !item.IsExpanded) return;
        item.IsExpanded = false;
        var startIdx = DisplayItems.IndexOf(item) + 1;
        while (startIdx < DisplayItems.Count && DisplayItems[startIdx].Depth > item.Depth)
        {
            var removed = DisplayItems[startIdx];
            DisplayItems.RemoveAt(startIdx);
            if (removed.Node is not null) _nodeItems.Remove(removed.Node);
        }
    }

    private void CollapseSubtreeFromDisplay(JsonNodeModel node)
    {
        if (!_nodeItems.TryGetValue(node, out var item)) return;
        if (!item.IsExpanded) return;
        item.IsExpanded = false;
        var startIdx = DisplayItems.IndexOf(item) + 1;
        while (startIdx < DisplayItems.Count && DisplayItems[startIdx].Depth > item.Depth)
        {
            var removed = DisplayItems[startIdx];
            DisplayItems.RemoveAt(startIdx);
            if (removed.Node is not null) _nodeItems.Remove(removed.Node);
        }
    }

    private void ExpandAncestorsOf(JsonNodeModel node)
    {
        var ancestors = new List<JsonNodeModel>();
        var current   = node.Parent;
        while (current is not null) { ancestors.Add(current); current = current.Parent; }
        ancestors.Reverse();
        foreach (var a in ancestors) ExpandNode(a);
    }

    // ── View mode commands ────────────────────────────────────────────────────

    [RelayCommand] private void SetTreeMode()  => SetViewMode(NodeViewMode.Tree);
    [RelayCommand] private void SetTextMode()  => SetViewMode(NodeViewMode.Text);
    [RelayCommand] private void SetTableMode() => SetViewMode(NodeViewMode.Table);

    private void SetViewMode(NodeViewMode mode)
    {
        var node = SelectedNode;
        if (node is null) return;

        if (mode == NodeViewMode.Table && node is not JsonArrayNodeModel)
            return;

        // If we're in root table mode and the user is switching the root's mode,
        // exit table mode first (restores tree rows for depth 1).
        if (_isRootTableMode && (node == Root || mode != NodeViewMode.Table))
            ExitRootTableMode();

        if (_activeInlineItem is not null)
        {
            DisplayItems.Remove(_activeInlineItem);
            _activeInlineItem = null;
        }

        if (mode == NodeViewMode.Tree)
        {
            if (_nodeItems.TryGetValue(node, out var treeItem))
            {
                treeItem.IsExpanded = false;
                ExpandNode(node, treeItem);
            }
            RaiseViewModeProperties();
            return;
        }

        // Root-array Table mode → render rows inside the outer DisplayList so
        // scroll/load-on-scroll behaves identically to tree mode.
        if (mode == NodeViewMode.Table && node == Root && node is JsonArrayNodeModel rootArr)
        {
            EnterRootTableMode(rootArr);
            RaiseViewModeProperties();
            return;
        }

        CollapseSubtreeFromDisplay(node);

        var rawJson = mode == NodeViewMode.Text ? JsonNodeSerializer.Serialize(node) : string.Empty;
        _activeInlineItem = new JsonInlineContentDisplayItem
        {
            Node     = node,
            Depth    = _nodeItems.TryGetValue(node, out var di) ? di.Depth : 0,
            ViewMode = mode,
            RawJson  = rawJson,
        };

        if (_nodeItems.TryGetValue(node, out var ni))
            DisplayItems.Insert(DisplayItems.IndexOf(ni) + 1, _activeInlineItem);

        RaiseViewModeProperties();

        // Scroll the inline view into the viewport (otherwise it lands just below
        // the selected row and is easy to miss when near the bottom of the list).
        ScrollToItemRequested?.Invoke(this, _activeInlineItem);
    }

    // ── Root table mode helpers ──────────────────────────────────────────────

    private void EnterRootTableMode(JsonArrayNodeModel rootArr)
    {
        // Collect column keys from currently loaded object children (first-appearance order).
        var keys = new List<string>();
        var seen = new HashSet<string>();
        foreach (var obj in rootArr.Children.OfType<JsonObjectNodeModel>())
            foreach (var child in obj.Children)
                if (child.Key is not null && seen.Add(child.Key))
                    keys.Add(child.Key);
        if (keys.Count == 0) return;

        _tableColumns = keys;
        _isRootTableMode = true;

        // Replace each depth-1 tree row with a table row. Also collapse any
        // expanded subtrees (depth > 1 entries are removed). Insert a header
        // before the first depth-1 entry.
        var rootIdx = _nodeItems.TryGetValue(rootArr, out var rootItem)
            ? DisplayItems.IndexOf(rootItem)
            : -1;
        if (rootIdx < 0) return;

        // Remove any items deeper than depth 1 (collapsed subtrees) and replace
        // depth-1 tree rows with table rows.
        for (var i = DisplayItems.Count - 1; i > rootIdx; i--)
        {
            var item = DisplayItems[i];
            if (item.Depth > 1)
            {
                if (item.Node is not null) _nodeItems.Remove(item.Node);
                DisplayItems.RemoveAt(i);
            }
            else if (item is JsonTreeDisplayItem tdi && tdi.Depth == 1)
            {
                _nodeItems.Remove(tdi.Node);
                DisplayItems[i] = BuildTableRow(tdi.Node);
            }
        }

        // Insert header row immediately after the root.
        DisplayItems.Insert(rootIdx + 1, new JsonTableHeaderDisplayItem
        {
            Depth   = 1,
            Columns = _tableColumns,
        });
    }

    private void ExitRootTableMode()
    {
        _isRootTableMode = false;

        var rootIdx = Root is not null && _nodeItems.TryGetValue(Root, out var rootItem)
            ? DisplayItems.IndexOf(rootItem)
            : -1;
        if (rootIdx < 0) { _tableColumns = []; return; }

        // Strip header + all table rows from depth 1.
        for (var i = DisplayItems.Count - 1; i > rootIdx; i--)
        {
            if (DisplayItems[i] is JsonTableHeaderDisplayItem ||
                DisplayItems[i] is JsonTableRowDisplayItem)
                DisplayItems.RemoveAt(i);
        }

        // Reset the root's IsExpanded so ExpandNode rebuilds depth-1 from the
        // model (which still holds the loaded children + virtual sentinels).
        if (Root is not null && _nodeItems.TryGetValue(Root, out var ri))
        {
            ri.IsExpanded = false;
            ExpandNode(Root, ri);
        }

        _tableColumns = [];
    }

    private JsonTableRowDisplayItem BuildTableRow(JsonNodeModel node)
    {
        var values = new string[_tableColumns.Count];
        if (node is JsonObjectNodeModel obj)
        {
            var byKey = obj.Children
                .Where(c => c.Key is not null)
                .ToDictionary(c => c.Key!, c => c);
            for (var i = 0; i < _tableColumns.Count; i++)
            {
                values[i] = byKey.TryGetValue(_tableColumns[i], out var c)
                    ? c switch
                    {
                        JsonValueNodeModel  v => v.DisplayValue,
                        JsonObjectNodeModel o => $"{{ {o.Children.Count} }}",
                        JsonArrayNodeModel  a => $"[ {a.Children.Count} ]",
                        _                      => string.Empty,
                    }
                    : string.Empty;
            }
        }
        else
        {
            // Non-object: show in column 0
            for (var i = 0; i < values.Length; i++) values[i] = string.Empty;
            if (values.Length > 0)
                values[0] = node switch
                {
                    JsonValueNodeModel  v => v.DisplayValue,
                    JsonArrayNodeModel  a => $"[ {a.Children.Count} ]",
                    _                      => string.Empty,
                };
        }

        return new JsonTableRowDisplayItem
        {
            Node    = node,
            Depth   = 1,
            Columns = _tableColumns,
            Values  = values,
        };
    }

    private void RaiseViewModeProperties()
    {
        OnPropertyChanged(nameof(IsTreeModeActive));
        OnPropertyChanged(nameof(IsTextModeActive));
        OnPropertyChanged(nameof(IsTableModeActive));
    }

    // ── Save & Format ─────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!IsModified || Root is null) return;
        if (HasVirtualNodes(Root))
        {
            _shellServices.ShowError("Cannot save: file has unloaded sections. Expand all nodes first.");
            return;
        }
        try
        {
            var json = JsonNodeSerializer.Serialize(Root);
            await Task.Run(() => VirtualFileSystem.Instance.WriteAllText(FilePath, json, Encoding.UTF8));
            IsModified = false;
        }
        catch (Exception ex) { _shellServices.ShowError($"Save failed: {ex.Message}"); }
    }

    [RelayCommand]
    private void FormatJson()
    {
        if (Root is null) return;
        if (HasVirtualNodes(Root)) { _shellServices.ShowError("Cannot format: file has unloaded sections."); return; }
        var formatted = JsonNodeSerializer.Serialize(Root, indented: true);
        try
        {
            var parsed    = JsonNode.Parse(formatted);
            var nodeCount = 0;
            Root      = JsonFileLoader.BuildModelFromJsonNode(parsed, null, null, null, ref nodeCount);
            NodeCount = nodeCount;
            RebuildDisplayList();
            if (Root is not null) ExpandNode(Root);
            IsModified = true;
        }
        catch { /* ignore */ }
    }

    // ── Commit raw JSON from AvalonEdit ───────────────────────────────────────

    internal void CommitRawJson(JsonNodeModel node, string rawText)
    {
        try
        {
            var parsed = JsonNode.Parse(rawText);
            ReplaceChildren(node, parsed!);
            IsModified = true;
        }
        catch (JsonException) { /* invalid JSON — keep as-is */ }
    }

    private static void ReplaceChildren(JsonNodeModel node, JsonNode parsed)
    {
        var nodeCount = 0;
        switch (node)
        {
            case JsonObjectNodeModel obj when parsed is JsonObject parsedObj:
                obj.Children.Clear();
                foreach (var (k, v) in parsedObj)
                    obj.Children.Add(JsonFileLoader.BuildModelFromJsonNode(v, obj, k, null, ref nodeCount));
                break;
            case JsonArrayNodeModel arr when parsed is JsonArray parsedArr:
                arr.Children.Clear();
                for (var i = 0; i < parsedArr.Count; i++)
                    arr.Children.Add(JsonFileLoader.BuildModelFromJsonNode(parsedArr[i], arr, null, i, ref nodeCount));
                break;
        }
    }

    // ── Drag reorder ──────────────────────────────────────────────────────────

    internal void MoveNode(JsonNodeModel dragged, JsonNodeModel dropTarget, bool insertBefore)
    {
        if (dragged == dropTarget) return;
        if (dragged.Parent is null || dragged.Parent != dropTarget.Parent) return;

        var siblings = GetChildren(dragged.Parent);
        if (siblings is null) return;

        siblings.Remove(dragged);
        var targetIdx = siblings.IndexOf(dropTarget);
        siblings.Insert(insertBefore ? targetIdx : targetIdx + 1, dragged);
        ReindexArrayChildren(dragged.Parent);
        IsModified = true;
        RebuildSubtree(dragged.Parent);
    }

    private void RebuildSubtree(JsonNodeModel parent)
    {
        if (!_nodeItems.TryGetValue(parent, out var parentItem)) return;
        var wasExpanded = parentItem.IsExpanded;
        CollapseNode(parent, parentItem);
        if (wasExpanded) ExpandNode(parent, parentItem);
    }

    // ── JSONPath ──────────────────────────────────────────────────────────────

    public void EvaluateJsonPath(string jsonPath)
    {
        if (Root is null) return;
        var matches = _evaluator.Evaluate(jsonPath, Root);
        if (matches.Count == 0) { _shellServices.ShowNotification($"No matches for: {jsonPath}"); return; }
        ExpandAncestorsOf(matches[0]);
        if (_nodeItems.TryGetValue(matches[0], out var item))
        {
            SelectedDisplayItem = item;
            ScrollToItemRequested?.Invoke(this, item);
            return;
        }
        // The node exists in the model but has no display item — it lives outside the loaded window.
        // Saying so beats the silent no-op this used to be.
        _shellServices.ShowNotification(
            $"'{jsonPath}' is outside the loaded part of this file — scroll toward it and try again.");
    }

    // ── Breadcrumb navigation ─────────────────────────────────────────────────

    internal void SelectAndScrollToNode(JsonNodeModel node)
    {
        ExpandAncestorsOf(node);
        if (_nodeItems.TryGetValue(node, out var item))
        {
            SelectedDisplayItem = item;
            ScrollToItemRequested?.Invoke(this, item);
        }
    }

    private void RebuildBreadcrumbs(JsonNodeModel? node)
    {
        SelectedNodeBreadcrumbs.Clear();
        if (node is null) return;
        var chain = new List<JsonNodeModel>();
        for (var n = node; n is not null; n = n.Parent) chain.Add(n);
        chain.Reverse();
        foreach (var n in chain)
        {
            var captured = n;
            SelectedNodeBreadcrumbs.Add(new JsonBreadcrumbItem
            {
                Label           = captured.DisplayKey,
                Node            = captured,
                NavigateCommand = new RelayCommand(() => SelectAndScrollToNode(captured)),
            });
        }
    }

    // ── IPageViewModel ────────────────────────────────────────────────────────

    public string GetContext()
    {
        if (string.IsNullOrEmpty(FilePath)) return "JSON viewer: no file loaded.";
        var rootKind = Root switch
        {
            JsonObjectNodeModel o => $"an object with {o.Children.Count} propert{(o.Children.Count == 1 ? "y" : "ies")}",
            JsonArrayNodeModel a  => $"an array with {a.Children.Count} item(s)" + (IsLargeFile ? ", loaded in a window" : ""),
            _                     => "a scalar",
        };
        var mode  = IsTableModeActive ? "table" : IsTextModeActive ? "raw-text" : "tree";
        var sel   = SelectedNode?.GetJsonPath() ?? "none";
        var dirty = IsModified ? " (unsaved changes)" : string.Empty;
        return $"JSON file '{FileName}' ({FileSizeText}, {NodeCount} nodes){dirty}. Root is {rootKind}. "
             + $"Showing the {mode} view; selected node: {sel}.\n"
             + "Use query_json_path to read a value at any path, or read_json for the whole (loaded) document.";
    }

    public string? GetSecurityContext() => FilePath;

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        // ── Read / explore (auto-run) — the data the model couldn't reach before ──
        new DelegateClientTool(
            "query_json_path",
            "Evaluate a JSONPath (e.g. $.store.book[0].title) against the loaded document and return the matching "
          + "value(s) as JSON — the way to read data out of the file.",
            [new ClientToolParameter("path", "A JSONPath expression.")],
            ToolSafety.SafeOperation,
            (args, _) =>
            {
                if (Root is null) return Task.FromResult(ToolResult.Error("No JSON is loaded."));
                var path = ToolArgs.Str(args, "path", "jsonpath", "query");
                if (string.IsNullOrEmpty(path)) return Task.FromResult(ToolResult.Error("No 'path' provided."));
                var matches = _evaluator.Evaluate(path, Root);
                if (matches.Count == 0)
                    return Task.FromResult(ToolResult.Ok($"no match for {path}", $"No node matched JSONPath '{path}'."));
                var body = matches.Count == 1
                    ? JsonNodeSerializer.Serialize(matches[0], indented: true)
                    : "[\n" + string.Join(",\n", matches.Take(50).Select(m => JsonNodeSerializer.Serialize(m, indented: true))) + "\n]";
                var note = matches.Count > 50 ? $"\n({matches.Count} matches; showing the first 50.)"
                         : matches.Count > 1  ? $"\n({matches.Count} matches.)" : string.Empty;
                return Task.FromResult(ToolResult.Ok($"{matches.Count} match(es) for {path}", body + note));
            }),

        new DelegateClientTool(
            "read_json",
            "Return the full JSON document (reflects unsaved edits). For a large windowed file this returns only the "
          + "portion currently loaded.",
            [],
            ToolSafety.SafeOperation,
            (_, _) =>
            {
                if (Root is null) return Task.FromResult(ToolResult.Error("No JSON is loaded."));
                var partial = HasVirtualNodes(Root);
                var body = JsonNodeSerializer.Serialize(Root, indented: true);
                var note = partial ? "\n(This file is loaded in a window — unloaded sections are omitted.)" : string.Empty;
                return Task.FromResult(ToolResult.Ok(partial ? "read (partial)" : "read document", body + note));
            }),

        // ── Mutating (safe — re-indent only; approval not required for a reversible reflow) ──
        new DelegateClientTool(
            "format_json",
            "Re-indent the JSON document.",
            [],
            ToolSafety.SafeOperation,
            (arguments, ct) =>
            {
                FormatJsonCommand.Execute(null);
                return Task.FromResult(ToolResult.Ok("formatted", "Re-indented the JSON document."));
            })
    ];

    public IContext? GetContextObject()
    {
        if (string.IsNullOrEmpty(FilePath)) return null;
        var dir = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(dir)) return null;
        return new FileSystemContext { RootPath = dir, CurrentPath = dir, SelectedItems = [FilePath] };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ObservableCollection<JsonNodeModel>? GetChildren(JsonNodeModel? node)
        => node switch
        {
            JsonObjectNodeModel obj => obj.Children,
            JsonArrayNodeModel  arr => arr.Children,
            _                      => null,
        };

    private static bool HasChildNodes(JsonNodeModel node)
        => node switch
        {
            JsonObjectNodeModel obj => obj.Children.Count > 0,
            JsonArrayNodeModel  arr => arr.Children.Count > 0,
            _                      => false,
        };

    private static bool HasVirtualNodes(JsonNodeModel node)
    {
        if (node is VirtualJsonNodeModel) return true;
        if (node is JsonObjectNodeModel obj) return obj.Children.Any(HasVirtualNodes);
        if (node is JsonArrayNodeModel  arr) return arr.Children.Any(HasVirtualNodes);
        return false;
    }

    private static void ReindexArrayChildren(JsonNodeModel parent)
    {
        if (parent is not JsonArrayNodeModel arr) return;
        for (var i = 0; i < arr.Children.Count; i++) arr.Children[i].Index = i;
    }

    private static string BuildKeyLabel(JsonNodeModel node)
    {
        if (node.Key   is not null) return $"\"{node.Key}\": ";
        if (node.Index is not null) return $"[{node.Index}] ";
        return string.Empty;
    }

    private static string BuildTypeLabel(JsonNodeModel node)
        => node switch
        {
            JsonObjectNodeModel obj => $"{{  }}  {obj.Children.Count} properties",
            JsonArrayNodeModel  arr => $"[  ]  {arr.Children.Count} items",
            JsonValueNodeModel  val => val.DisplayValue,
            VirtualJsonNodeModel    => "…",
            _                       => string.Empty,
        };

    private void RebuildDisplayList()
    {
        DisplayItems.Clear();
        _nodeItems.Clear();
        _activeInlineItem = null;
        _byteOffsetIndex.Clear();
        _loadWindowStart = 0;
        _loadWindowEnd   = 0;
        if (Root is null) return;
        InsertNodeItem(0, Root, depth: 0);
    }

    private static string FormatSize(long bytes) => SizeFormatter.FormatBytes(bytes);

    public void Dispose()
    {
        _loadCts?.Cancel();
    }
}
