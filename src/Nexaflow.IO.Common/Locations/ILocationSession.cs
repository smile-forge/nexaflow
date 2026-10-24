using Nexaflow.IO.Common.Terminal;

namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// One live connection to a host. Every member is async and cancellable, because every one waits on the network.
/// <para>
/// Paths here are the <b>provider's</b>: absolute, <c>/</c>-separated, holding names exactly as the server does
/// (<c>/home/adam/notes:v2.md</c>), with <c>/</c> the location's root. The VFS turns its <c>::{id}\…</c> form into
/// this and back (<see cref="LocationPath"/>), so a provider never sees an escaped name. Failures are
/// <see cref="LocationException"/>s.
/// </para>
/// </summary>
public interface ILocationSession : IAsyncDisposable
{
    /// <summary>The children of a folder, streamed as the server sends them, so a 50,000-entry folder paints its
    /// first rows at once. <see cref="LocationError.NotFound"/> when the folder is missing.</summary>
    IAsyncEnumerable<LocationEntry> EnumerateAsync(string path, CancellationToken ct);

    /// <summary>The entry at the path, or null when there is none. The root's name is empty.</summary>
    ValueTask<LocationEntry?> GetEntryAsync(string path, CancellationToken ct);

    /// <summary>A stream of the file's bytes. For <see cref="ReadIntent.Random"/> it is seekable — fetching blocks
    /// on demand where the protocol has ranged reads (<see cref="LocationCapabilities.RandomRead"/>), spooling the
    /// file first where it has not.</summary>
    ValueTask<Stream> OpenReadAsync(string path, ReadIntent intent, CancellationToken ct);

    /// <summary>
    /// Starts a write. Nothing is visible at the path until <see cref="ILocationWrite.CommitAsync"/>: where the
    /// location can rename atomically the bytes go to a sibling temp name that the commit renames over the target, so
    /// a dropped connection never leaves half a file; where it cannot, the commit uploads the whole object.
    /// </summary>
    /// <param name="expected">The entry as the writer last read it, or null. When given, the commit re-reads the
    /// target and fails with <see cref="LocationError.Conflict"/> if it is no longer that version
    /// (<see cref="LocationEntry.IsSameVersion"/>).</param>
    ValueTask<ILocationWrite> BeginWriteAsync(string path, WriteMode mode, LocationEntry? expected, CancellationToken ct);

    /// <summary>Renames or moves within this session. <see cref="LocationError.Conflict"/> when the target exists and
    /// <paramref name="replace"/> is false — and always for a folder, which never replaces anything.
    /// <see cref="LocationError.NotFound"/> when the source is missing.</summary>
    ValueTask MoveAsync(string from, string to, bool replace, CancellationToken ct);

    /// <summary>Deletes a file, or a folder — only an empty one unless <paramref name="recursive"/>. Permanent: a
    /// location has no recycle bin. <see cref="LocationError.NotFound"/> when nothing is there;
    /// <see cref="LocationError.Conflict"/> when a folder still holds something and <paramref name="recursive"/> is false.</summary>
    ValueTask DeleteAsync(string path, bool recursive, CancellationToken ct);

    /// <summary>Creates the folder and any missing parents; nothing happens when it already exists.</summary>
    ValueTask CreateDirectoryAsync(string path, CancellationToken ct);

    /// <summary>Reports changes under the path, or null when the location lacks
    /// <see cref="LocationCapabilities.Watch"/> — the caller then polls.</summary>
    ILocationWatch? Watch(string path, bool recursive);

    /// <summary>A shell on the same host, or null when the location has none (<see cref="LocationCapabilities.Shell"/>).</summary>
    ILocationShell? Shell { get; }
}

/// <summary>A write in progress. Disposing it without <see cref="CommitAsync"/> discards everything written.</summary>
public interface ILocationWrite : IAsyncDisposable
{
    /// <summary>Where the new bytes go. Write-only; owned by the write — never dispose it yourself.</summary>
    Stream Content { get; }

    /// <summary>Makes the bytes the file's content. Fails with <see cref="LocationError.Conflict"/> when the target
    /// was given as expected and has since changed, or when <see cref="WriteMode.CreateNew"/> finds it exists; the
    /// target is then untouched. Disposing after a commit does nothing more.</summary>
    ValueTask CommitAsync(CancellationToken ct);
}

/// <summary>Changes under a watched path, for as long as the watch is held.</summary>
public interface ILocationWatch : IDisposable
{
    /// <summary>Raised with the path that changed — in the same form the watch was asked with — or the watched path
    /// itself when the location cannot say which. May fire on any thread, and more than once for one change.</summary>
    event Action<string>? Changed;
}

/// <summary>Opens a shell on a location's host.</summary>
public interface ILocationShell
{
    /// <summary>Starts a shell in <paramref name="workingDirectory"/> (a provider path) at the given size.</summary>
    ValueTask<ITerminalTransport> OpenAsync(string workingDirectory, int columns, int rows, CancellationToken ct);
}

/// <summary>
/// The live session behind one connected location. The session pool implements it per connection, sharing one
/// session among every browser, viewer, transfer and terminal on that host and reconnecting when the link drops.
/// </summary>
public interface ILocationSessionSource
{
    /// <summary>A connected session, connecting first if need be. Owned by the source: callers never dispose it.</summary>
    ValueTask<ILocationSession> GetSessionAsync(CancellationToken ct);
}
