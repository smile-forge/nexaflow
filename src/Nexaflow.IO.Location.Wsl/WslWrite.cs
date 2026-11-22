using Nexaflow.IO.Common.Locations;

namespace Nexaflow.IO.Location.Wsl;

/// <summary>
/// A write to a distro file. Created and replaced content goes to a hidden sibling over <c>\\wsl.localhost</c>, and the
/// helper renames it over the target at commit — one step, keeping the replaced file's permission bits — so a dropped
/// link never leaves half a file. An append writes in place, because an append has nothing to swap. A write given the
/// entry it read checks the target is still that version, read the same way it was, before anything lands.
/// </summary>
internal sealed class WslWrite : ILocationWrite
{
    private const int BufferSize = 81920;

    private readonly WslSession _session;
    private readonly string _path;
    private readonly WriteMode _mode;
    private readonly LocationEntry? _expected;
    private readonly string? _temp;
    private readonly FileStream _content;
    private bool _finished;

    private WslWrite(WslSession session, string path, WriteMode mode, LocationEntry? expected, string? temp, FileStream content)
    {
        _session  = session;
        _path     = path;
        _mode     = mode;
        _expected = expected;
        _temp     = temp;
        _content  = content;
    }

    public static async ValueTask<WslWrite> OpenAsync(WslSession session, string path, WriteMode mode, LocationEntry? expected,
                                                      CancellationToken ct)
    {
        if (mode == WriteMode.CreateNew && await session.GetEntryAsync(path, ct).ConfigureAwait(false) is not null)
            throw LocationFailures.Conflict(path, $"'{path}' already exists.");
        if (WslSession.ParentOf(path) is { } parent)
            await session.CreateDirectoryAsync(parent, ct).ConfigureAwait(false);

        if (mode == WriteMode.Append)
        {
            // An append cannot be taken back, so the version is checked before the first byte rather than at commit.
            await RequireVersionAsync(session, path, expected, ct).ConfigureAwait(false);
            var appending = await OpenFileAsync(session, path, FileMode.Append, ct).ConfigureAwait(false);
            return new WslWrite(session, path, mode, expected, null, appending);
        }

        var temp    = $"{WslSession.ParentOf(path) ?? ""}/.{WslSession.NameOf(path)}.{Guid.NewGuid():N}.nexatmp".Replace("//", "/");
        var content = await OpenFileAsync(session, temp, FileMode.CreateNew, ct).ConfigureAwait(false);
        return new WslWrite(session, path, mode, expected, temp, content);
    }

    private static async ValueTask<FileStream> OpenFileAsync(WslSession session, string path, FileMode mode, CancellationToken ct)
    {
        var native = session.Native(path);
        try
        {
            return await Task.Run(() => new FileStream(native, mode, FileAccess.Write, FileShare.Read, BufferSize,
                                                       FileOptions.Asynchronous), ct).ConfigureAwait(false);
        }
        catch (Exception failure) when (LocationFailures.Translate(failure, path) is { } translated)
        {
            throw translated;
        }
    }

    private static async ValueTask RequireVersionAsync(WslSession session, string path, LocationEntry? expected, CancellationToken ct)
    {
        if (expected is null) return;
        var current = await session.GetEntryAsync(path, ct).ConfigureAwait(false);
        if (current is null || !current.IsSameVersion(expected))
            throw LocationFailures.Conflict(path, $"'{path}' changed since it was read.");
    }

    public Stream Content => _content;

    public async ValueTask CommitAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        await _content.FlushAsync(ct).ConfigureAwait(false);
        await _content.DisposeAsync().ConfigureAwait(false);
        if (_temp is not null)
        {
            await RequireVersionAsync(_session, _path, _expected, ct).ConfigureAwait(false);
            await _session.RenameAsync(_temp, _path, replace: _mode != WriteMode.CreateNew, ct).ConfigureAwait(false);
        }
        _finished = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_finished) return;
        _finished = true;
        await _content.DisposeAsync().ConfigureAwait(false);
        if (_temp is null) return;
        try { await _session.DeleteAsync(_temp, recursive: false, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception left) when (left is LocationException or IOException)
        {
            // A temp left behind is a hidden file beside the target; the target is untouched either way.
        }
    }
}
