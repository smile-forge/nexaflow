using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Json.Models;
using Nexaflow.Features.Json.Services;
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

    // O(1) lookup from model node to its display item (real nodes only, not virtual)
    private readonly Dictionary<JsonNodeModel, JsonTreeDisplayItem> _nodeItems = [];
    private JsonInlineContentDisplayItem? _activeInlineItem;
    private CancellationTokenSource? _loadCts;

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

    public ObservableCollection<JsonDisplayItem>    DisplayItems            { get; } = [];
    public ObservableCollection<JsonBreadcrumbItem> SelectedNodeBreadcrumbs { get; } = [];

    /// <summary>True when the display list contains at least one unloaded virtual sentinel.</summary>
    public bool HasVirtualItems => DisplayItems.OfType<JsonVirtualDisplayItem>().Any();

    [ObservableProperty] private JsonDisplayItem? _selectedDisplayItem;

    public JsonNodeModel? SelectedNode
        => (SelectedDisplayItem as JsonTreeDisplayItem)?.Node
        ?? (SelectedDisplayItem as JsonInlineContentDisplayItem)?.Node;

    public bool IsTreeModeActive  => _activeInlineItem?.Node != SelectedNode || SelectedNode is null;
    public bool IsTextModeActive  => _activeInlineItem?.Node == SelectedNode
                                  && _activeInlineItem?.ViewMode == NodeViewMode.Text;
    public bool IsTableModeActive => _activeInlineItem?.Node == SelectedNode
                                  && _activeInlineItem?.ViewMode == NodeViewMode.Table;
    public bool IsTableModeAvailable => SelectedNode is JsonArrayNodeModel arr
                                     && arr.Children.OfType<JsonObjectNodeModel>().Any();

    public event EventHandler<JsonDisplayItem>? ScrollToItemRequested;

    partial void OnSelectedDisplayItemChanged(JsonDisplayItem? value)
    {
        OnPropertyChanged(nameof(SelectedNode));
        OnPropertyChanged(nameof(IsTreeModeActive));
        OnPropertyChanged(nameof(IsTextModeActive));
        OnPropertyChanged(nameof(IsTableModeActive));
        OnPropertyChanged(nameof(IsTableModeAvailable));
        RebuildBreadcrumbs(SelectedNode);
        if (value is JsonVirtualDisplayItem vdi)
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

            if (Root is not null)
            {
                InsertNodeItem(0, Root, depth: 0);
                ExpandNode(Root);
            }

            }
        catch (OperationCanceledException) { }
        finally { IsLoading = false; }
    }

    // ── Display list management ───────────────────────────────────────────────

    private int InsertNodeItem(int insertAt, JsonNodeModel node, int depth)
    {
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

        // Remove any existing inline content item
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
        {
            DisplayItems.Insert(DisplayItems.IndexOf(ni) + 1, _activeInlineItem);
        }

        RaiseViewModeProperties();
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
            await File.WriteAllTextAsync(FilePath, json, Encoding.UTF8);
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
        }
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

    // ── Virtual node loading ──────────────────────────────────────────────────

    /// <summary>
    /// Called by scroll-triggered pre-loading: triggers the first available
    /// unloaded virtual sentinel in the display list.
    /// </summary>
    internal void TriggerVirtualLoads()
    {
        var virtualItem = DisplayItems
            .OfType<JsonVirtualDisplayItem>()
            .FirstOrDefault(i => i.Node is VirtualJsonNodeModel v && !v.IsLoading);
        if (virtualItem is not null)
            _ = LoadVirtualItemAsync(virtualItem);
    }

    private async Task LoadVirtualItemAsync(JsonVirtualDisplayItem item)
    {
        if (item.Node is not VirtualJsonNodeModel virtualNode) return;
        if (virtualNode.IsLoading) return;  // guard against double-trigger
        virtualNode.IsLoading = true;
        try
        {
            var parent = virtualNode.Parent;
            if (parent is null) return;

            var isArray  = parent is JsonArrayNodeModel;
            var fileSize = virtualNode.EndOffset;  // EndOffset == file size for all sentinels

            var (batch, nextOffset) = await _loader.LoadVirtualChunkAsync(
                FilePath, virtualNode.ByteOffset, fileSize, isArray, CancellationToken.None);

            if (batch.Count == 0) return;
            await Application.Current.Dispatcher.InvokeAsync(
                () => ReplaceVirtualNodeWithBatch(virtualNode, batch, item, nextOffset));
        }
        catch { /* ignore */ }
        finally { virtualNode.IsLoading = false; }
    }

    private void ReplaceVirtualNodeWithBatch(
        VirtualJsonNodeModel           virtualNode,
        List<(string? key, JsonNode? node)> batch,
        JsonVirtualDisplayItem         displayItem,
        long                           nextOffset)
    {
        var parent = virtualNode.Parent;
        if (parent is null) return;

        var siblings = GetChildren(parent);
        if (siblings is null) return;

        var sibIdx = siblings.IndexOf(virtualNode);
        if (sibIdx < 0) return;
        siblings.RemoveAt(sibIdx);

        var startIndex = virtualNode.Index ?? sibIdx;
        var newNodes   = new List<JsonNodeModel>();

        for (var i = 0; i < batch.Count; i++)
        {
            var (key, jsonNode) = batch[i];
            var nodeCount = 0;
            var model = JsonFileLoader.BuildModelFromJsonNode(
                jsonNode, parent, key,
                parent is JsonArrayNodeModel ? startIndex + i : (int?)null,
                ref nodeCount);
            siblings.Insert(sibIdx + i, model);
            newNodes.Add(model);
        }

        // If more content remains, add a new sentinel immediately after the new items
        VirtualJsonNodeModel? nextVirtual = null;
        if (nextOffset < virtualNode.EndOffset - 10)
        {
            nextVirtual = new VirtualJsonNodeModel
            {
                Parent     = parent,
                Index      = startIndex + batch.Count,
                ByteOffset = nextOffset,
                EndOffset  = virtualNode.EndOffset,
            };
            siblings.Insert(sibIdx + batch.Count, nextVirtual);
        }

        // Update the flat display list
        var displayIdx = DisplayItems.IndexOf(displayItem);
        if (displayIdx < 0) return;
        DisplayItems.RemoveAt(displayIdx);

        foreach (var node in newNodes)
            displayIdx = InsertNodeItem(displayIdx, node, displayItem.Depth);

        if (nextVirtual is not null)
            DisplayItems.Insert(displayIdx, new JsonVirtualDisplayItem
            {
                Node  = nextVirtual,
                Depth = displayItem.Depth,
            });
    }

    // ── IPageViewModel ────────────────────────────────────────────────────────

    public string GetContext()
    {
        if (string.IsNullOrEmpty(FilePath)) return "JSON viewer: no file loaded.";
        var sel = SelectedNode?.GetJsonPath() ?? "none";
        return $"JSON file: '{FileName}' ({FileSizeText}), {NodeCount} nodes. Selected: {sel}";
    }

    public IReadOnlyList<ActionDescriptor> GetAvailableActions()
        => [new ActionDescriptor("Format JSON", "Re-indent the JSON document.")];

    public IContext? GetContextObject()
    {
        if (string.IsNullOrEmpty(FilePath)) return null;
        var dir = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(dir)) return null;
        return new FileSystemContext { RootPath = dir, CurrentPath = dir, SelectedItems = [FilePath] };
    }

    public void Execute(ActionDescriptor action)
    {
        if (action.Name == "Format JSON") FormatJsonCommand.Execute(null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ObservableCollection<JsonNodeModel>? GetChildren(JsonNodeModel node)
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
        if (Root is null) return;
        InsertNodeItem(0, Root, depth: 0);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)              return $"{bytes} B";
        if (bytes < 1024 * 1024)      return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
    }
}
