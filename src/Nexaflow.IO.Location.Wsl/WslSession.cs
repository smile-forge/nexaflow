using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Wsl.Protocol;

namespace Nexaflow.IO.Location.Wsl;

/// <summary>
/// One distro, connected: its helper answers listings, metadata, renames, copies, deletes and watches with the distro's
/// own facts — POSIX mode, owner and group, links — and bytes are read and written over <c>\\wsl.localhost</c>, which
/// seeks, so a large file opens at its first page. Paths are the distro's own (<c>/home/adam</c>).
/// </summary>
internal sealed class WslSession : ILocationSession
{
    private const int BufferSize = 81920;

    private readonly WslHelperLink _link;

    private WslSession(string distro, WslHelperLink link, ILocationShell shell)
    {
        Distro = distro;
        _link  = link;
        Shell  = shell;
    }

    public string Distro { get; }

    public ILocationShell? Shell { get; }

    /// <summary>The helper, for a test that drops it.</summary>
    internal WslHelperLink Link => _link;

    /// <summary>Starts the distro's helper and greets it.</summary>
    public static async ValueTask<WslSession> StartAsync(string distro, CancellationToken ct)
    {
        var start = await WslHelperDeployment.StartAsync(distro, ct).ConfigureAwait(false);
        try
        {
            var hello  = await start.Link.CallAsync(WslOp.Hello, w => w.Int32(WslWire.ProtocolVersion), "/", ct).ConfigureAwait(false);
            var reader = hello.Read();
            int version = reader.Int32();
            if (version != WslWire.ProtocolVersion)
                throw new LocationException(LocationError.Disconnected, "/",
                                            $"The WSL helper in '{distro}' speaks protocol {version}, not {WslWire.ProtocolVersion}.");
            reader.UInt32();
            reader.Text();
            var loginShell = reader.Text();
            return new WslSession(distro, start.Link, new WslShell(distro, start.Folder, loginShell));
        }
        catch
        {
            await start.Link.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Where a path of this distro is on this disk, named exactly.</summary>
    internal string Native(string path) => WslNames.ExactNativePath(Distro, path);

    /// <summary>A distro path's parent, or null for the root.</summary>
    internal static string? ParentOf(string path)
    {
        var trimmed = path.TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        return slash < 0 || trimmed.Length == 0 ? null : slash == 0 ? "/" : trimmed[..slash];
    }

    /// <summary>The last name in a distro path; empty for the root.</summary>
    internal static string NameOf(string path)
    {
        var trimmed = path.TrimEnd('/');
        return trimmed[(trimmed.LastIndexOf('/') + 1)..];
    }

    public async IAsyncEnumerable<LocationEntry> EnumerateAsync(string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var page in _link.StreamAsync(WslOp.List, w => w.String(path), path, ct).ConfigureAwait(false))
        {
            if (page.Code != (byte)WslReply.Entries) continue;
            var reader = page.Read();
            int count = reader.Int32();
            for (int i = 0; i < count; i++) yield return ToEntry(reader.Entry());
        }
    }

    public async ValueTask<LocationEntry?> GetEntryAsync(string path, CancellationToken ct)
    {
        try
        {
            var frame = await _link.CallAsync(WslOp.Stat, w => w.String(path), path, ct).ConfigureAwait(false);
            var entry = ToEntry(frame.Read().Entry());
            // The root is a folder with no name, as every location's is.
            return NameOf(path).Length == 0 ? entry with { Name = string.Empty } : entry;
        }
        catch (LocationException missing) when (missing.Error == LocationError.NotFound)
        {
            return null;
        }
    }

    /// <summary>An entry as a location lists it: a link to a folder opens as a folder, a name starting with a dot is
    /// hidden, and a file no one may write is read-only.</summary>
    internal static LocationEntry ToEntry(WireEntry entry)
    {
        bool isDirectory = entry.Kind == WireKind.Directory || (entry.Kind == WireKind.Symlink && entry.LinkToDirectory);
        return new LocationEntry(entry.Name, isDirectory, isDirectory ? 0 : entry.Size,
                                 new DateTime(entry.ModifiedTicks, DateTimeKind.Utc).ToLocalTime())
        {
            IsHidden      = entry.Name.StartsWith('.'),
            IsReadOnly    = (entry.Mode & 0b010_010_010) == 0,
            Created       = entry.CreatedTicks > 0 ? new DateTime(entry.CreatedTicks, DateTimeKind.Utc).ToLocalTime() : null,
            SymlinkTarget = entry.Kind == WireKind.Symlink ? entry.LinkTarget ?? string.Empty : null,
            Mode          = (UnixFileMode)entry.Mode,
            Owner         = entry.Owner ?? (entry.HasOwner ? entry.Uid.ToString(System.Globalization.CultureInfo.InvariantCulture) : null),
            Group         = entry.Group ?? (entry.HasOwner ? entry.Gid.ToString(System.Globalization.CultureInfo.InvariantCulture) : null),
        };
    }

    public async ValueTask<Stream> OpenReadAsync(string path, ReadIntent intent, CancellationToken ct)
    {
        var native  = Native(path);
        var options = FileOptions.Asynchronous | (intent == ReadIntent.Random ? FileOptions.RandomAccess : FileOptions.SequentialScan);
        try
        {
            return await Task.Run(() => (Stream)new FileStream(native, FileMode.Open, FileAccess.Read,
                                                               FileShare.ReadWrite | FileShare.Delete, BufferSize, options), ct)
                             .ConfigureAwait(false);
        }
        catch (Exception failure) when (LocationFailures.Translate(failure, path) is { } translated)
        {
            throw translated;
        }
    }

    public async ValueTask<ILocationWrite> BeginWriteAsync(string path, WriteMode mode, LocationEntry? expected, CancellationToken ct)
        => await WslWrite.OpenAsync(this, path, mode, expected, ct).ConfigureAwait(false);

    public async ValueTask MoveAsync(string from, string to, bool replace, CancellationToken ct)
        => await RenameAsync(from, to, replace, ct).ConfigureAwait(false);

    internal async ValueTask RenameAsync(string from, string to, bool replace, CancellationToken ct)
        => await _link.CallAsync(WslOp.Rename, w => w.String(from).String(to).Bool(replace), from, ct).ConfigureAwait(false);

    public async ValueTask CopyWithinAsync(string from, string to, bool replace, CancellationToken ct)
        => await _link.CallAsync(WslOp.Copy, w => w.String(from).String(to).Bool(replace), from, ct).ConfigureAwait(false);

    public async ValueTask DeleteAsync(string path, bool recursive, CancellationToken ct)
        => await _link.CallAsync(WslOp.Delete, w => w.String(path).Bool(recursive), path, ct).ConfigureAwait(false);

    public async ValueTask CreateDirectoryAsync(string path, CancellationToken ct)
        => await _link.CallAsync(WslOp.CreateDirectory, w => w.String(path), path, ct).ConfigureAwait(false);

    public ILocationWatch? Watch(string path, bool recursive) => new WslWatch(_link, path, recursive);

    public ValueTask DisposeAsync() => _link.DisposeAsync();
}

/// <summary>Changes under a distro path, as the helper's inotify reports them.</summary>
internal sealed class WslWatch : ILocationWatch
{
    private readonly IDisposable _watching;

    public WslWatch(WslHelperLink link, string path, bool recursive)
        => _watching = link.Watch(path, recursive, changed => Changed?.Invoke(changed));

    public event Action<string>? Changed;

    public void Dispose() => _watching.Dispose();
}
