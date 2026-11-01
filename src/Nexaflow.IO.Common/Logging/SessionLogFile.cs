using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Nexaflow.IO.Common.Logging;

/// <summary>
/// One process's log: a file of its own in a folder of them, named for the moment and the process that opened it, held
/// open and flushed line by line so a process that dies leaves everything written up to it. Opening one deletes the
/// folder's files of the same prefix last written more than <see cref="RetentionDays"/> ago, and past
/// <see cref="MaxBytes"/> a file takes nothing more but the line that says so. It never throws: a log that cannot be
/// written is one the process runs on without.
/// </summary>
public sealed class SessionLogFile : IDisposable
{
    /// <summary>Files of the same prefix older than this are deleted when a new one opens.</summary>
    public const int RetentionDays = 10;

    /// <summary>Ceiling for one file. Past it, the file says so once and takes nothing further.</summary>
    public const long MaxBytes = 8L * 1024 * 1024;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly Lock                 _gate = new();
    private readonly Func<DateTimeOffset> _clock;
    private StreamWriter?                 _writer;
    private long                          _written;
    private bool                          _capped;

    /// <param name="prefix">Names the files — <c>{prefix}-yyyyMMdd-HHmmss-{pid}.log</c> — and is all pruning matches,
    /// so logs of different prefixes can share a folder.</param>
    public SessionLogFile(string directory, string prefix, Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.Now);
        var now = _clock();
        FilePath = Path.Combine(directory, $"{prefix}-{now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
        try
        {
            Directory.CreateDirectory(directory);
            Prune(directory, prefix, now);
            var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _written = stream.Length;
            _writer  = new StreamWriter(stream, Utf8) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _writer = null;
        }
    }

    /// <summary>The file this session writes, whether or not it could be opened.</summary>
    public string FilePath { get; }

    /// <summary>False when the file could not be opened, or stopped taking lines.</summary>
    public bool IsOpen
    {
        get { lock (_gate) return _writer is not null; }
    }

    /// <summary>A line stamped now.</summary>
    public void Write(string text) => Write(_clock(), text);

    /// <summary>A line stamped with when it happened — for a line held before the file could be opened.</summary>
    public void Write(DateTimeOffset at, string text)
    {
        lock (_gate)
        {
            if (_writer is null || _capped) return;
            var line  = $"{at:HH:mm:ss.fff}  {text}{Environment.NewLine}";
            var bytes = Utf8.GetByteCount(line);
            try
            {
                if (_written + bytes > MaxBytes)
                {
                    _capped = true;
                    _writer.Write($"{at:HH:mm:ss.fff}  --- {MaxBytes / (1024 * 1024)}MB cap reached; nothing further is written ---{Environment.NewLine}");
                    return;
                }
                _writer.Write(line);
                _written += bytes;
            }
            catch (IOException)
            {
                // A full disk or a lost volume: stop here rather than fail every later line the same way.
                Close();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate) Close();
    }

    private void Close()
    {
        try { _writer?.Dispose(); }
        catch (IOException) { /* the last flush failed; nothing is left to save */ }
        _writer = null;
    }

    private static void Prune(string directory, string prefix, DateTimeOffset now)
    {
        var cutoff = now.UtcDateTime.AddDays(-RetentionDays);
        foreach (var file in Directory.EnumerateFiles(directory, $"{prefix}-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Held open or already gone — the next session prunes it.
            }
        }
    }
}
