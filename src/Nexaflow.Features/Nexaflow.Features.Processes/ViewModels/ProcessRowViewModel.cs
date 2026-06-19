using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Processes.Models;

namespace Nexaflow.Features.Processes.ViewModels;

/// <summary>
/// One row in the process tree. Pure display state plus tree links — the durable identity is the
/// <see cref="Pid"/>, so the same instance is reused across refreshes (its metric properties update in
/// place), which is what preserves selection and expansion. Actions live on
/// <see cref="ProcessesViewModel"/> and take the row as a parameter.
/// </summary>
public sealed partial class ProcessRowViewModel(int pid) : ObservableObject
{
    public int Pid { get; } = pid;

    /// <summary>Set during tree rebuild; not bound (the flat list is what the grid shows).</summary>
    public ProcessRowViewModel? Parent { get; set; }
    public List<ProcessRowViewModel> Children { get; } = [];

    /// <summary>Start time, when readable — guards against PID reuse across samples.</summary>
    public DateTime? StartTime { get; set; }

    public string Path { get; set; } = "";

    [ObservableProperty] private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuText))]
    private double _cpuPercent;

    [ObservableProperty] private long _privateBytes;
    [ObservableProperty] private long _workingSet;
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _company = "";
    [ObservableProperty] private bool _accessDenied;

    [ObservableProperty] private int _depth;
    [ObservableProperty] private bool _isExpanded;   // tree starts collapsed — expand on demand
    [ObservableProperty] private bool _hasChildren;

    /// <summary>CPU shown blank below 0.05% (matches Process Explorer's quiet idle rows).</summary>
    public string CpuText => CpuPercent < 0.05 ? "" : CpuPercent.ToString("0.0");

    public void Update(ProcessSample s)
    {
        Name         = s.Name;
        CpuPercent   = s.CpuPercent;
        PrivateBytes = s.PrivateBytes;
        WorkingSet   = s.WorkingSet;
        Description  = s.Description;
        Company      = s.Company;
        AccessDenied = s.AccessDenied;
        Path         = s.Path;
        StartTime    = s.StartTime;
    }
}
