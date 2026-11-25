using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Nexaflow.Core.Services.Logging;

/// <summary>
/// The crash log: every fault the shell meets — each entry at <see cref="LogLevel.Error"/> and above — always kept, in one file per
/// day in the configuration folder (<c>crash-yyyy-MM-dd.log</c>), pruned to <see cref="RetentionDays"/> days, capped at
/// <see cref="MaxBytesPerDay"/> a day, and stamped with the build that wrote it. The file is made only when a fault is written, so a
/// run that met none leaves none — which is what a UI journey's teardown checks.
/// <para>
/// A repeated identical fault stops being written in full after <see cref="FullTracesPerBurst"/> (<see cref="FaultBurst"/>): the
/// rest of the burst is a count, written when the burst ends, on <see cref="Flush"/>, or when the log is disposed. Whether a fault
/// is still worth handling is not this log's to say — it records; <see cref="DispatcherFaultPolicy"/> decides.
/// </para>
/// </summary>
public sealed class CrashLogProvider : LogDestination
{
    /// <summary>Crash logs older than this are deleted on the first write of a session.</summary>
    public const int RetentionDays = 10;

    /// <summary>Ceiling for one day's file. Past it, that day stops accepting entries.</summary>
    public const long MaxBytesPerDay = 8L * 1024 * 1024;

    /// <summary>Identical faults written in full before the burst collapses to a count.</summary>
    public const int FullTracesPerBurst = 5;

    private readonly Func<string>         _dir;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string               _version;
    private readonly FaultBurst           _burst = new();
    private readonly Lock                 _gate  = new();

    private string? _day;
    private string? _path;
    private bool    _capped;
    private int     _suppressed;

    /// <param name="directory">The configuration folder, where the files are kept.</param>
    /// <param name="clock">The time each entry is stamped with, and the day it is filed under.</param>
    /// <param name="version">The build, named in each file's banner.</param>
    public CrashLogProvider(Func<string> directory, Func<DateTimeOffset> clock, string version)
    {
        _dir     = directory;
        _clock   = clock;
        _version = version;
    }

    /// <summary>Today's file. Named for the user, so it is the dated one even before anything has crashed.</summary>
    public string CurrentPath => Path.Combine(_dir(), FileNameFor(_clock().Date));

    /// <summary>The name of the file a fault met on <paramref name="day"/> is written to.</summary>
    public static string FileNameFor(DateTime day) =>
        $"crash-{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log";

    protected override bool Takes(string category, LogLevel level) => level >= LogLevel.Error;

    /// <summary>
    /// A fault, as its trace beneath the line saying what met it; an entry with no exception, as its message alone. Identical
    /// entries in a burst past <see cref="FullTracesPerBurst"/> are counted, not written.
    /// </summary>
    protected override void Write(in ShellLogEntry entry)
    {
        var signature = entry.Exception is { } ex ? FaultBurst.SignatureOf(ex) : entry.Message;
        lock (_gate)
        {
            var now = _clock();
            if (_burst.Note(signature, now)) FlushSuppressed(now);

            if (_burst.Count > FullTracesPerBurst)
            {
                _suppressed++;
                return;
            }

            Append(now, entry.Exception is { } fault
                ? $"[{now:O}] {entry.Message}{Environment.NewLine}{fault}{Environment.NewLine}{Environment.NewLine}"
                : $"[{now:O}] --- {entry.Message} ---{Environment.NewLine}{Environment.NewLine}");
        }
    }

    /// <summary>Writes out any collapsed burst. Called when the process is going down, so a tail-end burst is not lost.</summary>
    public void Flush()
    {
        lock (_gate) FlushSuppressed(_clock());
    }

    public override void Dispose() => Flush();

    private void FlushSuppressed(DateTimeOffset now)
    {
        if (_suppressed <= 0) return;
        var n = _suppressed;
        _suppressed = 0;
        Append(now, $"[{now:O}] --- {n} further identical occurrence(s) suppressed ---"
                  + $"{Environment.NewLine}{Environment.NewLine}");
    }

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
