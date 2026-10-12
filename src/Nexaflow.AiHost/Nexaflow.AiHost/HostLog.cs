namespace Nexaflow.AiHost;

/// <summary>
/// The host's own log, one file a day under the AI folder's <c>logs</c>, kept a week. The host has no window and the
/// app only sees its pipe, so this is where a load failure or a crash's last words end up.
/// </summary>
internal sealed class HostLog : IDisposable
{
    private static readonly TimeSpan Kept = TimeSpan.FromDays(7);

    private readonly object _gate = new();
    private readonly StreamWriter? _writer;

    public HostLog(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var old in Directory.EnumerateFiles(directory, "aihost-*.log"))
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(old) > Kept) File.Delete(old);

            var stream = new FileStream(Path.Combine(directory, $"aihost-{DateTime.Now:yyyyMMdd}.log"),
                                        FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _writer = new StreamWriter(stream) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _writer = null;   // a host that cannot log still serves
        }
    }

    public void Write(string line)
    {
        if (_writer is null) return;
        lock (_gate) _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{Environment.ProcessId}] {line}");
    }

    public void Dispose()
    {
        lock (_gate) _writer?.Dispose();
    }
}
