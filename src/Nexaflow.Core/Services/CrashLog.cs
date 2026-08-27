using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Nexaflow.Core.Services;

/// <summary>
/// The shell's crash log: one file per day, pruned to <see cref="RetentionDays"/> days, capped in size, and
/// stamped with the build that produced it.
/// <para>
/// It also decides whether a fault is still worth handling. The handler that feeds this marks exceptions
/// handled so the shell survives them - right for a one-off fault, catastrophic for one thrown from the WPF
/// render pass, where handling leaves the layout dirty and WPF simply re-measures and throws again. A
/// customer sent us a 170MB crash.log that was one such live-lock: a single exception, 17,724 times, in 85
/// seconds. So a repeated identical fault stops being written in full after
/// <see cref="FullTracesPerBurst"/> (a count is kept instead), and past <see cref="LiveLockCount"/> in one
/// window <see cref="Record"/> gives up and lets the process die rather than spin - a restart the user can
/// see beats a frozen shell they cannot.
/// </para>
/// </summary>
public sealed class CrashLog
{
    /// <summary>Crash logs older than this are deleted on the first write of a session.</summary>
    public const int RetentionDays = 10;

    /// <summary>Ceiling for one day's file. Past it, that day stops accepting entries.</summary>
    public const long MaxBytesPerDay = 8L * 1024 * 1024;

    /// <summary>Identical faults written in full before the burst collapses to a count.</summary>
    public const int FullTracesPerBurst = 5;

    /// <summary>Identical faults in one window that mean "this is a live-lock, not a fault".</summary>
    public const int LiveLockCount = 250;

    private static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(10);

    private readonly Func<string>         _dir;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string               _version;
    private readonly object               _gate = new();

    private string?        _day;
    private string?        _path;
    private bool           _capped;
    private string?        _signature;
    private DateTimeOffset _burstStart;
    private int            _burstCount;
    private int            _suppressed;

    public CrashLog(Func<string> directory, Func<DateTimeOffset> clock, string version)
    {
        _dir     = directory;
        _clock   = clock;
        _version = version;
    }

    /// <summary>The app-wide log, writing into whatever config root is active when the first fault lands.</summary>
    public static CrashLog Instance { get; } = new(
        () => ConfigManager.Instance.BaseDir,
        () => DateTimeOffset.Now,
        typeof(CrashLog).Assembly.GetName().Version?.ToString() ?? "unknown");

    /// <summary>Today's file. Named for the user, so it is the dated one even before anything has crashed.</summary>
    public string CurrentPath => Path.Combine(_dir(), FileNameFor(_clock().Date));

    /// <summary>
    /// Records a fault and says whether the shell should carry on. False means the same fault has recurred so
    /// often in so short a window that handling it is what keeps it coming - let it terminate the process.
    /// </summary>
    public bool Record(Exception ex)
    {
        lock (_gate)
        {
            var now = _clock();
            var sig = SignatureOf(ex);

            if (sig != _signature || now - _burstStart > BurstWindow)
            {
                FlushSuppressed(now);
                _signature  = sig;
                _burstStart = now;
                _burstCount = 0;
            }

            _burstCount++;

            if (_burstCount <= FullTracesPerBurst)
                Append(now, $"[{now:O}] {ex}{Environment.NewLine}{Environment.NewLine}");
            else
                _suppressed++;

            if (_burstCount < LiveLockCount) return true;

            FlushSuppressed(now);
            Append(now, $"[{now:O}] --- the fault above repeated {_burstCount} times in "
                      + $"{(now - _burstStart).TotalSeconds:F1}s. Handling it is what keeps it recurring (a "
                      + "render-pass fault re-throws on every frame), so the process is being allowed to "
                      + $"terminate instead of live-locking. ---{Environment.NewLine}{Environment.NewLine}");
            _signature = null;   // a restart starts fresh
            return false;
        }
    }

    /// <summary>Writes out any collapsed burst. Call on exit so a tail-end burst is not lost.</summary>
    public void Flush()
    {
        lock (_gate) FlushSuppressed(_clock());
    }

    private void FlushSuppressed(DateTimeOffset now)
    {
        if (_suppressed <= 0) return;
        var n = _suppressed;
        _suppressed = 0;
        Append(now, $"[{now:O}] --- {n} further identical occurrence(s) suppressed ---"
                  + $"{Environment.NewLine}{Environment.NewLine}");
    }

    /// <summary>Type, message and top frame - enough to call two faults "the same" without holding the trace.</summary>
    private static string SignatureOf(Exception ex)
    {
        var trace   = ex.StackTrace ?? "";
        var newline = trace.IndexOf('\n');
        var top     = (newline < 0 ? trace : trace[..newline]).Trim();
        return $"{ex.GetType().FullName}|{ex.Message}|{top}";
    }

    private static string FileNameFor(DateTime day) =>
        $"crash-{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log";

    private void Append(DateTimeOffset now, string text)
    {
        try
        {
            var day = now.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (day != _day)
            {
                var dir = _dir();
                Directory.CreateDirectory(dir);
                _day    = day;
                _path   = Path.Combine(dir, FileNameFor(now.Date));
                _capped = false;
                Prune(dir, now);
                File.AppendAllText(_path, Banner(now));
            }

            if (_capped || _path is null) return;

            if (new FileInfo(_path).Length >= MaxBytesPerDay)
            {
                _capped = true;
                File.AppendAllText(_path,
                    $"[{now:O}] --- {MaxBytesPerDay / (1024 * 1024)}MB cap reached; further entries for "
                    + $"{_day} are dropped ---{Environment.NewLine}{Environment.NewLine}");
                return;
            }

            File.AppendAllText(_path, text);
        }
        catch { }   // a crash log that throws while logging a crash helps nobody
    }

    /// <summary>Without this the version is guesswork - the 170MB log a customer sent carried none.</summary>
    private string Banner(DateTimeOffset now) =>
        $"=== Nexaflow {_version} | {RuntimeInformation.OSDescription} | "
        + $"{RuntimeInformation.ProcessArchitecture} | pid {Environment.ProcessId} | session started "
        + $"{now:O} ==={Environment.NewLine}{Environment.NewLine}";

    /// <summary>
    /// Drops crash logs past the retention window - the dated ones by their date, and any pre-rotation
    /// <c>crash.log</c> by its last write, since that is the file the policy exists because of.
    /// </summary>
    private static void Prune(string dir, DateTimeOffset now)
    {
        var cutoff = now.Date.AddDays(-RetentionDays);
        foreach (var file in Directory.EnumerateFiles(dir, "crash*.log"))
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(file);
                DateTime stamp;

                if (name.StartsWith("crash-", StringComparison.Ordinal))
                {
                    if (!DateTime.TryParseExact(name[6..], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                                DateTimeStyles.None, out stamp))
                        continue;
                }
                else if (name.Equals("crash", StringComparison.OrdinalIgnoreCase))
                {
                    stamp = File.GetLastWriteTime(file).Date;
                }
                else continue;

                if (stamp < cutoff) File.Delete(file);
            }
            catch { }   // locked or vanished - the next session prunes it
        }
    }
}
