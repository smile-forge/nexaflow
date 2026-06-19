using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Processes.ClientTools;
using Nexaflow.Features.Processes.Models;
using Nexaflow.Visuals.Common.Converters;
using Nexaflow.Features.Processes.Services;

namespace Nexaflow.Features.Processes.ViewModels;

/// <summary>
/// The process tree page. Samples the system once a second (only while it is the active tab — the view
/// starts/stops via <see cref="OnActivated"/>/<see cref="OnDeactivated"/>) off the UI thread, then
/// reconciles the flat <see cref="Rows"/> list in place so selection and expansion survive. Exposes the
/// live picture to the AI as context + read/act tools.
/// </summary>
public sealed partial class ProcessesViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private const int HistoryLength = 60;

    private readonly IShellServices _shell;
    private readonly IProcessSource _source;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<int, ProcessRowViewModel> _rowsByPid = [];
    private readonly List<ProcessRowViewModel> _roots = [];
    private readonly List<double> _cpuBuf = [];
    private readonly List<double> _memBuf = [];
    private bool _refreshing;

    /// <summary>The flat, ordered, displayed rows (a depth-first walk of the tree honouring expansion).</summary>
    public ObservableCollection<ProcessRowViewModel> Rows { get; } = [];

    public IReadOnlyList<string> PriorityClasses { get; } =
        ["Idle", "BelowNormal", "Normal", "AboveNormal", "High", "RealTime"];

    [ObservableProperty] private string _sortColumn = nameof(ProcessRowViewModel.CpuPercent);
    [ObservableProperty] private bool _sortAscending;          // CPU desc by default → top consumers first
    [ObservableProperty] private bool _treeMode = true;        // tree is the default; rows start collapsed
    [ObservableProperty] private bool _autoRefreshEnabled = true;
    [ObservableProperty] private ProcessRowViewModel? _selectedRow;

    [ObservableProperty] private string _filterText = "";

    [ObservableProperty] private double _systemCpuPercent;
    [ObservableProperty] private double _memoryLoadPercent;
    [ObservableProperty] private long _totalPhysicalBytes;
    [ObservableProperty] private long _usedPhysicalBytes;
    [ObservableProperty] private int _processCount;
    [ObservableProperty] private IReadOnlyList<double> _cpuHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _memoryHistory = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextReady))]
    private bool _hasData;

    public ProcessesViewModel(IShellServices shell) : this(shell, new LiveProcessSource()) { }

    public ProcessesViewModel(IShellServices shell, IProcessSource source)
    {
        _shell  = shell;
        _source = source;
        _timer  = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => TriggerRefresh();
        // First sample happens on activation (the view's Loaded), not at construction — the page is built
        // by the factory before it's shown, and there's no point sampling a tab that isn't visible yet.
    }

    // ── Activation (driven by the view's Loaded/Unloaded — the active-tab signal) ─────────────────
    public void OnActivated()
    {
        if (AutoRefreshEnabled) _timer.Start();
        TriggerRefresh();
    }

    public void OnDeactivated() => _timer.Stop();

    partial void OnAutoRefreshEnabledChanged(bool value)
    {
        if (value) { _timer.Start(); TriggerRefresh(); }
        else _timer.Stop();
    }

    partial void OnTreeModeChanged(bool value) => Resort();
    partial void OnFilterTextChanged(string value) => Resort();

    [RelayCommand]
    private void RefreshNow() => TriggerRefresh();

    [RelayCommand]
    private void ClearFilter() => FilterText = "";

    [RelayCommand]
    private void ExpandAll() => SetAllExpanded(true);

    [RelayCommand]
    private void CollapseAll() => SetAllExpanded(false);

    private void SetAllExpanded(bool expanded)
    {
        foreach (var r in _rowsByPid.Values) r.IsExpanded = expanded;
        Resort();
    }

    private async void TriggerRefresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var snap = await Task.Run(_source.Snapshot);
            ApplySnapshot(snap);
        }
        catch { /* a sampling hiccup must not kill the loop */ }
        finally { _refreshing = false; }
    }

    // ── Reconcile (UI thread) ─────────────────────────────────────────────────────────────────────
    /// <summary>Folds a fresh snapshot into the live row set without clearing — internal so unit tests
    /// can drive it directly without the timer.</summary>
    internal void ApplySnapshot(ProcessSnapshot snap)
    {
        var live = new HashSet<int>(snap.Processes.Count);

        foreach (var s in snap.Processes)
        {
            live.Add(s.Pid);
            if (_rowsByPid.TryGetValue(s.Pid, out var row))
            {
                // PID reuse: a different start time means this is a new process on the same id.
                if (row.StartTime is { } prev && s.StartTime is { } cur && prev != cur)
                {
                    row = new ProcessRowViewModel(s.Pid);
                    _rowsByPid[s.Pid] = row;
                }
            }
            else
            {
                row = new ProcessRowViewModel(s.Pid);
                _rowsByPid[s.Pid] = row;
            }
            row.Update(s);
        }

        foreach (var pid in _rowsByPid.Keys.Where(k => !live.Contains(k)).ToList())
            _rowsByPid.Remove(pid);

        var parentMap = snap.Processes.ToDictionary(s => s.Pid, s => s.ParentPid);
        RebuildTree(parentMap);
        SyncRows(Flatten());
        UpdateGauges(snap);

        if (!HasData) HasData = true;
    }

    private void RebuildTree(Dictionary<int, int> parentMap)
    {
        foreach (var row in _rowsByPid.Values) { row.Parent = null; row.Children.Clear(); }
        _roots.Clear();

        foreach (var row in _rowsByPid.Values)
        {
            int parentPid = parentMap.GetValueOrDefault(row.Pid, 0);
            ProcessRowViewModel? parent = null;
            if (parentPid != row.Pid
                && _rowsByPid.TryGetValue(parentPid, out var p)
                && !ParentChainReaches(parentPid, row.Pid, parentMap))
                parent = p;

            if (parent is not null) { row.Parent = parent; parent.Children.Add(row); }
            else _roots.Add(row);
        }

        foreach (var row in _rowsByPid.Values)
            row.HasChildren = row.Children.Count > 0;
    }

    /// <summary>True if following <paramref name="startPid"/>'s parent chain reaches <paramref name="targetPid"/>
    /// — i.e. linking target under start would form a cycle. Bounded against pre-existing loops.</summary>
    private bool ParentChainReaches(int startPid, int targetPid, Dictionary<int, int> parentMap)
    {
        var seen = new HashSet<int>();
        int cur = startPid;
        while (_rowsByPid.ContainsKey(cur) && parentMap.TryGetValue(cur, out var par))
        {
            if (par == targetPid) return true;
            if (!seen.Add(par)) return false;
            cur = par;
        }
        return false;
    }

    private List<ProcessRowViewModel> Flatten()
    {
        var result = new List<ProcessRowViewModel>(_rowsByPid.Count);
        var filter = FilterText.Trim();

        // Filtering always collapses to a flat list of matches — a partially-filtered tree is confusing.
        if (!TreeMode || filter.Length > 0)
        {
            var all = _rowsByPid.Values.Where(r => Matches(r, filter)).ToList();
            all.Sort(CompareRows);
            foreach (var r in all) { r.Depth = 0; result.Add(r); }
            return result;
        }

        var roots = _roots.ToList();
        roots.Sort(CompareRows);
        foreach (var r in roots) AddSubtree(r, 0, result);
        return result;
    }

    private static bool Matches(ProcessRowViewModel r, string filter)
    {
        if (filter.Length == 0) return true;
        return r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || r.Pid.ToString().Contains(filter)
            || r.Company.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || r.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || r.Path.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void AddSubtree(ProcessRowViewModel row, int depth, List<ProcessRowViewModel> result)
    {
        row.Depth = depth;
        result.Add(row);
        if (!row.IsExpanded || row.Children.Count == 0) return;
        var kids = row.Children.ToList();
        kids.Sort(CompareRows);
        foreach (var k in kids) AddSubtree(k, depth + 1, result);
    }

    private int CompareRows(ProcessRowViewModel a, ProcessRowViewModel b)
    {
        int c = SortColumn switch
        {
            nameof(ProcessRowViewModel.CpuPercent)   => a.CpuPercent.CompareTo(b.CpuPercent),
            nameof(ProcessRowViewModel.PrivateBytes) => a.PrivateBytes.CompareTo(b.PrivateBytes),
            nameof(ProcessRowViewModel.WorkingSet)   => a.WorkingSet.CompareTo(b.WorkingSet),
            nameof(ProcessRowViewModel.Pid)          => a.Pid.CompareTo(b.Pid),
            nameof(ProcessRowViewModel.Description)  => string.Compare(a.Description, b.Description, StringComparison.OrdinalIgnoreCase),
            nameof(ProcessRowViewModel.Company)      => string.Compare(a.Company, b.Company, StringComparison.OrdinalIgnoreCase),
            _                                        => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
        };
        if (c == 0) c = a.Pid.CompareTo(b.Pid);     // stable tiebreak
        return SortAscending ? c : -c;
    }

    /// <summary>Reconciles <see cref="Rows"/> to <paramref name="target"/> with targeted Insert/Move/Remove
    /// (never Clear) so the grid never blanks and selection/scroll survive.</summary>
    private void SyncRows(List<ProcessRowViewModel> target)
    {
        var keep = new HashSet<ProcessRowViewModel>(target);
        for (int i = Rows.Count - 1; i >= 0; i--)
            if (!keep.Contains(Rows[i])) Rows.RemoveAt(i);

        for (int i = 0; i < target.Count; i++)
        {
            var item = target[i];
            if (i < Rows.Count && ReferenceEquals(Rows[i], item)) continue;
            int cur = Rows.IndexOf(item);
            if (cur >= 0) Rows.Move(cur, i);
            else Rows.Insert(i, item);
        }
        while (Rows.Count > target.Count) Rows.RemoveAt(Rows.Count - 1);
    }

    private void Resort()
    {
        if (_rowsByPid.Count == 0) return;
        SyncRows(Flatten());
    }

    private void UpdateGauges(ProcessSnapshot snap)
    {
        SystemCpuPercent   = snap.SystemCpuPercent;
        MemoryLoadPercent  = snap.MemoryLoadPercent;
        TotalPhysicalBytes = snap.TotalPhysicalBytes;
        UsedPhysicalBytes  = snap.UsedPhysicalBytes;
        ProcessCount       = snap.Processes.Count;

        Push(_cpuBuf, snap.SystemCpuPercent);  CpuHistory    = _cpuBuf.ToArray();
        Push(_memBuf, snap.MemoryLoadPercent); MemoryHistory = _memBuf.ToArray();
    }

    private static void Push(List<double> buf, double v)
    {
        buf.Add(v);
        if (buf.Count > HistoryLength) buf.RemoveAt(0);
    }

    // ── Sorting (header click) ────────────────────────────────────────────────────────────────────
    [RelayCommand]
    private void SortBy(string column)
    {
        if (string.IsNullOrEmpty(column)) return;
        if (SortColumn == column)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortColumn = column;
            // text columns read better ascending; numeric columns descending (biggest first).
            SortAscending = column is nameof(ProcessRowViewModel.Name)
                                   or nameof(ProcessRowViewModel.Description)
                                   or nameof(ProcessRowViewModel.Company);
        }
        Resort();
    }

    // ── Expansion ─────────────────────────────────────────────────────────────────────────────────
    [RelayCommand]
    private void ToggleExpand(ProcessRowViewModel? row)
    {
        if (row is null || !row.HasChildren) return;
        row.IsExpanded = !row.IsExpanded;
        SyncRows(Flatten());
    }

    // ── Actions ─────────────────────────────────────────────────────────────────────────────────
    [RelayCommand]
    private void ViewDetails(ProcessRowViewModel? row)
    {
        if (row is null) return;
        _shell.OpenTab(ProcessDetailPageRegistration.StaticPageKind,
            new Dictionary<string, string> { ["pid"] = row.Pid.ToString() });
    }

    [RelayCommand] private Task Kill(ProcessRowViewModel? row)     => ConfirmKill(row, tree: false);
    [RelayCommand] private Task KillTree(ProcessRowViewModel? row) => ConfirmKill(row, tree: true);

    private async Task ConfirmKill(ProcessRowViewModel? row, bool tree)
    {
        if (row is null) return;
        var title = tree ? $"Kill {row.Name} and its child processes?" : $"Kill {row.Name} (PID {row.Pid})?";
        var body  = tree
            ? "This terminates the process and every process it started, immediately. Unsaved work is lost."
            : "This terminates the process immediately. Unsaved work is lost.";
        if (!await _shell.ConfirmAsync(title, body)) return;

        await ProcessActions.KillAsync(_shell, row.Pid, row.Name, tree);
        TriggerRefresh();
    }

    [RelayCommand]
    private void OpenLocation(ProcessRowViewModel? row)
    {
        if (row is null) return;
        var path = string.IsNullOrWhiteSpace(row.Path) ? _source.ReadDetail(row.Pid)?.Path : row.Path;
        var dir  = string.IsNullOrWhiteSpace(path) ? null : System.IO.Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir))
        {
            _shell.ShowError($"Couldn't locate the image for {row.Name}.");
            return;
        }
        _shell.OpenTab("FileSystem", new Dictionary<string, string>
        {
            ["mode"]  = "path",
            ["path"]  = dir,
            ["label"] = row.Name,
        });
    }

    [RelayCommand]
    private void CopyDetails(ProcessRowViewModel? row)
    {
        if (row is null) return;
        var text = $"{row.Name} (PID {row.Pid})\n" +
                   $"CPU: {(row.CpuText.Length == 0 ? "0" : row.CpuText)}%\n" +
                   $"Private bytes: {BytesToTextConverter.Format(row.PrivateBytes)}\n" +
                   $"Working set: {BytesToTextConverter.Format(row.WorkingSet)}\n" +
                   $"{row.Description}{(row.Company.Length > 0 ? $" — {row.Company}" : "")}\n" +
                   row.Path;
        Copy(text);
    }

    [RelayCommand] private void CopyName(ProcessRowViewModel? row) { if (row is not null) Copy(row.Name); }
    [RelayCommand] private void CopyPid(ProcessRowViewModel? row)  { if (row is not null) Copy(row.Pid.ToString()); }

    [RelayCommand]
    private void CopyPath(ProcessRowViewModel? row)
    {
        if (row is null) return;
        if (string.IsNullOrWhiteSpace(row.Path)) { _shell.ShowError($"No image path available for {row.Name}."); return; }
        Copy(row.Path);
    }

    private static void Copy(string text)
    {
        try { Clipboard.SetText(text); } catch { /* clipboard busy */ }
    }

    // ── Hooks used by the AI tools ────────────────────────────────────────────────────────────────
    internal IReadOnlyList<ProcessRowViewModel> RowsSnapshot() => _rowsByPid.Values.ToList();
    internal ProcessRowViewModel? FindByPid(int pid) => _rowsByPid.GetValueOrDefault(pid);

    internal ProcessRowViewModel? FindByNameOrPid(string query)
    {
        var q = query.Trim();
        if (int.TryParse(q, out var pid) && _rowsByPid.TryGetValue(pid, out var byPid)) return byPid;
        return _rowsByPid.Values.FirstOrDefault(r => r.Name.Equals(q, StringComparison.OrdinalIgnoreCase))
            ?? _rowsByPid.Values.FirstOrDefault(r =>
                   r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                   $"{q}.exe".Equals(r.Name, StringComparison.OrdinalIgnoreCase));
    }

    internal ProcessDetail? ReadDetail(int pid) => _source.ReadDetail(pid);

    internal async Task<string> KillByToolAsync(int pid, bool tree)
    {
        var name = FindByPid(pid)?.Name ?? $"PID {pid}";
        var msg = await ProcessActions.KillAsync(_shell, pid, name, tree);
        TriggerRefresh();
        return msg;
    }

    internal async Task<string> SetPriorityByToolAsync(int pid, string priority)
    {
        var name = FindByPid(pid)?.Name ?? $"PID {pid}";
        var msg = await ProcessActions.SetPriorityAsync(_shell, pid, name, priority);
        TriggerRefresh();
        return msg;
    }

    // ── IPageViewModel ─────────────────────────────────────────────────────────────────────────────
    public string GetContext()
    {
        if (!HasData) return "Running processes: still gathering…";
        var all = _rowsByPid.Values;
        var topCpu = all.OrderByDescending(r => r.CpuPercent).Take(3)
                        .Select(r => $"{r.Name} ({r.CpuPercent:0.0}%)");
        var topMem = all.OrderByDescending(r => r.WorkingSet).Take(3)
                        .Select(r => $"{r.Name} ({BytesToTextConverter.Format(r.WorkingSet)})");
        return $"{ProcessCount} processes running on {Environment.MachineName}. " +
               $"System CPU {SystemCpuPercent:0}%, memory {MemoryLoadPercent:0}%. " +
               $"Top CPU: {string.Join(", ", topCpu)}. Top memory: {string.Join(", ", topMem)}. " +
               "Use the process tools to query details / modules / the tree, or to kill or re-prioritise a " +
               "process (mutations may prompt for administrator approval).";
    }

    public bool IsContextReady => HasData;

    public IReadOnlyList<IClientTool> GetClientTools() => ProcessTools.For(this);

    public string? GetSecurityContext() => "running-processes";

    public ContextSecurityRisk GetContextSecurityRisk() => ContextSecurityRisk.High;

    public void Dispose()
    {
        _timer.Stop();
        (_source as IDisposable)?.Dispose();
    }
}
