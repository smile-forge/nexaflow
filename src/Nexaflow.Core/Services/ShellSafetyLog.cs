using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Nexaflow.IO.Common.Logging;

namespace Nexaflow.Core.Services;

/// <summary>
/// The shell's safety log, kept while Options → Shell → Enable safety logging is on: one file per session in the
/// <c>logs</c> folder under <see cref="ConfigManager.LocalDir"/> (<c>%LOCALAPPDATA%\Smile\nexaflow\logs</c>), from the
/// first step of startup to the last of shutdown, with every load or activation failure the shell meets between — a
/// feature assembly that will not load or activate, the discovery cache, a provider assembly that is configured but not
/// installed, a workspace folder it could not tidy — and a one-line account of each fault the crash log takes. A session
/// with no <c>shutdown complete</c> line ended in a crash or was killed. Files are kept and capped by
/// <see cref="SessionLogFile"/>; the AI host's log goes into the same folder while this is on (<see cref="Folder"/>).
/// <para>
/// The shell's own: no contract exposes it, so features cannot write here. The setting is read only once the shell
/// config has loaded, so what startup records before that is held in memory, then written out or dropped by the first
/// <see cref="Apply"/>. Every line also goes to the debugger's output.
/// </para>
/// </summary>
internal sealed class ShellSafetyLog
{
    /// <summary>Names the shell's files in the logs folder.</summary>
    public const string FilePrefix = "shell";

    /// <summary>Lines held before the setting is known. Startup writes a few dozen; this bounds a runaway.</summary>
    private const int MostHeld = 500;

    private readonly Func<string>         _folder;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string               _version;
    private readonly Lock                 _gate = new();

    private List<(DateTimeOffset At, string Text)>? _held = [];   // null once the setting is known
    private SessionLogFile?                         _file;        // set while logging is on

    public ShellSafetyLog(Func<string> folder, Func<DateTimeOffset> clock, string version)
    {
        _folder  = folder;
        _clock   = clock;
        _version = version;
    }

    /// <summary>The app-wide log, writing into the config root's local folder as it stands when logging turns on.</summary>
    public static ShellSafetyLog Instance { get; } = new(
        () => Path.Combine(ConfigManager.Instance.LocalDir, "logs"),
        () => DateTimeOffset.Now,
        typeof(ShellSafetyLog).Assembly.GetName().Version?.ToString() ?? "unknown");

    /// <summary>The folder this session is logged in while logging is on; null while it is off. The AI host is handed it
    /// when it starts, so the host's log lands beside this one.</summary>
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
    /// Turns logging on or off. The first call settles what startup held: written out beneath the banner when on,
    /// dropped when off. A later call is the user changing the setting mid-session, and the file says so.
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

    /// <summary>A step of startup or shutdown, or something the shell found on the way.</summary>
    public void Step(string text) => Append(text);

    /// <summary>Something the shell tried and could not do, and carried on without.</summary>
    public void Failed(string what, Exception ex) => Append($"FAILED {what}: {Describe(ex)}");

    /// <summary>The last line of a session: written, then the file is closed. Nothing is written after it.</summary>
    public void Close()
    {
        Append("shutdown complete");
        lock (_gate)
        {
            _held = null;
            _file?.Dispose();
            _file = null;
        }
    }

    /// <summary>An exception as one line — its type and message, and for a type load the loader's own reasons, which
    /// are the part that names the missing dependency.</summary>
    internal static string Describe(Exception ex)
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

    private void Append(string text)
    {
        Debug.WriteLine($"[Shell] {text}");
        lock (_gate)
        {
            if (_file is { } file) file.Write(text);
            else if (_held is { Count: < MostHeld } held) held.Add((_clock(), text));
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
