using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Nexaflow.Services.Initiatives.Cli.Daemon;

/// <summary>
/// A line per thing the daemon does, appended to a file whose path is fixed and printable.
/// <para>
/// The daemon has no console — that is the point of it — so without this there is no way to see what it is
/// doing except through a client, and a client that is itself stuck is exactly when the question gets asked.
/// A file survives that: the work is still recorded while the caller waits, and it is still there afterwards
/// when the caller has gone. Tailing it answers "is it working or wedged, and on what" without a debugger
/// and without another round of guessing.
/// </para>
/// <para>
/// Best-effort throughout. A daemon that failed because it could not write its own log would be a worse
/// daemon than one that runs unlogged, so every failure here is swallowed.
/// </para>
/// </summary>
internal static class DaemonLog
{
    private static readonly object Gate = new();
    private static StreamWriter? _to;

    /// <summary>Beyond this the log is started again: it is a running account, not an archive.</summary>
    private const long Cap = 4 * 1024 * 1024;

    /// <summary>Where this daemon's log lives. Beside the staged copies rather than in one, so that pruning a
    /// stale build does not take its explanation with it.</summary>
    internal static string PathFor(string pipe) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "Smile", "nfi", "logs", pipe + ".log");

    internal static void Open(string pipe)
    {
        try
        {
            var path = PathFor(pipe);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (File.Exists(path) && new FileInfo(path).Length > Cap) File.Delete(path);

            lock (Gate)
                _to = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                                       new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>One line: when, which ticket, what happened, and any detail worth having.</summary>
    internal static void Say(string ticket, string what, string detail = "")
    {
        lock (Gate)
        {
            if (_to is null) return;
            try
            {
                _to.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{DateTime.UtcNow:HH:mm:ss.fff}  {ticket,-12}  {what,-8}  {detail}"));
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException) { _to = null; }
        }
    }

    internal static void Close()
    {
        lock (Gate)
        {
            try { _to?.Dispose(); } catch (IOException) { }
            _to = null;
        }
    }
}
