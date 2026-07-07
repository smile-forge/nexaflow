using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Dotnet.Models;

namespace Nexaflow.Features.Dotnet.Services;

/// <summary>
/// Runs a one-shot <c>dotnet</c> command against a <see cref="DotnetTarget"/> and captures its
/// combined stdout/stderr. Fire-and-forget — interactive output belongs in the Console feature,
/// not here.
/// </summary>
public static class DotnetCli
{
    public sealed record Result(int ExitCode, string Output)
    {
        public bool Succeeded => ExitCode == 0;
    }

    /// <summary>Killed after this long without any new stdout/stderr data, in case the process hangs.</summary>
    private const int InactivityTimeoutMs = 60_000;

    /// <summary>Runs <c>dotnet &lt;verb&gt;</c> for a build/restore/run/test/clean against the target.</summary>
    /// <param name="onLine">Receives each non-empty output line as it arrives (for live progress).</param>
    public static Task<Result> RunAsync(
        string verb, DotnetTarget target, string workingDir,
        IProgress<string>? onLine = null, CancellationToken ct = default)
    {
        var args = new List<string> { verb };
        // `dotnet run` needs a project, never a solution; everything else takes either.
        if (verb == "run")
        {
            if (!target.IsSolution)
            {
                args.Add("--project");
                args.Add(target.Path);
            }
            // Solution selected → run in the folder and let the CLI resolve (errors if ambiguous).
        }
        else
        {
            args.Add(target.Path);
        }

        return RunRawAsync(args, workingDir, onLine, ct);
    }

    /// <summary>Runs <c>dotnet list &lt;target&gt; package --outdated</c>. This read-only check is short
    /// and side-effect-free, so on cancellation it is abandoned (left to exit on its own) rather than
    /// force-killed — a tree-kill here runs synchronously on the caller's (UI) thread and stalls folder
    /// navigation, and throws a flurry of first-chance Win32Exceptions for transient child processes.</summary>
    /// <param name="onStarted">Invoked with the started process so a caller can force-kill it out of band
    /// (the viewlet quiesce path does this — cancellation alone abandons rather than kills this check, so its
    /// working-directory lock would otherwise linger).</param>
    public static Task<Result> RunListOutdatedAsync(
        DotnetTarget target, string workingDir, CancellationToken ct = default,
        Action<Process>? onStarted = null)
        => RunRawAsync(["list", target.Path, "package", "--outdated"], workingDir, onLine: null, ct,
                       killTreeOnCancel: false, onStarted);

    /// <param name="killTreeOnCancel">When true (build/run/test/clean), cancelling <paramref name="ct"/>
    /// force-kills the process tree. When false, cancellation just stops waiting and the process is left
    /// to exit naturally — see <see cref="RunListOutdatedAsync"/>.</param>
    /// <param name="onStarted">Invoked with the process immediately after it starts.</param>
    private static async Task<Result> RunRawAsync(
        IReadOnlyList<string> args, string workingDir, IProgress<string>? onLine, CancellationToken ct,
        bool killTreeOnCancel = true, Action<Process>? onStarted = null)
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

        var output = new StringBuilder();
        long lastTick = Environment.TickCount64;

        void OnData(DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            Interlocked.Exchange(ref lastTick, Environment.TickCount64);
            lock (output) output.AppendLine(e.Data);
            if (e.Data.Length > 0) onLine?.Report(e.Data);
        }

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => OnData(e);
        proc.ErrorDataReceived  += (_, e) => OnData(e);

        try
        {
            proc.Start();
            onStarted?.Invoke(proc);
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            return new Result(-1, $"Failed to start dotnet: {ex.Message}");
        }

        // Caller cancellation (e.g. the user navigated away): for killable verbs, kill the process tree
        // (the WaitForExitAsync(ct) below then throws OperationCanceledException, which propagates). For
        // the read-only outdated check we register nothing — the process is abandoned, not killed, so
        // Cancel() can't block the UI thread inside a tree-kill.
        using var killReg = killTreeOnCancel
            ? ct.Register(() => { try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ } })
            : default;

        // Watchdog: kill the process if it goes silent for InactivityTimeoutMs.
        var timedOut = false;
        using var watchdogCts = new CancellationTokenSource();
        var watchdog = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await Task.Delay(1_000, watchdogCts.Token);
                    if (Environment.TickCount64 - Interlocked.Read(ref lastTick) > InactivityTimeoutMs)
                    {
                        timedOut = true;
                        try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { /* completed normally */ }
        }, watchdogCts.Token);

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        finally
        {
            watchdogCts.Cancel();
            try { await watchdog; } catch { /* ignore */ }
        }

        string text;
        lock (output) text = output.ToString();
        if (timedOut)
            text += $"{Environment.NewLine}[killed: no output for {InactivityTimeoutMs / 1000}s]";

        return new Result(timedOut ? -1 : proc.ExitCode, text);
    }
}
