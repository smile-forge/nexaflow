using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Processes.Models;
using Nexaflow.Features.Processes.Services;
using Nexaflow.Visuals.Common.Converters;

namespace Nexaflow.Features.Processes.ViewModels;

/// <summary>
/// The per-process details page (General / Performance / Threads / Modules as vertical tabs). Refreshes
/// once a second only while it is the active tab. Performance numbers update in place via
/// <see cref="Detail"/>; <see cref="Threads"/> and <see cref="Modules"/> are reconciled by key so their
/// lists don't reset scroll each tick. Sections that can't be read without elevation show a banner.
/// </summary>
public sealed partial class ProcessDetailViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private const int HistoryLength = 60;

    private readonly IShellServices _shell;
    private readonly IProcessSource _source;
    private readonly DispatcherTimer _timer;
    private readonly List<double> _cpuBuf = [];
    private int _pid;
    private bool _refreshing;
    private bool _suppressPriority;

    public ObservableCollection<ThreadInfo> Threads { get; } = [];
    public ObservableCollection<ModuleInfo> Modules { get; } = [];

    /// <summary>Open handles — populated on demand via an elevated <c>process.inspect</c>, not the refresh
    /// tick (no managed API, and one UAC per load).</summary>
    public ObservableCollection<HandleInfo> Handles { get; } = [];

    /// <summary>Sortable view over <see cref="Modules"/> (header-click sorting + resizable columns).</summary>
    public ICollectionView ModulesView { get; }

    /// <summary>Views the three list sections bind through, so a "?" search can narrow one of them without
    /// touching the collections the refresh tick reconciles.</summary>
    public ICollectionView ThreadsView { get; }
    public ICollectionView HandlesView { get; }

    [ObservableProperty] private string _moduleSortColumn = nameof(ModuleInfo.Name);
    [ObservableProperty] private bool _moduleSortAscending = true;

    public IReadOnlyList<string> PriorityClasses { get; } =
        ["Idle", "BelowNormal", "Normal", "AboveNormal", "High", "RealTime"];

    [ObservableProperty] private string _title = "Process";
    [ObservableProperty] private ProcessDetail? _detail;
    [ObservableProperty] private bool _threadsDenied;
    [ObservableProperty] private bool _modulesDenied;
    [ObservableProperty] private bool _handlesLoaded;
    [ObservableProperty] private bool _handlesLoading;
    [ObservableProperty] private string? _handlesError;
    [ObservableProperty] private bool _isGone;
    [ObservableProperty] private bool _autoRefreshEnabled = true;
    [ObservableProperty] private string? _selectedPriority;
    [ObservableProperty] private IReadOnlyList<double> _cpuHistory = [];

    /// <summary>Upper bound of the CPU sparkline — auto-scaled to the recent peak (min 5%) so a process
    /// using a fraction of a percent still shows a readable trend instead of a flat line at the bottom.</summary>
    [ObservableProperty] private double _cpuHistoryMax = 5;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextReady))]
    private bool _hasData;

    public ProcessDetailViewModel(IShellServices shell, int pid)
        : this(shell, pid, new LiveProcessSource()) { }

    public ProcessDetailViewModel(IShellServices shell, int pid, IProcessSource source)
    {
        _shell  = shell;
        _source = source;
        _pid    = pid;
        Title   = $"Process {pid}";
        ModulesView = CollectionViewSource.GetDefaultView(Modules);
        ThreadsView = CollectionViewSource.GetDefaultView(Threads);
        HandlesView = CollectionViewSource.GetDefaultView(Handles);
        ModulesView.Filter = o => PassesSearch(Sections.Modules, o);
        ThreadsView.Filter = o => PassesSearch(Sections.Threads, o);
        HandlesView.Filter = o => PassesSearch(Sections.Handles, o);
        ApplyModuleSort();
        _timer  = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => TriggerRefresh();
        // First read happens on activation (the view's Loaded), not at construction.
    }

    [RelayCommand]
    private void SortModules(string column)
    {
        if (string.IsNullOrEmpty(column)) return;
        if (ModuleSortColumn == column) ModuleSortAscending = !ModuleSortAscending;
        else { ModuleSortColumn = column; ModuleSortAscending = true; }
        ApplyModuleSort();
    }

    private void ApplyModuleSort()
    {
        ModulesView.SortDescriptions.Clear();
        ModulesView.SortDescriptions.Add(new SortDescription(
            ModuleSortColumn, ModuleSortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending));
    }

    public int Pid => _pid;

    // ── Activation ────────────────────────────────────────────────────────────────────────────────
    public void OnActivated()
    {
        if (AutoRefreshEnabled && !IsGone) _timer.Start();
        TriggerRefresh();
    }

    public void OnDeactivated() => _timer.Stop();

    partial void OnAutoRefreshEnabledChanged(bool value)
    {
        if (value && !IsGone) { _timer.Start(); TriggerRefresh(); }
        else _timer.Stop();
    }

    /// <summary>Called by the view when the tab is re-activated with params (possibly a new PID).</summary>
    public void Reinitialize(int pid)
    {
        if (pid == _pid) { TriggerRefresh(); return; }
        _pid = pid;
        Title = $"Process {pid}";
        IsGone = false;
        _cpuBuf.Clear();
        Threads.Clear();
        Modules.Clear();
        Handles.Clear();
        HandlesLoaded = false;
        HandlesError = null;
        HasData = false;
        if (AutoRefreshEnabled) _timer.Start();
        TriggerRefresh();
    }

    [RelayCommand]
    private void RefreshNow() => TriggerRefresh();

    private async void TriggerRefresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        int pid = _pid;
        try
        {
            var detail = await Task.Run(() => _source.ReadDetail(pid));
            if (pid == _pid) ApplyDetail(detail);
        }
        catch { /* keep the tab alive */ }
        finally { _refreshing = false; }
    }

    internal void ApplyDetail(ProcessDetail? d)
    {
        if (d is null)
        {
            IsGone = true;
            _timer.Stop();
            return;
        }

        Detail = d;
        Title  = $"{d.Name} ({d.Pid})";
        ThreadsDenied = d.ThreadsDenied;
        ModulesDenied = d.ModulesDenied;

        Reconcile(Threads, d.Threads, t => t.Tid);
        Reconcile(Modules, d.Modules, m => $"{m.Name}|{m.Path}");

        Push(_cpuBuf, d.CpuPercent);
        CpuHistory    = _cpuBuf.ToArray();
        CpuHistoryMax = NiceCeil(_cpuBuf.Count > 0 ? _cpuBuf.Max() : 0);

        _suppressPriority = true;
        SelectedPriority = d.PriorityClass;
        _suppressPriority = false;

        if (!HasData) HasData = true;
    }

    partial void OnSelectedPriorityChanged(string? value)
    {
        if (_suppressPriority || string.IsNullOrEmpty(value)) return;
        if (Detail is not null && value == Detail.PriorityClass) return;
        _ = ApplyPriority(value);
    }

    private async Task ApplyPriority(string priority)
    {
        await ProcessActions.SetPriorityAsync(_shell, _pid, Detail?.Name ?? $"PID {_pid}", priority);
        TriggerRefresh();
    }

    [RelayCommand]
    private async Task Kill()
    {
        var name = Detail?.Name ?? $"PID {_pid}";
        if (!await _shell.ConfirmAsync($"Kill {name} (PID {_pid})?",
                "This terminates the process immediately. Unsaved work is lost."))
            return;
        await ProcessActions.KillAsync(_shell, _pid, name, tree: false);
        TriggerRefresh();
    }

    /// <summary>Loads the process's open handles (and, for a protected process, its modules + command line)
    /// via one elevated <c>process.inspect</c>. Driven by the Handles tab button, never the refresh tick.</summary>
    [RelayCommand]
    private async Task LoadHandles()
    {
        if (HandlesLoading) return;
        HandlesLoading = true;
        HandlesError = null;
        try
        {
            var r = await ProcessInspect.InspectAsync(_shell, _pid, "all");
            ApplyInspect(r);
        }
        finally { HandlesLoading = false; }
    }

    internal void ApplyInspect(ProcessInspect.InspectResult r)
    {
        if (r.Declined) { HandlesError = "Administrator approval was declined."; return; }
        if (!r.Success) { HandlesError = r.Message; _shell.ShowError(r.Message); return; }

        Reconcile(Handles, r.Handles, h => $"{h.HandleValue}|{h.Type}");
        HandlesLoaded = true;

        if (r.Modules is { } mods)
        {
            Reconcile(Modules, mods, m => $"{m.Name}|{m.Path}");
            ModulesDenied = false;
        }
        if (r.CommandLine is { Length: > 0 } cl && Detail is not null)
            Detail = Detail with { CommandLine = cl };
    }

    [RelayCommand]
    private void OpenLocation()
    {
        var dir = string.IsNullOrWhiteSpace(Detail?.Path) ? null : System.IO.Path.GetDirectoryName(Detail!.Path);
        if (string.IsNullOrEmpty(dir)) { _shell.ShowError("The image path isn't available."); return; }
        _shell.OpenTab("FileSystem", new Dictionary<string, string>
        {
            ["mode"]  = "path",
            ["path"]  = dir,
            ["label"] = Detail!.Name,
        });
    }

    [RelayCommand]
    private void OpenModuleLocation(ModuleInfo? module)
    {
        var dir = string.IsNullOrWhiteSpace(module?.Path) ? null : System.IO.Path.GetDirectoryName(module!.Path);
        if (string.IsNullOrEmpty(dir)) { _shell.ShowError("The module path isn't available."); return; }
        _shell.OpenTab("FileSystem", new Dictionary<string, string>
        {
            ["mode"]  = "path",
            ["path"]  = dir,
            ["label"] = module!.Name,
        });
    }

    [RelayCommand]
    private void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { System.Windows.Clipboard.SetText(text); } catch { /* clipboard busy */ }
    }

    private static void Push(List<double> buf, double v)
    {
        buf.Add(v);
        if (buf.Count > HistoryLength) buf.RemoveAt(0);
    }

    private static double NiceCeil(double v)
    {
        v = Math.Max(v, 5);
        foreach (var step in new[] { 5.0, 10, 25, 50, 100 })
            if (v <= step) return step;
        return 100;
    }

    /// <summary>In-place reconcile of an observable list to <paramref name="source"/> by stable key, so a
    /// 1s refresh doesn't reset scroll/selection. Records compare by value, so unchanged rows aren't touched.</summary>
    private static void Reconcile<T>(ObservableCollection<T> target, IReadOnlyList<T> source, Func<T, object> key)
    {
        var srcKeys = source.Select(key).ToHashSet();
        for (int i = target.Count - 1; i >= 0; i--)
            if (!srcKeys.Contains(key(target[i]))) target.RemoveAt(i);

        for (int i = 0; i < source.Count; i++)
        {
            var item = source[i];
            var k = key(item);
            if (i < target.Count && key(target[i]).Equals(k))
            {
                if (!Equals(target[i], item)) target[i] = item;
                continue;
            }
            int existing = -1;
            for (int j = i + 1; j < target.Count; j++)
                if (key(target[j]).Equals(k)) { existing = j; break; }

            if (existing >= 0)
            {
                if (!Equals(target[existing], item)) target[existing] = item;
                target.Move(existing, i);
            }
            else target.Insert(i, item);
        }
        while (target.Count > source.Count) target.RemoveAt(target.Count - 1);
    }

    // ── IPageViewModel ─────────────────────────────────────────────────────────────────────────────
    public string GetContext()
    {
        if (IsGone) return $"Process {_pid} has exited.";
        if (Detail is not { } d) return $"Process {_pid}: still gathering…";
        return $"Process {d.Name} (PID {d.Pid}) — {d.Description}. " +
               $"CPU {d.CpuPercent:0.0}%, private bytes {BytesToTextConverter.Format(d.PrivateBytes)}, " +
               $"working set {BytesToTextConverter.Format(d.WorkingSet)}, {d.ThreadCount} threads, " +
               $"{d.HandleCount} handles. Path: {(d.Path.Length > 0 ? d.Path : "—")}. " +
               $"Started {(d.StartTime?.ToString("g") ?? "—")} as {(d.User.Length > 0 ? d.User : "—")}.";
    }

    public bool IsContextReady => HasData || IsGone;

    public ContextSecurityRisk GetContextSecurityRisk() => ContextSecurityRisk.High;

    public string? GetSecurityContext() => "running-processes";

    public void Dispose()
    {
        _timer.Stop();
        (_source as IDisposable)?.Dispose();
    }
}
