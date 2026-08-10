using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsApps.Models;
using Nexaflow.Features.WindowsApps.Services;

namespace Nexaflow.Features.WindowsApps.ViewModels;

/// <summary>
/// The "Advanced options" side pane for one Store app — the same surface Windows offers: the
/// background-execution dropdown, Terminate / Repair / Reset, a Move-to-another-drive picker, and the
/// list of add-ons installed against the app.
///
/// Every operation is queued onto the shell's background-activity queue (the completion callback comes
/// back marshalled), so the pane never blocks the UI thread and never touches the dispatcher itself.
/// The destructive ones — Reset and removing an add-on — confirm first.
/// </summary>
public sealed partial class AppAdvancedOptionsViewModel : ObservableObject
{
    private readonly IShellServices _shell;
    private readonly InstalledAppsService _service;
    private readonly IStoreAppOperations? _ops;
    private readonly Action _close;

    /// <summary>The row this pane describes. Re-pointed at the fresh item after a rescan.</summary>
    [ObservableProperty] private InstalledAppItem _item;

    /// <summary>An operation is in flight — the pane's buttons are disabled while it runs.</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>The outcome of the last operation, shown under the buttons. Null when there's nothing to say.</summary>
    [ObservableProperty] private string? _status;

    /// <summary>Set when the pane was opened by "Move…" rather than "Advanced options".</summary>
    [ObservableProperty] private bool _moveHighlighted;

    /// <summary>False until the add-on scan has run once, so "none" isn't claimed before we looked.</summary>
    [ObservableProperty] private bool _addOnsScanned;

    public ObservableCollection<AppAddOn> AddOns { get; } = [];
    public ObservableCollection<AppVolume> Volumes { get; } = [];

    public IReadOnlyList<BackgroundModeOption> BackgroundModes => BackgroundModeOption.All;

    /// <summary>The drive Move would relocate the package to.</summary>
    [ObservableProperty] private AppVolume? _selectedVolume;

    [ObservableProperty] private BackgroundModeOption? _selectedBackgroundMode;

    /// <summary>Guards the initial read from being written straight back as if the user had chosen it.</summary>
    private bool _applyingStoredMode;

    public AppAdvancedOptionsViewModel(IShellServices shell, InstalledAppsService service,
                                       InstalledAppItem item, Action close)
    {
        _shell   = shell;
        _service = service;
        _ops     = service.StoreOperations;
        _item    = item;
        _close   = close;
    }

    /// <summary>Dismisses the pane. Owned here so the view binds one DataContext, not its grandparent's.</summary>
    [RelayCommand]
    private void Close() => _close();

    /// <summary>Reads the stored background policy and kicks off the drive + add-on scans.</summary>
    public void Load()
    {
        var family = Item.App.PackageFamilyName;
        if (!string.IsNullOrWhiteSpace(family))
        {
            _applyingStoredMode = true;
            SelectedBackgroundMode = BackgroundModeOption.For(_service.BackgroundAccess.Get(family));
            _applyingStoredMode = false;
        }

        LoadVolumes();
        RefreshAddOns();
    }

    /// <summary>Points the pane at the equivalent row from a fresh scan, keeping it open across a refresh.</summary>
    public void Rebind(InstalledAppItem item) => Item = item;

    // ── Background execution ──────────────────────────────────────────────────

    partial void OnSelectedBackgroundModeChanged(BackgroundModeOption? value)
    {
        if (_applyingStoredMode || value is null) return;

        var family = Item.App.PackageFamilyName;
        if (string.IsNullOrWhiteSpace(family)) return;

        if (_service.BackgroundAccess.Set(family, value.Mode))
            Status = $"Background permission set to “{value.Label}”.";
        else
            _shell.ShowError($"Couldn't change the background permission for {Item.Name}.");
    }

    // ── Terminate / Repair / Reset ────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanRunOperation))]
    private void Terminate()
    {
        if (!TryBegin(out var ops)) return;

        var task = new TerminateAppTask(ops, Item.App);
        Run(task, () => Status = task.Killed == 0
            ? $"{Item.Name} wasn't running."
            : $"Stopped {task.Killed} {(task.Killed == 1 ? "process" : "processes")}.");
    }

    [RelayCommand(CanExecute = nameof(CanRunOperation))]
    private void Repair()
    {
        if (!TryBegin(out var ops)) return;

        var task = new RepairAppTask(ops, Item.App);
        Run(task, () => Report(task.Result, $"{Item.Name} was repaired.", "repair"));
    }

