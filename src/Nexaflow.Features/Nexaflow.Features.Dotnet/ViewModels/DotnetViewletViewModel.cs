using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Dotnet.ClientTools;
using Nexaflow.Features.Dotnet.Models;
using Nexaflow.Features.Dotnet.Services;

namespace Nexaflow.Features.Dotnet.ViewModels;

/// <summary>
/// Backs the .NET folder viewlet: a SingleBar toolbar of dotnet verbs plus a NuGet-update caution.
/// Output isn't shown inline — success is a transient ✓, failure is surfaced through the shell's
/// error toast + notification. The NuGet check runs as a shell background task (reported in the
/// activity ticker; it no longer blocks the AI input bar).
/// </summary>
public sealed partial class DotnetViewletViewModel : ObservableObject
{
    private readonly IShellServices _shell;
    private readonly string _folderPath;
    private CancellationTokenSource? _nugetCts;

    public ObservableCollection<DotnetTarget> Targets { get; } = [];

    public bool ShowTargetPicker => Targets.Count > 1;
    public bool ShowTargetLabel  => Targets.Count == 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private DotnetTarget? _selectedTarget;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _isBusy;

    /// <summary>Transient status glyph: "✓" on success, "✗" on failure, empty otherwise.</summary>
    [ObservableProperty] private string _statusGlyph = string.Empty;

    /// <summary>While a verb runs: the gerund label, e.g. "Building".</summary>
    [ObservableProperty] private string _runningLabel = string.Empty;

    /// <summary>Latest output line from the running command, for inline display (empty until one arrives).</summary>
    [ObservableProperty] private string _progressDetail = string.Empty;

    [ObservableProperty] private bool _hasUpdates;
    [ObservableProperty] private string _updatesTooltip = string.Empty;

    /// <summary>Count behind <see cref="HasUpdates"/>, surfaced in the AI context line.</summary>
    private int _outdatedCount;

    public DotnetViewletViewModel(IShellServices shell, string folderPath)
    {
        _shell = shell;
        _folderPath = folderPath;

        foreach (var t in DotnetTargetScanner.Scan(folderPath))
            Targets.Add(t);

        OnPropertyChanged(nameof(ShowTargetPicker));
        OnPropertyChanged(nameof(ShowTargetLabel));

        // Solution preferred, else first project. The setter's change handler kicks off the
        // first NuGet check.
        SelectedTarget = Targets.FirstOrDefault(t => t.IsSolution) ?? Targets.FirstOrDefault();
    }

    // Selecting a different target invalidates the caution — clear it and re-check so we never
    // show stale data for the wrong target.
    partial void OnSelectedTargetChanged(DotnetTarget? value)
    {
        HasUpdates = false;
        UpdatesTooltip = string.Empty;
        if (value is not null)
            QueueNugetCheck();
    }

    private bool CanRun() => !IsBusy && SelectedTarget is not null;

    [RelayCommand(CanExecute = nameof(CanRun))] private Task Restore() => RunVerbAsync("restore");
    [RelayCommand(CanExecute = nameof(CanRun))] private Task Build()   => RunVerbAsync("build");
    [RelayCommand(CanExecute = nameof(CanRun))] private Task Run()     => RunVerbAsync("run");
    [RelayCommand(CanExecute = nameof(CanRun))] private Task Test()    => RunVerbAsync("test");
    [RelayCommand(CanExecute = nameof(CanRun))] private Task Clean()   => RunVerbAsync("clean");

    /// <summary>
    /// Runs a dotnet verb against the selected target and returns the captured result — or null when
    /// there's no target or a command is already running. Drives the same busy/✓/✗ UI for both the
    /// toolbar buttons (which ignore the return) and the AI tools (which feed the output back to the model).
    /// </summary>
    public async Task<DotnetCli.Result?> RunVerbAsync(string verb, CancellationToken ct = default)
    {
        var target = SelectedTarget;
        if (target is null || IsBusy)
            return null;

        IsBusy = true;
        StatusGlyph = string.Empty;
        RunningLabel = Gerund(verb);
        ProgressDetail = string.Empty;
        var progress = new Progress<string>(line => ProgressDetail = Truncate(line));
        try
        {
            var result = await DotnetCli.RunAsync(verb, target, _folderPath, progress, ct);
            if (result.Succeeded)
            {
                StatusGlyph = "✓";
                if (verb is "restore" or "build")
                    QueueNugetCheck();
            }
            else
            {
                StatusGlyph = "✗";
                _shell.ShowError($"dotnet {verb} failed ({target.DisplayName})");
                _shell.ShowNotification(Tail(result.Output));
            }
            return result;
        }
        finally
        {
            IsBusy = false;
            RunningLabel = string.Empty;
            ProgressDetail = string.Empty;
        }
    }

