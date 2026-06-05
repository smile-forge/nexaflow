using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Features.SystemInfo.Services;

namespace Nexaflow.Features.SystemInfo.ViewModels;

/// <summary>
/// The system-info dashboard: a set of section cards (OS, hardware, security, storage) gathered off the
/// UI thread and exposed to the LLM via <see cref="GetContext"/>. Re-gathered on demand via Refresh.
/// </summary>
public sealed partial class SystemInfoViewModel : ObservableObject, IPageViewModel
{
    private readonly IShellServices _shell;
    private readonly SystemInfoCollector _collector;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextReady))]
    private bool _isLoading;

    /// <summary>The dashboard cards. The latest snapshot's plain text, kept for <see cref="GetContext"/>.</summary>
    public ObservableCollection<SystemInfoSection> Sections { get; } = [];

    private string _contextText = "System information: still gathering…";

    public SystemInfoViewModel(IShellServices shell) : this(shell, new SystemInfoCollector()) { }

    public SystemInfoViewModel(IShellServices shell, SystemInfoCollector collector)
    {
        _shell     = shell;
        _collector = collector;
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        if (IsLoading) return;
        IsLoading = true;

        var task = new LoadSystemInfoTask(_collector);
        _shell.QueueBackgroundTask(task, onComplete: ok =>
        {
            // Marshalled to the UI thread by ShellServices.
            if (ok && task.Result is { } snapshot) Populate(snapshot);
            IsLoading = false;
        });
    }

    private void Populate(SystemInfoSnapshot snapshot)
    {
        Sections.Clear();
        foreach (var section in snapshot.Sections) Sections.Add(section);
        _contextText = snapshot.ToPlainText();
    }

    // ── IPageViewModel ─────────────────────────────────────────────────────────
    public string GetContext() =>
        IsLoading && Sections.Count == 0
            ? "System information: still gathering…"
            : $"Device summary for {Environment.MachineName}:\n{_contextText}";

    /// <summary>Ready once the background gather has finished — success or failure (see Refresh's onComplete,
    /// which always clears IsLoading). Tied to IsLoading, not Sections, so a failed scan still releases the send.</summary>
    public bool IsContextReady => !IsLoading;
}
