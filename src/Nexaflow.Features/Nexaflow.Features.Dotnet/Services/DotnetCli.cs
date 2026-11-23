using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Dotnet.Models;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Features.Dotnet.Services;

/// <summary>
/// Runs a one-shot <c>dotnet</c> command against a <see cref="DotnetTarget"/> and captures its combined stdout/stderr, in
/// the order it was written. A folder on this PC runs this PC's <c>dotnet</c>; a folder on a location runs the
/// location host's own (<see cref="IProgramHost"/>), handed the host's paths, so what a build writes keeps the names and
/// permissions the host gives it. Fire-and-forget — interactive output belongs in the Console feature, not here.
/// </summary>
public static class DotnetCli
{
    public sealed record Result(int ExitCode, string Output)
    {
        public bool Succeeded => ExitCode == 0;

        /// <summary>The system's reason <c>dotnet</c> could not be started, when it could not; <see cref="Output"/>
        /// is then empty.</summary>
        public string? StartError { get; init; }

        /// <summary>The seconds of silence after which the watchdog killed the process, when it did.</summary>
        public int? SilenceTimeoutSeconds { get; init; }

        /// <summary><see cref="Output"/> as the model reads it, with why the command ended early noted in English.</summary>
        public string Transcript => TranscriptWith(
            error => $"Failed to start dotnet: {error}",
            seconds => $"[killed: no output for {seconds}s]");

        /// <summary><see cref="Output"/> with why the command ended early, in the reader's words:
        /// <paramref name="startFailed"/> stands in for it when <c>dotnet</c> never started, and
        /// <paramref name="silenceKilled"/> follows it when the watchdog ended the process. The model's
        /// <see cref="Transcript"/> and the person's failure notification share this one shape.</summary>
        public string TranscriptWith(Func<string, string> startFailed, Func<int, string> silenceKilled)
            => StartError is { } error
                ? startFailed(error)
                : SilenceTimeoutSeconds is { } seconds
                    ? $"{Output}{Environment.NewLine}{silenceKilled(seconds)}"
                    : Output;
    }

    /// <summary>Killed after this long without any new stdout/stderr data, in case the process hangs.</summary>
    private const int InactivityTimeoutMs = 60_000;

    /// <summary>Disables the inactivity watchdog for a verb that is legitimately silent (see <c>run</c>).</summary>
    private const int NoInactivityTimeout = 0;

    /// <summary>Runs <c>dotnet &lt;verb&gt;</c> for a build/restore/run/test/clean against the target.</summary>
    /// <param name="target">For <c>run</c> this must be a <em>project</em> — the CLI cannot run a solution.
    /// The caller resolves a solution to its startup project first.</param>
    /// <param name="workingDir">The folder the command runs in, as the file system names it.</param>
    /// <param name="host">The location's host the folder is on, which runs its own <c>dotnet</c>; null for a folder of
    /// this PC.</param>
    /// <param name="onLine">Receives each non-empty output line as it arrives (for live progress).</param>
    public static Task<Result> RunAsync(
        string verb, DotnetTarget target, string workingDir, IProgramHost? host,
        IProgress<string>? onLine = null, CancellationToken ct = default)
    {
        if (verb == "run" && target.IsSolution)
            return Task.FromResult(new Result(-1, $"'{target.DisplayName}' is a solution; dotnet run needs a project."));
        if (TargetPath(target, host) is not { } path)
            return Task.FromResult(new Result(-1, string.Empty) { StartError = $"'{target.Path}' is not on {host!.Label}." });

        // A launched GUI app writes nothing to stdout, so the watchdog would kill it — and the user's app with it — a
        // minute after a perfectly good launch. Stopping a run is the caller's job (cancel the token); the process tree is
        // killed then.
        return verb == "run"
            ? Run(["run", "--project", path], workingDir, host, onLine, ct, inactivityTimeoutMs: NoInactivityTimeout)
            : Run([verb, path], workingDir, host, onLine, ct);
    }

    /// <summary>Runs <c>dotnet list &lt;target&gt; package --outdated</c> against the target.
    /// <para>
    /// On this PC it runs from a <b>neutral working directory</b>, not the project's folder: the <c>dotnet</c> host and the
    /// MSBuild/compiler nodes it spawns inherit their CWD, and a process's CWD locks that directory against rename/delete
    /// on Windows — pointing them at a temp dir instead means this passive check never holds the folder hostage (so it can
    /// just be cancelled, never force-killed). The check is read-only and short, so on cancellation the process is
    /// abandoned (left to exit on its own). On a location's host nothing is locked so, and it runs in the target's folder.
    /// </para></summary>
    public static Task<Result> RunListOutdatedAsync(DotnetTarget target, IProgramHost? host, CancellationToken ct = default)
    {
        if (host is null)
            return RunHereAsync(["list", target.Path, "package", "--outdated"], Path.GetTempPath(), onLine: null, ct,
                                killTreeOnCancel: false);
        if (TargetPath(target, host) is not { } path || Path.GetDirectoryName(target.Path) is not { } folder)
            return Task.FromResult(new Result(-1, string.Empty) { StartError = $"'{target.Path}' is not on {host.Label}." });
        return RunOnHostAsync(host, ["list", path, "package", "--outdated"], folder, onLine: null, ct, InactivityTimeoutMs);
    }

    /// <summary>The target as the <c>dotnet</c> that runs it names it: its path here, the host's own path on a location;
    /// null for a target that is not on the host.</summary>
    private static string? TargetPath(DotnetTarget target, IProgramHost? host)
        => host is null ? target.Path : host.HostPathOf(target.Path);