    // ── AI surface (merged into the file browser's context + tools by IViewletAiSurface) ──────────

    /// <summary>One-line summary of the .NET target + package state for the AI context.</summary>
    public string GetContext()
    {
        var target = SelectedTarget;
        if (target is null)
            return ".NET: no buildable target detected.";

        var sb = new StringBuilder($".NET: target '{target.DisplayName}'");
        if (Targets.Count > 1) sb.Append($" (of {Targets.Count} targets)");
        sb.Append('.');
        if (HasUpdates && _outdatedCount > 0)
            sb.Append($" {_outdatedCount} package update(s) available.");
        return sb.ToString();
    }

    /// <summary>Verb tools (build/test/restore/clean) plus a read-only outdated-package check.
    /// <c>run</c> is intentionally not exposed — it can launch a long-lived app that never exits.</summary>
    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new DotnetVerbTool(this, "dotnet_build",   "build",   "Build the .NET target"),
        new DotnetVerbTool(this, "dotnet_test",    "test",    "Run the .NET target's tests"),
        new DotnetVerbTool(this, "dotnet_restore", "restore", "Restore NuGet packages for the .NET target"),
        new DotnetVerbTool(this, "dotnet_clean",   "clean",   "Clean the .NET target's build outputs"),
        new DotnetOutdatedPackagesTool(this),
    ];

    /// <summary>Selects the target whose display name matches <paramref name="name"/> (case-insensitive)
    /// so a verb runs against it; falls back to the current selection when null/blank or unmatched.</summary>
    public DotnetTarget? ResolveTarget(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return SelectedTarget;
        return Targets.FirstOrDefault(t =>
            string.Equals(t.DisplayName, name, StringComparison.OrdinalIgnoreCase)) ?? SelectedTarget;
    }

    /// <summary>Runs an on-demand outdated-package check for the selected target (read-only).</summary>
    public Task<IReadOnlyList<NugetUpdateChecker.PackageUpdate>> CheckOutdatedAsync(CancellationToken ct)
        => SelectedTarget is { } target
            ? NugetUpdateChecker.CheckAsync(target, _folderPath, ct)
            : Task.FromResult<IReadOnlyList<NugetUpdateChecker.PackageUpdate>>([]);

    private static string Gerund(string verb) => verb switch
    {
        "restore" => "Restoring",
        "build"   => "Building",
        "run"     => "Running",
        "test"    => "Testing",
        "clean"   => "Cleaning",
        _         => char.ToUpperInvariant(verb[0]) + verb[1..],
    };

    private static string Truncate(string line, int max = 80)
    {
        line = line.Trim();
        return line.Length <= max ? line : line[..(max - 1)] + "…";
    }

    private async void QueueNugetCheck()
    {
        var target = SelectedTarget;
        if (target is null)
            return;

        // Abort any in-flight check (target switch / leaving the folder) before starting a new one.
        _nugetCts?.Cancel();
        _nugetCts?.Dispose();
        _nugetCts = new CancellationTokenSource();
        var token = _nugetCts.Token;

        // Settle delay: while the user is traversing folders the view unloads (cancelling the token)
        // before this elapses, so we never launch a check for a folder just passed through.
        try { await Task.Delay(1500, token); }
        catch (OperationCanceledException) { return; }

        var task = new NugetUpdateCheckTask(target, _folderPath);
        _shell.QueueBackgroundTask(task, onComplete: _ =>
        {
            // onComplete runs on the UI thread; ignore a result whose target is no longer selected.
            if (ReferenceEquals(task.Target, SelectedTarget))
                ApplyUpdates(task.Updates);
        }, ct: token);
    }

    /// <summary>Aborts any in-flight NuGet check; called when the viewlet's view unloads (folder change).</summary>
    public void CancelPending() => _nugetCts?.Cancel();

    private void ApplyUpdates(IReadOnlyList<NugetUpdateChecker.PackageUpdate> updates)
    {
        if (updates.Count == 0)
        {
            HasUpdates = false;
            UpdatesTooltip = string.Empty;
            _outdatedCount = 0;
            return;
        }

        var sb = new StringBuilder();
        foreach (var u in updates)
            sb.AppendLine($"{u.Name}: {u.Current} > {u.Latest}");

        UpdatesTooltip = sb.ToString().TrimEnd();
        _outdatedCount = updates.Count;
        HasUpdates = true;
    }

    /// <summary>Last <paramref name="maxLines"/> lines of command output — used for the error toast
    /// and for the AI tool result (a noisy build shouldn't flood the model).</summary>
    public static string Tail(string output, int maxLines = 30)
    {
        var lines = output.Replace("\r\n", "\n").TrimEnd().Split('\n');
        if (lines.Length <= maxLines)
            return output.Trim();
        return string.Join(Environment.NewLine, lines[^maxLines..]);
    }
}
