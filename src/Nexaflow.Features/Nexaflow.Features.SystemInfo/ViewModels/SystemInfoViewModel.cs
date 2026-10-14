using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Features.SystemInfo.Services;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.SystemInfo.ViewModels;

/// <summary>
/// The system-info dashboard: a set of section cards (OS, hardware, security, storage) gathered off the
/// UI thread and exposed to the LLM via <see cref="GetContext"/>. Re-gathered on demand via Refresh.
/// </summary>
public sealed partial class SystemInfoViewModel : ObservableObject, IPageViewModel
{
    private readonly IShellServices _shell;
    private readonly IAIService _ai;
    private readonly SystemInfoCollector _collector;

    /// <summary>The Local AI card's check of the machine, kept across refreshes so a run under way stays shown.</summary>
    private SystemInfoAction? _check;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextReady))]
    private bool _isLoading;

    /// <summary>The dashboard cards. The latest snapshot's plain text, kept for <see cref="GetContext"/>.</summary>
    public ObservableCollection<SystemInfoSection> Sections { get; } = [];

    private string _contextText = "System information: still gathering…";

    public SystemInfoViewModel(IShellServices shell, IAIService ai) : this(shell, ai, new SystemInfoCollector()) { }

    public SystemInfoViewModel(IShellServices shell, IAIService ai, SystemInfoCollector collector)
    {
        _shell     = shell;
        _ai        = ai;
        _collector = collector;
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        if (IsLoading) return;
        IsLoading = true;

        var task = new LoadSystemInfoTask(_collector, _ai);
        _shell.QueueBackgroundTask(task, onComplete: ok =>
        {
            // Marshalled to the UI thread by ShellServices.
            if (ok && task.Result is { } snapshot) Populate(snapshot, task.LocalAi);
            IsLoading = false;
        });
    }

    private void Populate(SystemInfoSnapshot snapshot, LocalAiSetup? localAi)
    {
        // A re-gather replaces every fact object, so the marks a "?" search left behind belong to rows
        // that no longer exist — the chip has to go with them.
        ClearSearch();

        // The machine can be checked only against models the workspace runs itself.
        if (snapshot.LocalAi is { } card && localAi is { Models.Count: > 0 })
            card.Action = _check ??= new SystemInfoAction(Str.Get("SystemInfo.LocalAi.Check"), new AsyncRelayCommand(CheckLocalAiAsync));

        Sections.Clear();
        foreach (var section in snapshot.Sections) Sections.Add(section);
        _contextText = snapshot.ToPlainText();
    }

    /// <summary>Checks the machine against the workspace's own models, the button saying what it is doing, and opens the
    /// report it writes.</summary>
    private async Task CheckLocalAiAsync()
    {
        var check = _check!;
        check.Outcome = null;
        var progress = new Progress<LocalAiCheckProgress>(p => check.Label = Checking(p));
        try
        {
            var done = await _ai.CheckLocalAiAsync(progress);
            (check.Outcome, check.OutcomeStatus) = done.Passed
                ? (Str.Get("SystemInfo.LocalAi.Check.Passed"), SystemInfoStatus.Good)
                : (Str.Get("SystemInfo.LocalAi.Check.NotPassed"), SystemInfoStatus.Warning);
            _shell.OpenTab("Markdown", new Dictionary<string, string> { ["path"] = done.ReportPath });
        }
        catch (InvalidOperationException ex)
        {
            (check.Outcome, check.OutcomeStatus) = (ex.Message, SystemInfoStatus.Bad);
        }
        finally
        {
            check.Label = Str.Get("SystemInfo.LocalAi.Check");
        }
    }

    private static string Checking(LocalAiCheckProgress progress) => progress.Stage switch
    {
        LocalAiCheckStage.Loading  => Str.Format("SystemInfo.LocalAi.Checking.Loading", progress.Model ?? ""),
        LocalAiCheckStage.Replying => Str.Get("SystemInfo.LocalAi.Checking.Replying"),
        LocalAiCheckStage.Speaking => Str.Get("SystemInfo.LocalAi.Checking.Speaking"),
        LocalAiCheckStage.Hearing  => Str.Get("SystemInfo.LocalAi.Checking.Hearing"),
        LocalAiCheckStage.Drawing  => Str.Get("SystemInfo.LocalAi.Checking.Drawing"),
        _                          => Str.Get("SystemInfo.LocalAi.Checking.Describing"),
    };

    // ── IPageViewModel ─────────────────────────────────────────────────────────
    public string GetContext() =>
        IsLoading && Sections.Count == 0
            ? "System information: still gathering…"
            : $"Device summary for {Environment.MachineName}:\n{_contextText}";

    /// <summary>Ready once the background gather has finished — success or failure (see Refresh's onComplete,
    /// which always clears IsLoading). Tied to IsLoading, not Sections, so a failed scan still releases the send.</summary>
    public bool IsContextReady => !IsLoading;
}
