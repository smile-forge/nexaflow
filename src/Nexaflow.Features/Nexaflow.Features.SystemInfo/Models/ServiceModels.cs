using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.SystemInfo.Models;

/// <summary>
/// One Windows service in the list. Identity fields are immutable; <see cref="Status"/> and
/// <see cref="StartMode"/> are observable so a row updates in place after an elevated action without a
/// full re-scan. The Can* flags drive per-row button enablement and refresh with the status.
/// </summary>
public sealed partial class ServiceRow : ObservableObject
{
    /// <summary>Service key name (used for control); e.g. "Spooler".</summary>
    public string Name { get; }

    /// <summary>Friendly name shown to the user; e.g. "Print Spooler".</summary>
    public string DisplayName { get; }

    public string Description { get; }

    /// <summary>WMI AcceptPause — whether Pause/Continue are meaningful for this service.</summary>
    public bool CanPauseAndContinue { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(CanResume))]
    private string _status;

    /// <summary>Canonical startup mode: Automatic | AutomaticDelayed | Manual | Disabled (or Boot/System).</summary>
    [ObservableProperty]
    private string _startMode;

    public ServiceRow(string name, string displayName, string description,
                      bool canPauseAndContinue, string status, string startMode)
    {
        Name                = name;
        DisplayName         = displayName;
        Description         = description;
        CanPauseAndContinue = canPauseAndContinue;
        _status             = status;
        _startMode          = startMode;
    }

    public bool CanStart  => Status is "Stopped";
    public bool CanStop   => Status is "Running" or "Paused";
    public bool CanPause  => CanPauseAndContinue && Status is "Running";
    public bool CanResume => Status is "Paused";
}
