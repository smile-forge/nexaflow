using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Nexaflow.IO.Common.Logging;

namespace Nexaflow.Session.Services.Logging;

/// <summary>
/// The session's log, kept while Options → Shell → Enable safety logging is on (<see cref="ShellConfig.SafetyLogging"/>): one file
/// per session in the <c>logs</c> folder under <see cref="AppDataFolders.LocalDir"/> (<c>%LOCALAPPDATA%\Smile\nexaflow\logs</c>),
/// taking every entry at <see cref="LogLevel.Information"/> and above, from the first step of startup to the last of shutdown — the
/// startup milestones, every load or activation failure the shell meets between, a one-line account of each fault the crash log
/// takes, and each AI host that ended without being asked to. A session with no <c>shutdown complete</c> line ended in a crash or
/// was killed. Files are kept and capped by <see cref="SessionLogFile"/>; the AI host's log, and its error output, go into the same
/// folder while this is on (<see cref="Folder"/>).
/// <para>
/// An entry is one line: a warning carrying an exception is something the shell tried and could not do, and carried on without —
/// <c>FAILED {what was tried}: {the exception}</c>; a fault (<see cref="LogLevel.Error"/> and above) is a pointer to the crash log,
/// which holds its trace, collapsed in a burst as the crash log collapses it (<see cref="FaultBurst"/>); anything else is its message.
/// </para>
/// <para>
/// The setting is read only once the shell config has loaded, so what startup logs before that is held in memory, then written out
/// or dropped by the first <see cref="Apply"/>. Every line also goes to the debugger's output.
/// </para>
/// </summary>
public sealed class SessionLogProvider : LogDestination
{
    /// <summary>Names the shell's files in the logs folder.</summary>
    public const string FilePrefix = "shell";

    /// <summary>Lines held before the setting is known. Startup writes a few dozen; this bounds a runaway.</summary>
    private const int MostHeld = 500;

    private readonly Func<string>         _folder;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string               _version;
    private readonly Func<DateTime, string> _traceFileOn;
    private readonly FaultBurst           _burst = new();
    private readonly Lock                 _gate  = new();

    private List<(DateTimeOffset At, string Text)>? _held = [];   // null once the setting is known
    private SessionLogFile?                         _file;        // set while logging is on

    /// <param name="folder">The logs folder, as it stands when logging turns on.</param>
    /// <param name="clock">The time each line is stamped with.</param>
    /// <param name="version">The build, named in the file's banner.</param>
    /// <param name="traceFileOn">The name of the file that holds the trace of a fault met on a given day — the crash log's.</param>
    public SessionLogProvider(Func<string> folder, Func<DateTimeOffset> clock, string version, Func<DateTime, string> traceFileOn)
    {
        _folder      = folder;
        _clock       = clock;
        _version     = version;
        _traceFileOn = traceFileOn;
    }

    /// <summary>The folder this session is logged in while logging is on; null while it is off. The AI host is handed it when it
    /// starts, so the host's log lands beside this one.</summary>
    public string? Folder
    {
        get { lock (_gate) return _file is null ? null : Path.GetDirectoryName(_file.FilePath); }
    }

    /// <summary>This session's file while logging is on; null while it is off.</summary>
    public string? CurrentPath
    {
        get { lock (_gate) return _file?.FilePath; }
    }

    /// <summary>
    /// Turns logging on or off. The first call settles what startup held: written out beneath the banner when on, dropped when
    /// off. A later call is the user changing the setting mid-session, and the file says so.
    /// </summary>
    public void Apply(bool enabled)
    {
        lock (_gate)
        {
            var held = _held;
            _held = null;

            if (enabled && _file is null)
            {
                _file = new SessionLogFile(_folder(), FilePrefix, _clock);
                _file.Write(Banner());
                if (held is null)
                    _file.Write("safety logging turned on mid-session");
                else
                    foreach (var (at, text) in held) _file.Write(at, text);
            }
            else if (!enabled && _file is not null)
            {
                _file.Write("safety logging turned off; nothing further is written this session");
                _file.Dispose();
                _file = null;
            }
        }
    }

    protected override bool Takes(string category, LogLevel level) => level >= LogLevel.Information;

    protected override void Write(in ShellLogEntry entry)
    {
        var now = _clock();
        string line;
        if (entry.Level >= LogLevel.Error)
        {
            lock (_gate)
            {
                // A burst the crash log collapses to a count is not repeated here either.
                _burst.Note(entry.Exception is { } ex ? FaultBurst.SignatureOf(ex) : entry.Message, now);
                if (_burst.Count > CrashLogProvider.FullTracesPerBurst) return;
            }
            line = entry.Exception is { } fault ? $"fault recorded in {_traceFileOn(now.Date)}: {Describe(fault)}" : entry.Message;
        }
        else
        {
            line = entry.Exception is { } failure ? $"FAILED {entry.Message}: {Describe(failure)}" : entry.Message;
        }
        Append(now, line);
    }

    /// <summary>The last line of a session is written by then: the file is closed, and nothing is written after it.</summary>
    public override void Dispose()
    {
        lock (_gate)
        {
            _held = null;
            _file?.Dispose();
            _file = null;
        }
    }

    /// <summary>An exception as one line — its type and message, and for a type load the loader's own reasons, which are the part
    /// that names the missing dependency.</summary>
    public static string Describe(Exception ex)
    {
        var line = $"{ex.GetType().FullName}: {ex.Message}";
        if (ex is ReflectionTypeLoadException load)
        {
            var reasons = load.LoaderExceptions.OfType<Exception>().Select(e => e.Message).Distinct().ToList();
            if (reasons.Count > 0) line += " — " + string.Join(" | ", reasons);
        }
        else if (ex.InnerException is { } inner)
        {
            line += $" — {inner.GetType().FullName}: {inner.Message}";
        }
        return line.ReplaceLineEndings(" ");
    }

    private void Append(DateTimeOffset at, string text)
    {
        Debug.WriteLine($"[Shell] {text}");
        lock (_gate)
        {
            if (_file is { } file) file.Write(at, text);
            else if (_held is { Count: < MostHeld } held) held.Add((at, text));
        }
    }

    private string Banner()
    {
        DateTime? started;
        try
        {
            using var process = Process.GetCurrentProcess();
            started = process.StartTime;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { started = null; }

        return $"=== Nexaflow {_version} | {RuntimeInformation.OSDescription} | {RuntimeInformation.ProcessArchitecture} | "
             + $"pid {Environment.ProcessId} | process started {started?.ToString("O") ?? "unknown"} ===";
    }
}
