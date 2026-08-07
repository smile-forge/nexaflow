using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.SystemInfo.ClientTools;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Features.SystemInfo.Services;

namespace Nexaflow.Features.SystemInfo.ViewModels;

/// <summary>
/// The Windows Services page: a filterable list read off the UI thread, with per-row controls that run
/// through the elevated privilege bridge (one UAC prompt per action). Exposes the list to the AI as a
/// summary plus read/act client tools.
/// </summary>
public sealed partial class ServicesViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private readonly IShellServices _shell;
    private readonly ServicesCollector _collector;
    private CancellationTokenSource _cts = new();

    // True while a programmatic StartMode write is snapping the ComboBox back, so the view's
    // SelectionChanged handler ignores it (see OnStartModeSelected).
    private bool _suppressStartMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextReady))]
    private bool _isLoading;

    [ObservableProperty]
    private string _filterText = "";

    public ObservableCollection<ServiceRow> Services { get; } = [];
    public ICollectionView ServicesView { get; }

    /// <summary>Startup modes offered in the per-row picker.</summary>
    public IReadOnlyList<string> StartModes { get; } =
    [
        ServiceStartModes.Automatic, ServiceStartModes.AutomaticDelayed,
        ServiceStartModes.Manual,    ServiceStartModes.Disabled,
    ];

    private string _contextText = "Windows services: still gathering…";

    public ServicesViewModel(IShellServices shell) : this(shell, new ServicesCollector()) { }

    public ServicesViewModel(IShellServices shell, ServicesCollector collector)
    {
        _shell = shell;
        _collector = collector;
        ServicesView = CollectionViewSource.GetDefaultView(Services);
        ServicesView.Filter = MatchesFilter;
        Refresh();
    }

    partial void OnFilterTextChanged(string value)
    {
        // Typing over the box drops whatever "?" put behind it — the text on screen is always what is
        // being filtered by.
        if (!_suppressFilterReset) ClearSearchState();
        ServicesView.Refresh();
    }

    /// <summary>Most specific first: an agent's pinned set, then a compiled "?" query, then the typed text.</summary>
    private bool MatchesFilter(object item)
    {
        if (item is not ServiceRow r) return false;
        if (_pinnedNames is not null)    return _pinnedNames.Contains(r.Name);
        if (_filterRequest is not null)  return _filterRequest.Matches(r.Name)
                                             || _filterRequest.Matches(r.DisplayName);
        if (string.IsNullOrWhiteSpace(FilterText)) return true;
        var f = FilterText.Trim();
        return r.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase)
            || r.Name.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void Refresh()
    {
        if (IsLoading) return;
        IsLoading = true;

        var task = new LoadServicesTask(_collector);
        _shell.QueueBackgroundTask(task, onComplete: ok =>
        {
            if (ok && task.Result is { } rows)
            {
                Services.Clear();
                foreach (var r in rows) Services.Add(r);
                UpdateContext();
            }
            IsLoading = false;
        }, _cts.Token);
    }

    // ── Per-row actions (UI buttons bind their Command + the row as CommandParameter) ────────────
    [RelayCommand] private Task StartService(ServiceRow row)   => ControlAsync(row, ElevatedOps.ServiceStart);
    [RelayCommand] private Task StopService(ServiceRow row)    => ControlAsync(row, ElevatedOps.ServiceStop);
    [RelayCommand] private Task RestartService(ServiceRow row) => ControlAsync(row, ElevatedOps.ServiceRestart);
    [RelayCommand] private Task PauseService(ServiceRow row)   => ControlAsync(row, ElevatedOps.ServicePause);
    [RelayCommand] private Task ResumeService(ServiceRow row)  => ControlAsync(row, ElevatedOps.ServiceContinue);

    /// <summary>Runs a service control op via the bridge and refreshes the row. Shared by UI + tools.</summary>
    internal async Task<ElevatedResult> ControlAsync(ServiceRow row, string op)
    {
        if (row is null) return ElevatedResult.Fail(ElevatedErrorKind.Unexpected, "No service.");
        var res = await _shell.RunElevatedAsync(
            ElevatedRequest.Single(op, (ElevatedArgs.ServiceName, row.Name)));
        if (!res.Success && !res.WasDeclined) _shell.ShowError(res.Message);
        RefreshRow(row);
        return res;
    }

    /// <summary>Applies a startup-mode change via the bridge and snaps the row back to the real value.</summary>
    internal async Task<ElevatedResult> SetStartModeAsync(ServiceRow row, string mode)
    {
        var res = await _shell.RunElevatedAsync(ElevatedRequest.Single(ElevatedOps.ServiceSetStartMode,
            (ElevatedArgs.ServiceName, row.Name), (ElevatedArgs.StartMode, mode)));
        if (!res.Success && !res.WasDeclined) _shell.ShowError(res.Message);
        RefreshRow(row);
        return res;
    }

    /// <summary>Called by the view when the user picks a new startup mode in a row's ComboBox.</summary>
    public void OnStartModeSelected(ServiceRow row, string mode)
    {
        if (_suppressStartMode || row is null || string.IsNullOrEmpty(mode) || mode == row.StartMode) return;
        _ = SetStartModeAsync(row, mode);
    }

    private void RefreshRow(ServiceRow row)
    {
        if (_collector.ReadState(row.Name) is not { } state) return;
        _suppressStartMode = true;
        row.Status    = state.Status;
        row.StartMode = state.StartMode;
        _suppressStartMode = false;
        UpdateContext();
    }

    internal ServiceRow? Find(string nameOrDisplay)
    {
        var q = nameOrDisplay.Trim();
        return Services.FirstOrDefault(s => s.Name.Equals(q, StringComparison.OrdinalIgnoreCase))
            ?? Services.FirstOrDefault(s => s.DisplayName.Equals(q, StringComparison.OrdinalIgnoreCase))
            ?? Services.FirstOrDefault(s => s.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    internal IReadOnlyList<ServiceRow> Snapshot() => Services.ToList();

    private void UpdateContext()
    {
        var total   = Services.Count;
        var running = Services.Count(s => s.Status == "Running");
        var stopped = Services.Count(s => s.Status == "Stopped");
        var auto    = Services.Count(s => s.StartMode is ServiceStartModes.Automatic or ServiceStartModes.AutomaticDelayed);
        _contextText =
            $"Windows Services on {Environment.MachineName}: {total} services — {running} running, {stopped} stopped, " +
            $"{auto} automatic-start. Use the service tools to query specifics or to start / stop / restart / change " +
            $"the startup type of a service (each change requires admin approval via a UAC prompt).";
    }

    // ── IPageViewModel ──────────────────────────────────────────────────────────────────────────
    public string GetContext() =>
        IsLoading && Services.Count == 0 ? "Windows services: still gathering…" : _contextText;

    public bool IsContextReady => !IsLoading;

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new ServiceTools.GetServices(this),
        new ServiceTools.FindService(this),
        new ServiceTools.GetService(this),
        new ServiceTools.StartService(this),
        new ServiceTools.StopService(this),
        new ServiceTools.RestartService(this),
        new ServiceTools.SetServiceStartMode(this),
    ];

    public string? GetSecurityContext() => "system-services";

    public ContextSecurityRisk GetContextSecurityRisk() => ContextSecurityRisk.High;

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
