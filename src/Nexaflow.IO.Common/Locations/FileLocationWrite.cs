namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// A write to a real file. Created and replaced content goes to a sibling temp file — the same volume, so the rename
/// that commits it is atomic — and nothing is visible at the target until then; an append writes in place, because
/// an append has nothing to swap.
/// </summary>
internal sealed class FileLocationWrite : ILocationWrite
{
    private const int BufferSize = 81920;

    private readonly string _target;
    private readonly string _path;
    private readonly WriteMode _mode;
    private readonly LocationEntry? _expected;
    private readonly string? _temp;
    private readonly FileStream _content;
    private bool _finished;

    private FileLocationWrite(string target, string path, WriteMode mode, LocationEntry? expected, string? temp, FileStream content)
    {
        _target   = target;
        _path     = path;
        _mode     = mode;
        _expected = expected;
        _temp     = temp;
        _content  = content;
    }

    /// <param name="target">The real file written.</param>
    /// <param name="path">The path the caller named, for failures — a mounted path stays mounted in a message.</param>
    public static FileLocationWrite Open(string target, string path, WriteMode mode, LocationEntry? expected)
    {
        if (mode == WriteMode.CreateNew && (File.Exists(target) || Directory.Exists(target)))
            throw LocationFailures.Conflict(path, $"'{path}' already exists.");

        var dir = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (mode == WriteMode.Append)
        {
            // An append cannot be taken back, so the version is checked before the first byte rather than at commit.
            RequireVersion(target, path, expected);
            var appending = new FileStream(target, FileMode.Append, FileAccess.Write, FileShare.Read, BufferSize, FileOptions.Asynchronous);
            return new FileLocationWrite(target, path, mode, expected, null, appending);
        }

        var temp = $"{target}.{System.Guid.NewGuid():N}.nexatmp";
        var content = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
        return new FileLocationWrite(target, path, mode, expected, temp, content);
    }

    public Stream Content => _content;

    public async ValueTask CommitAsync(CancellationToken ct)
    {
        System.ObjectDisposedException.ThrowIf(_finished, this);
        await _content.FlushAsync(ct).ConfigureAwait(false);
        await _content.DisposeAsync().ConfigureAwait(false);
        if (_temp is not null)
        {
            try { await Task.Run(Swap, ct).ConfigureAwait(false); }
            catch (System.Exception e) when (LocationFailures.Translate(e, _path) is { } failure) { throw failure; }
        }
        _finished = true;
    }

    private void Swap()
    {
        RequireVersion(_target, _path, _expected);
        if (_mode == WriteMode.CreateNew) File.Move(_temp!, _target, overwrite: false);
        else if (File.Exists(_target))   File.Replace(_temp!, _target, destinationBackupFileName: null);
        else                             File.Move(_temp!, _target);
    }

    private static void RequireVersion(string target, string path, LocationEntry? expected)
    {
        if (expected is null) return;
        var current = File.Exists(target) ? LocationEntry.FromFileSystemInfo(new FileInfo(target)) : null;
        if (current is null || !current.IsSameVersion(expected))
            throw LocationFailures.Conflict(path, $"'{path}' changed since it was read.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_finished) return;
        _finished = true;
        await _content.DisposeAsync().ConfigureAwait(false);
        if (_temp is null) return;
        try { File.Delete(_temp); }
        catch (IOException) { /* a temp left behind is swept with the folder; the target is untouched either way */ }
        catch (System.UnauthorizedAccessException) { }
    }
}

/// <summary>
/// A write gathered in memory and handed whole to <c>commit</c> — for a target with no file of its own to stream
/// into, such as an entry inside an archive, which is rewritten in one piece.
/// </summary>
internal sealed class BufferedLocationWrite(System.Func<byte[], CancellationToken, ValueTask> commit) : ILocationWrite
{
    private readonly MemoryStream _content = new();
    private bool _finished;

    public Stream Content => _content;

    public async ValueTask CommitAsync(CancellationToken ct)
    {
        System.ObjectDisposedException.ThrowIf(_finished, this);
        await commit(_content.ToArray(), ct).ConfigureAwait(false);
        _finished = true;
    }

    public ValueTask DisposeAsync()
    {
        _finished = true;
        _content.Dispose();
        return ValueTask.CompletedTask;
    }
}