    private static Task<Result> Run(
        IReadOnlyList<string> args, string workingDir, IProgramHost? host, IProgress<string>? onLine, CancellationToken ct,
        int inactivityTimeoutMs = InactivityTimeoutMs)
        => host is null
            ? RunHereAsync(args, workingDir, onLine, ct, inactivityTimeoutMs: inactivityTimeoutMs)
            : RunOnHostAsync(host, args, workingDir, onLine, ct, inactivityTimeoutMs);

    /// <param name="killTreeOnCancel">When true (build/run/test/clean), cancelling <paramref name="ct"/>
    /// force-kills the process tree. When false, cancellation just stops waiting and the process is left
    /// to exit naturally — see <see cref="RunListOutdatedAsync"/>.</param>
    /// <param name="inactivityTimeoutMs">Kill the process after this long with no output.
    /// <see cref="NoInactivityTimeout"/> runs no watchdog at all.</param>
    private static async Task<Result> RunHereAsync(
        IReadOnlyList<string> args, string workingDir, IProgress<string>? onLine, CancellationToken ct,
        bool killTreeOnCancel = true, int inactivityTimeoutMs = InactivityTimeoutMs)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory       = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        var output = new Transcript(onLine);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) output.Add(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) output.Add(e.Data); };

        try
        {
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            return new Result(-1, string.Empty) { StartError = ex.Message };
        }

        // Caller cancellation (e.g. the user navigated away): for killable verbs, kill the process tree
        // (the WaitForExitAsync(ct) below then throws OperationCanceledException, which propagates). For
        // the read-only outdated check we register nothing — the process is abandoned, not killed, so
        // Cancel() can't block the UI thread inside a tree-kill.
        using var killReg = killTreeOnCancel
            ? ct.Register(() => Kill(proc))
            : default;

        // Skipped entirely for a verb whose silence is expected (run) — there, only cancellation ends the process.
        using var watchdog = new SilenceWatchdog(output, inactivityTimeoutMs, () => Kill(proc));
        try
        {
            await proc.WaitForExitAsync(ct);
        }
        finally
        {
            await watchdog.StopAsync();
        }

        return new Result(watchdog.Fired ? -1 : proc.ExitCode, output.Text)
        {
            SilenceTimeoutSeconds = watchdog.Fired ? inactivityTimeoutMs / 1000 : null,
        };

        static void Kill(Process proc)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }
    }

    /// <summary>Runs <c>dotnet</c> on a location's host in the folder given, its output read line by line as the host
    /// streams it, under the same watchdog as a run here. A <c>dotnet</c> the host does not have is a start error.</summary>
    private static async Task<Result> RunOnHostAsync(
        IProgramHost host, IReadOnlyList<string> args, string workingDir, IProgress<string>? onLine, CancellationToken ct,
        int inactivityTimeoutMs)
    {
        var output = new Transcript(onLine);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var watchdog = new SilenceWatchdog(output, inactivityTimeoutMs, stopping.Cancel);
        try
        {
            var exit = await host.RunProgramAsync(new ProgramRun("dotnet", args, workingDir), output, stopping.Token);
            return new Result(exit.ExitCode, output.Text);
        }
        catch (OperationCanceledException) when (watchdog.Fired && !ct.IsCancellationRequested)
        {
            return new Result(-1, output.Text) { SilenceTimeoutSeconds = inactivityTimeoutMs / 1000 };
        }
        catch (LocationException ex)
        {
            return new Result(-1, string.Empty) { StartError = ex.Message };
        }
        finally
        {
            await watchdog.StopAsync();
        }
    }

    /// <summary>The command's output in the order it was written, stdout and stderr together, and when it last wrote; each
    /// non-empty line is passed on for live progress. Taken in from any thread.</summary>
    private sealed class Transcript(IProgress<string>? onLine) : IProgress<string>
    {
        private readonly StringBuilder _text = new();
        private long _lastTick = Environment.TickCount64;

        public string Text { get { lock (_text) return _text.ToString(); } }

        public long LastTick => Interlocked.Read(ref _lastTick);

        public void Add(string line)
        {
            Interlocked.Exchange(ref _lastTick, Environment.TickCount64);
            lock (_text) _text.AppendLine(line);
            if (line.Length > 0) onLine?.Report(line);
        }

        void IProgress<string>.Report(string value) => Add(value);
    }

    /// <summary>Ends a command that has written nothing for the timeout — a hung process — by calling the end given once;
    /// a timeout of <see cref="NoInactivityTimeout"/> watches nothing.</summary>
    private sealed class SilenceWatchdog : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _watching;
        private volatile bool _fired;

        public SilenceWatchdog(Transcript output, int timeoutMs, Action end)
        {
            _watching = timeoutMs <= NoInactivityTimeout ? Task.CompletedTask : Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        await Task.Delay(1_000, _stop.Token);
                        if (Environment.TickCount64 - output.LastTick <= timeoutMs) continue;
                        _fired = true;
                        end();
                        return;
                    }
                }
                catch (OperationCanceledException) { /* the command ended first */ }
            });
        }

        /// <summary>True once the watchdog has ended the command.</summary>
        public bool Fired => _fired;

        public async Task StopAsync()
        {
            await _stop.CancelAsync();
            try { await _watching; } catch { /* ignore */ }
        }

        public void Dispose() => _stop.Dispose();
    }
}
