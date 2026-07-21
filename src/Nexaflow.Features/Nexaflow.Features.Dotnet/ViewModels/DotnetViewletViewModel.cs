using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private CancellationTokenSource? _verbCts;

    public ObservableCollection<DotnetTarget> Targets { get; } = [];

    /// <summary>The projects of the selected solution that <c>dotnet run</c> could launch. Empty when the
    /// selected target is itself a project (it <em>is</em> the run target) or has nothing runnable.</summary>
    public ObservableCollection<DotnetTarget> RunnableProjects { get; } = [];

    public bool ShowTargetPicker => Targets.Count > 1;
    public bool ShowTargetLabel  => Targets.Count == 1;

    /// <summary>Show the caret beside Run only when the choice of project is genuinely open.</summary>
    public bool ShowStartupPicker => SelectedTarget is { IsSolution: true } && RunnableProjects.Count > 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenTargetCommand))]
    private DotnetTarget? _selectedTarget;

    /// <summary>The project <c>Run</c> launches when a solution is selected — <c>dotnet run</c> cannot take
    /// a solution. Guessed by <see cref="SolutionReader.RunnableProjects"/>, overridden by the user's
    /// remembered pick.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyPropertyChangedFor(nameof(RunTooltip))]
    private DotnetTarget? _startupProject;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelVerbCommand))]
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

    // Selecting a different target invalidates the caution and the run target — clear both and re-derive
    // so we never show stale data for the wrong target.
    partial void OnSelectedTargetChanged(DotnetTarget? value)
    {
        HasUpdates = false;
        UpdatesTooltip = string.Empty;
        ResolveRunnableProjects(value);
        if (value is not null)
            QueueNugetCheck();
    }

    /// <summary>Rebuilds <see cref="RunnableProjects"/> for the newly selected target and picks the startup
    /// project: whatever the user last chose for this solution, else the best guess.</summary>
    private void ResolveRunnableProjects(DotnetTarget? target)
    {
        RunnableProjects.Clear();

        if (target is { IsSolution: true })
            foreach (var p in SolutionReader.RunnableProjects(target.Path))
                RunnableProjects.Add(p);

        StartupProject = Remembered(target) ?? RunnableProjects.FirstOrDefault();
        OnPropertyChanged(nameof(ShowStartupPicker));
        OnPropertyChanged(nameof(RunTooltip));
    }

    private DotnetTarget? Remembered(DotnetTarget? target)
    {
        if (target is not { IsSolution: true } || !StartupProjectCache.TryGet(target.Path, out var stored))
            return null;

        // Only honour a remembered pick that's still a runnable project of this solution.
        return RunnableProjects.FirstOrDefault(
            p => string.Equals(p.Path, stored, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Sets the startup project <em>because the user picked it</em>, and remembers it for this
    /// solution. Distinct from assigning <see cref="StartupProject"/>, which also happens for a guess —
    /// persisting a guess would freeze it.</summary>
    public void ChooseStartupProject(DotnetTarget project)
    {
        StartupProject = project;
        if (SelectedTarget is { IsSolution: true } solution)
            StartupProjectCache.Store(solution.Path, project.Path);
    }

    private bool CanRun() => !IsBusy && SelectedTarget is not null;

    private bool HasTarget() => SelectedTarget is not null;

    /// <summary>Run needs an actual project: the selected target itself, or — for a solution — a resolved
    /// startup project. A solution with nothing runnable in it leaves Run disabled.</summary>
    private bool CanRunApp() => !IsBusy && RunTarget is not null;

    private DotnetTarget? RunTarget =>
        SelectedTarget is { IsSolution: true } ? StartupProject : SelectedTarget;

    public string RunTooltip => SelectedTarget switch
    {
        null                                        => "dotnet run",
        { IsSolution: true } s when RunTarget is null => $"No runnable project in {s.DisplayName}",
        _                                           => $"dotnet run — {RunTarget!.DisplayName}",
    };

    [RelayCommand(CanExecute = nameof(CanRun))]    private Task Restore() => RunVerbAsync("restore");
    [RelayCommand(CanExecute = nameof(CanRun))]    private Task Build()   => RunVerbAsync("build");
    [RelayCommand(CanExecute = nameof(CanRunApp))] private Task Run()     => RunVerbAsync("run");
    [RelayCommand(CanExecute = nameof(CanRun))]    private Task Test()    => RunVerbAsync("test");
    [RelayCommand(CanExecute = nameof(CanRun))]    private Task Clean()   => RunVerbAsync("clean");

    /// <summary>Stops the running verb by killing its process tree — for <c>run</c> that means closing the
    /// app it launched, which is the only way out of a <c>run</c> now that it has no inactivity watchdog.</summary>
    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void CancelVerb() => _verbCts?.Cancel();

    /// <summary>Opens the selected target (solution/project) file with its default OS handler
    /// (e.g. Visual Studio for a <c>.sln</c>/<c>.slnx</c>).</summary>
    [RelayCommand(CanExecute = nameof(HasTarget))]
    private void OpenTarget()
    {
        if (SelectedTarget is not { } target) return;
        try
        {
            Process.Start(new ProcessStartInfo(target.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _shell.ShowError($"Couldn't open {target.DisplayName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs a dotnet verb against the selected target and returns the captured result — or null when
    /// there's no target or a command is already running. Drives the same busy/✓/✗ UI for both the
    /// toolbar buttons (which ignore the return) and the AI tools (which feed the output back to the model).
    /// </summary>
    public async Task<DotnetCli.Result?> RunVerbAsync(string verb, CancellationToken ct = default)
    {
        // `run` launches a project, so a selected solution resolves to its startup project; every other
        // verb takes the solution itself.
        var target = verb == "run" ? RunTarget : SelectedTarget;
        if (target is null || IsBusy)
            return null;

        IsBusy = true;
        StatusGlyph = string.Empty;
        RunningLabel = Gerund(verb);
        ProgressDetail = string.Empty;
        var progress = new Progress<string>(line => ProgressDetail = Truncate(line));

        // Linked so both the caller (an AI tool) and the Stop button can end the process tree.
        _verbCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var result = await DotnetCli.RunAsync(verb, target, _folderPath, progress, _verbCts.Token);
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
        catch (OperationCanceledException)
        {
            // The user pressed Stop (or the folder is being mutated). Deliberate — no error toast, and the
            // exception must not escape or it faults the command's task unobserved.
            StatusGlyph = string.Empty;
            return new DotnetCli.Result(-1, $"dotnet {verb} cancelled.");
        }
        finally
        {
            _verbCts?.Dispose();
            _verbCts = null;
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

    /// <summary>Runs an on-demand outdated-package check for the selected target (read-only). Always
    /// fresh (the agent asked explicitly), but refreshes the shared daily cache on success.</summary>
    public async Task<IReadOnlyList<NugetUpdateChecker.PackageUpdate>> CheckOutdatedAsync(CancellationToken ct)
    {
        if (SelectedTarget is not { } target) return [];
        var result = await NugetUpdateChecker.CheckAsync(target, ct);
        if (result.Checked)
            NugetCheckCache.Store(target.Path, result.Updates);
        return result.Updates;
    }

    /// <summary>The progress label for a verb — "build" reads as "Building" while it runs.</summary>
    internal static string Gerund(string verb) => verb switch
    {
        "restore" => "Restoring",
        "build"   => "Building",
        "run"     => "Running",
        "test"    => "Testing",
        "clean"   => "Cleaning",
        _         => char.ToUpperInvariant(verb[0]) + verb[1..],
    };

    /// <summary>Clips one output line to the width the inline progress detail can show.</summary>
    internal static string Truncate(string line, int max = 80)
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

        // A recent result (checked < 24h ago) is reused as-is — no settle delay, no `dotnet list` process.
        if (NugetCheckCache.TryGet(target.Path, out var cached))
        {
            ApplyUpdates(cached);
            return;
        }

        // Settle delay: while the user is traversing folders the view unloads (cancelling the token)
        // before this elapses, so we never launch a check for a folder just passed through.
        try { await Task.Delay(1500, token); }
        catch (OperationCanceledException) { return; }

        var task = new NugetUpdateCheckTask(target);
        _shell.QueueBackgroundTask(task, onComplete: _ =>
        {
            // onComplete runs on the UI thread; ignore a result whose target is no longer selected.
            if (!ReferenceEquals(task.Target, SelectedTarget)) return;
            // Only a real result (the command ran) is cached/applied — a "not restored yet" failure is
            // left uncached so the next visit re-checks once the target has been restored.
            if (task.Checked)
            {
                NugetCheckCache.Store(task.Target.Path, task.Updates);
                ApplyUpdates(task.Updates);
            }
        }, ct: token);
    }

    /// <summary>Aborts any in-flight NuGet check; called when the viewlet's view unloads (folder change).
    /// A running verb is deliberately left alone — navigating away shouldn't kill a build you started, and
    /// an app you launched with Run should keep running.</summary>
    public void CancelPending() => _nugetCts?.Cancel();

    /// <summary>
    /// Releases this viewlet's hold on the folder before it's mutated/deleted. The NuGet check runs from a
    /// neutral working directory (see <see cref="DotnetCli.RunListOutdatedAsync"/>) so it never locks the
    /// folder and only needs cancelling. A <em>verb</em> does: its working directory is the folder, which
    /// Windows locks against rename/delete, so its process tree has to go. Cancelling runs the tree-kill
    /// synchronously inside <c>Cancel()</c>, hence the hop off the UI thread.
    /// </summary>
    public Task QuiesceAsync(CancellationToken ct)
    {
        _nugetCts?.Cancel();
        var verbCts = _verbCts;
        return verbCts is null ? Task.CompletedTask : Task.Run(() =>
        {
            try { verbCts.Cancel(); } catch (ObjectDisposedException) { /* finished on its own */ }
        }, ct);
    }

    /// <summary>Publishes an outdated-package result as the caution badge + its per-package tooltip; an empty
    /// result clears it (so a target with nothing outstanding never shows a stale warning).</summary>
    internal void ApplyUpdates(IReadOnlyList<NugetUpdateChecker.PackageUpdate> updates)
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
