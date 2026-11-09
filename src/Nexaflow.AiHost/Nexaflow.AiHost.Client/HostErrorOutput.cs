using Nexaflow.IO.Common.Logging;

namespace Nexaflow.AiHost.Client;

/// <summary>
/// What a host writes to its error output — where a native runtime's last words go when it fails too hard to log them,
/// and where .NET writes an exception nothing caught. The last <see cref="KeptLines"/> are held for the record of the
/// host's end; while the app keeps logs, every line is also written as it arrives to a file of its own in the logs
/// folder, beside the host's own log and pruned with it, so nothing depends on the app living to record the end. The
/// file is opened with the first line: a host that writes none leaves none.
/// </summary>
internal sealed class HostErrorOutput : IDisposable
{
    /// <summary>Names the files: <c>aihost-stderr-…</c>, beside the host's <c>aihost-…</c> log.</summary>
    public const string FilePrefix = "aihost-stderr";

    /// <summary>Lines held for the record of the host's end.</summary>
    public const int KeptLines = 20;

    private readonly Lock _gate = new();
    private readonly Queue<string> _last = new();
    private readonly string? _directory;
    private readonly int _processId;
    private SessionLogFile? _file;
    private bool _closed;

    /// <param name="directory">The logs folder; null, and only the last lines are kept, in memory.</param>
    /// <param name="processId">The host's process, which the file names in its first line.</param>
    public HostErrorOutput(string? directory, int processId)
    {
        _directory = directory;
        _processId = processId;
    }

    /// <summary>The last lines written, oldest first.</summary>
    public IReadOnlyList<string> Last
    {
        get { lock (_gate) return [.. _last]; }
    }

    /// <summary>A line the host wrote.</summary>
    public void Take(string line)
    {
        lock (_gate)
        {
            _last.Enqueue(line);
            if (_last.Count > KeptLines) _last.Dequeue();
            if (_directory is null || _closed) return;
            if (_file is null)
            {
                _file = new SessionLogFile(_directory, FilePrefix);
                _file.Write($"error output of AI host process {_processId}");
            }
            _file.Write(line);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _closed = true;
            _file?.Dispose();
            _file = null;
        }
    }
}