    [RelayCommand(CanExecute = nameof(CanRunOperation))]
    private async Task Reset()
    {
        if (_ops is null || IsBusy) return;

        var confirmed = await _shell.ConfirmAsync(
            $"Reset {Item.Name}?",
            "This permanently deletes the app's data, settings and sign-in details, then reinstalls it " +
            "from scratch. The app itself stays installed.",
            "Reset", "Cancel");
        if (!confirmed) return;

        if (!TryBegin(out var ops)) return;

        var task = new ResetAppTask(ops, Item.App);
        Run(task, () => Report(task.Result, $"{Item.Name} was reset.", "reset"));
    }

    // ── Move ──────────────────────────────────────────────────────────────────

    private void LoadVolumes()
    {
        if (_ops is null) return;

        var task = new LoadAppVolumesTask(_ops);
        _shell.QueueBackgroundTask(task, onComplete: ok =>
        {
            if (!ok) return;
            Volumes.Clear();
            foreach (var volume in task.Result) Volumes.Add(volume);

            // Pre-select the drive the package is on today, so the dropdown reads as "where it is".
            SelectedVolume  = task.Result.FirstOrDefault(CurrentlyHosts) ?? task.Result.FirstOrDefault();
            HasMoveTarget   = task.Result.Count > 1;
            MoveCommand.NotifyCanExecuteChanged();
        });
    }

    /// <summary>
    /// More than one drive can host packages. False means there is nowhere to move to — the pane says so
    /// rather than offering a dropdown with a single, pointless entry.
    /// </summary>
    [ObservableProperty] private bool _hasMoveTarget;

    private bool CurrentlyHosts(AppVolume volume)
    {
        var path = Item.App.InstallLocation;
        return !string.IsNullOrWhiteSpace(path)
               && !string.IsNullOrWhiteSpace(volume.MountPoint)
               && path.StartsWith(volume.MountPoint, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True once a drive other than the current one is picked — moving in place is a no-op.</summary>
    public bool CanMove => CanRunOperation() && SelectedVolume is { } v && !CurrentlyHosts(v);

    partial void OnSelectedVolumeChanged(AppVolume? value) => MoveCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanMove))]
    private void Move()
    {
        if (SelectedVolume is not { } target || !TryBegin(out var ops)) return;

        var task = new MoveAppTask(ops, Item.App, target);
        Run(task, () => Report(task.Result,
                               $"{Item.Name} now lives on {target.MountPoint.TrimEnd('\\')}.", "move"));
    }

    // ── Add-ons ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private void RefreshAddOns()
    {
        if (_ops is null) { AddOnsScanned = true; return; }

        var task = new LoadAddOnsTask(_ops, Item.App);
        _shell.QueueBackgroundTask(task, onComplete: ok =>
        {
            if (ok)
            {
                AddOns.Clear();
                foreach (var addOn in task.Result) AddOns.Add(addOn);
            }
            AddOnsScanned = true;
        });
    }

    private bool CanRemoveAddOn(AppAddOn? addOn) => addOn is not null && CanRunOperation();

    [RelayCommand(CanExecute = nameof(CanRemoveAddOn))]
    private async Task RemoveAddOn(AppAddOn? addOn)
    {
        if (addOn is null || _ops is null || IsBusy) return;

        var confirmed = await _shell.ConfirmAsync(
            $"Remove {addOn.Name}?",
            $"This removes the add-on only — {Item.Name} stays installed.",
            "Remove", "Cancel");
        if (!confirmed) return;

        if (!TryBegin(out var ops)) return;

        var task = new RemoveAddOnTask(ops, addOn);
        Run(task, () =>
        {
            Report(task.Result, $"{addOn.Name} was removed.", "removal");
            if (task.Result.Success) AddOns.Remove(addOn);
        });
    }

    // ── Shared plumbing ───────────────────────────────────────────────────────

    /// <summary>Nothing may run without a Store backend, and never two operations at once.</summary>
    private bool CanRunOperation() => _ops is not null && !IsBusy;

    private bool TryBegin(out IStoreAppOperations ops)
    {
        ops = _ops!;
        if (_ops is null || IsBusy) return false;

        Status = null;
        IsBusy = true;
        return true;
    }

    /// <summary>Queues <paramref name="task"/> and reports through <paramref name="onDone"/> when it lands.</summary>
    private void Run(IBackgroundTask task, Action onDone)
    {
        _shell.QueueBackgroundTask(task, onComplete: ok =>
        {
            IsBusy = false;
            if (ok) onDone();
            else _shell.ShowError($"{task.Description} didn't finish.");
        });
    }

    private void Report(AppOperationResult result, string success, string what)
    {
        if (result.Success) Status = success;
        else _shell.ShowError($"Couldn't {what} {Item.Name}: {result.Error}");
    }

    partial void OnIsBusyChanged(bool value)
    {
        TerminateCommand.NotifyCanExecuteChanged();
        RepairCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        MoveCommand.NotifyCanExecuteChanged();
        RemoveAddOnCommand.NotifyCanExecuteChanged();
    }
}
