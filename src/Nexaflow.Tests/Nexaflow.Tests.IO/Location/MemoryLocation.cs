using System.Runtime.CompilerServices;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Tests.IO.Location;

/// <summary>
/// A host the memory provider connects to: one tree of files and folders that outlives its sessions, so a dropped
/// session reconnects to the same content — what a server is to a real provider. Every operation waits
/// <see cref="Latency"/> first, so a regression in how many round trips something takes shows as time.
/// </summary>
internal sealed class MemoryLocationHost
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Node> _nodes;
    private long _clock = DateTime.Now.Ticks;

    public MemoryLocationHost(string name, TimeSpan latency, bool caseSensitive = true, string? password = null)
    {
        Name          = name;
        Latency       = latency;
        CaseSensitive = caseSensitive;
        Password      = password;
        _nodes = new Dictionary<string, Node>(caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
        {
            ["/"] = new Node(IsDirectory: true, [], DateTime.Now),
        };
    }

    public string Name { get; }

    public TimeSpan Latency { get; }

    public bool CaseSensitive { get; }

    /// <summary>The password a connect must present, or null to accept any.</summary>
    public string? Password { get; }

    /// <summary>Whether the host copies on its server (<see cref="LocationCapabilities.ServerSideCopy"/>), and how many
    /// times it has been asked to.</summary>
    public bool CopiesOnServer { get; init; }

    public int ServerCopies;

    /// <summary>Bumped by <see cref="Drop"/>: a session from an older generation finds its link gone.</summary>
    public int Generation { get; private set; }

    /// <summary>Drops every live session, as a server restart or a network blip does.</summary>
    public void Drop()
    {
        lock (_gate) Generation++;
    }

    internal sealed record Node(bool IsDirectory, byte[] Content, DateTime Modified);

    /// <summary>Runs <paramref name="work"/> on the tree under the host's lock.</summary>
    internal T With<T>(Func<Dictionary<string, Node>, T> work)
    {
        lock (_gate) return work(_nodes);
    }

    /// <summary>A modified time later than every one handed out before, so two writes are always two versions.</summary>
    internal DateTime NextStamp()
    {
        lock (_gate) return new DateTime(_clock += TimeSpan.TicksPerSecond, DateTimeKind.Local);
    }

    internal static string ParentOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash <= 0 ? "/" : path[..slash];
    }

    internal static string NameOf(string path) => path == "/" ? string.Empty : path[(path.LastIndexOf('/') + 1)..];

    internal static string Join(string folder, string name) => folder == "/" ? "/" + name : folder + "/" + name;
}

/// <summary>An <see cref="ILocationProvider"/> over a <see cref="MemoryLocationHost"/>, for the contract suite and
/// for the routing tests that need a remote location without a network.</summary>
internal sealed class MemoryLocationProvider(MemoryLocationHost host) : ILocationProvider
{
    public string Kind => "memory";

    public LocationCapabilities Capabilities
        => LocationCapabilities.RandomRead | LocationCapabilities.AtomicRename
           | (host.CaseSensitive ? LocationCapabilities.CaseSensitive : LocationCapabilities.None)
           | (host.CopiesOnServer ? LocationCapabilities.ServerSideCopy : LocationCapabilities.None);

    public async ValueTask<ILocationSession> ConnectAsync(ConnectionProfile profile, ICredentialSource credentials, CancellationToken ct)
    {
        await Task.Delay(host.Latency, ct);
        if (!string.Equals(profile.Host, host.Name, StringComparison.OrdinalIgnoreCase))
            throw new LocationException(LocationError.Disconnected, null, $"No host named '{profile.Host}' answered.");
        if (host.Password is not null
            && await credentials.GetSecretAsync(profile, SecretKind.Password, ct) != host.Password)
            throw new LocationException(LocationError.AuthFailed, null, $"'{profile.User}' could not sign in to '{profile.Host}'.");
        return new MemoryLocationSession(host, host.Generation);
    }
}

internal sealed class MemoryLocationSession(MemoryLocationHost host, int generation) : ILocationSession
{
    private async ValueTask HopAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (host.Latency > TimeSpan.Zero) await Task.Delay(host.Latency, ct);
        if (host.Generation != generation)
            throw new LocationException(LocationError.Disconnected, null, $"The link to '{host.Name}' dropped.");
    }

    private static LocationEntry EntryOf(string path, MemoryLocationHost.Node node)
        => new(MemoryLocationHost.NameOf(path), node.IsDirectory, node.Content.Length, node.Modified);

    public async IAsyncEnumerable<LocationEntry> EnumerateAsync(string path, [EnumeratorCancellation] CancellationToken ct)
    {
        await HopAsync(ct);
        var children = host.With(nodes =>
        {
            if (!nodes.TryGetValue(path, out var folder) || !folder.IsDirectory)
                throw new LocationException(LocationError.NotFound, path, $"No folder at '{path}'.");
            return nodes.Where(n => n.Key != "/" && MemoryLocationHost.ParentOf(n.Key) == path)
                        .Select(n => EntryOf(n.Key, n.Value))
                        .ToList();
        });
        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            yield return child;
        }
    }

    public async ValueTask<LocationEntry?> GetEntryAsync(string path, CancellationToken ct)
    {
        await HopAsync(ct);
        return host.With(nodes => nodes.TryGetValue(path, out var node) ? EntryOf(path, node) : null);
    }

    public async ValueTask<Stream> OpenReadAsync(string path, ReadIntent intent, CancellationToken ct)
    {
        await HopAsync(ct);
        var content = host.With(nodes => nodes.TryGetValue(path, out var node) && !node.IsDirectory
            ? node.Content
            : throw new LocationException(LocationError.NotFound, path, $"No file at '{path}'."));
        return new MemoryStream(content, writable: false);
    }

    public async ValueTask<ILocationWrite> BeginWriteAsync(string path, WriteMode mode, LocationEntry? expected, CancellationToken ct)
    {
        await HopAsync(ct);
        host.With(nodes =>
        {
            if (!nodes.TryGetValue(MemoryLocationHost.ParentOf(path), out var parent) || !parent.IsDirectory)
                throw new LocationException(LocationError.NotFound, path, $"No folder to hold '{path}'.");
            if (mode == WriteMode.CreateNew && nodes.ContainsKey(path))
                throw new LocationException(LocationError.Conflict, path, $"'{path}' already exists.");
            return 0;
        });
        return new MemoryLocationWrite(this, host, path, mode, expected);
    }

    private sealed class MemoryLocationWrite(MemoryLocationSession session, MemoryLocationHost host, string path,
                                             WriteMode mode, LocationEntry? expected) : ILocationWrite
    {
        private readonly MemoryStream _content = new();

        public Stream Content => _content;

        public async ValueTask CommitAsync(CancellationToken ct)
        {
            await session.HopAsync(ct);
            var stamp = host.NextStamp();
            host.With(nodes =>
            {
                nodes.TryGetValue(path, out var current);
                if (mode == WriteMode.CreateNew && current is not null)
                    throw new LocationException(LocationError.Conflict, path, $"'{path}' already exists.");
                if (expected is not null && (current is null || !EntryOf(path, current).IsSameVersion(expected)))
                    throw new LocationException(LocationError.Conflict, path, $"'{path}' changed since it was read.");
                byte[] bytes = mode == WriteMode.Append && current is not null
                    ? [.. current.Content, .. _content.ToArray()]
                    : _content.ToArray();
                nodes[path] = new MemoryLocationHost.Node(IsDirectory: false, bytes, stamp);
                return 0;
            });
        }

        public ValueTask DisposeAsync()
        {
            _content.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    public async ValueTask MoveAsync(string from, string to, bool replace, CancellationToken ct)
    {
        await HopAsync(ct);
        host.With(nodes =>
        {
            if (!nodes.TryGetValue(from, out var source))
                throw new LocationException(LocationError.NotFound, from, $"Nothing is at '{from}'.");
            if (nodes.TryGetValue(to, out var target) && (!replace || source.IsDirectory || target.IsDirectory))
                throw new LocationException(LocationError.Conflict, to, $"'{to}' already exists.");

            var moving = nodes.Keys.Where(k => k == from || k.StartsWith(from + "/", StringComparison.Ordinal)).ToList();
            foreach (var key in moving)
            {
                var node = nodes[key];
                nodes.Remove(key);
                nodes[to + key[from.Length..]] = node;
            }
            return 0;
        });
    }

    public async ValueTask CopyWithinAsync(string from, string to, bool replace, CancellationToken ct)
    {
        if (!host.CopiesOnServer) throw new NotSupportedException("This host does not copy on its server.");
        await HopAsync(ct);
        host.With(nodes =>
        {
            if (!nodes.TryGetValue(from, out var source) || source.IsDirectory)
                throw new LocationException(LocationError.NotFound, from, $"No file is at '{from}'.");
            if (nodes.ContainsKey(to) && !replace)
                throw new LocationException(LocationError.Conflict, to, $"'{to}' already exists.");
            nodes[to] = source with { Modified = host.NextStamp() };
            Interlocked.Increment(ref host.ServerCopies);
            return 0;
        });
    }

    public async ValueTask DeleteAsync(string path, bool recursive, CancellationToken ct)
    {
        await HopAsync(ct);
        host.With(nodes =>
        {
            if (!nodes.ContainsKey(path))
                throw new LocationException(LocationError.NotFound, path, $"Nothing is at '{path}'.");
            var below = nodes.Keys.Where(k => k.StartsWith(path + "/", StringComparison.Ordinal)).ToList();
            if (below.Count > 0 && !recursive)
                throw new LocationException(LocationError.Conflict, path, $"'{path}' still holds something.");
            foreach (var key in below) nodes.Remove(key);
            nodes.Remove(path);
            return 0;
        });
    }

    public async ValueTask CreateDirectoryAsync(string path, CancellationToken ct)
    {
        await HopAsync(ct);
        var stamp = host.NextStamp();
        host.With(nodes =>
        {
            var missing = new Stack<string>();
            for (var p = path; p != "/" && !nodes.ContainsKey(p); p = MemoryLocationHost.ParentOf(p)) missing.Push(p);
            if (nodes.TryGetValue(path, out var existing) && !existing.IsDirectory)
                throw new LocationException(LocationError.Conflict, path, $"A file is already at '{path}'.");
            while (missing.Count > 0) nodes[missing.Pop()] = new MemoryLocationHost.Node(IsDirectory: true, [], stamp);
            return 0;
        });
    }

    public ILocationWatch? Watch(string path, bool recursive) => null;

    public ILocationShell? Shell => null;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Hands out one connected session, connecting on first use — the shape the session pool takes.</summary>
internal sealed class MemoryLocationSessionSource(ILocationProvider provider, ConnectionProfile profile, ICredentialSource credentials)
    : ILocationSessionSource
{
    private ILocationSession? _session;

    public async ValueTask<ILocationSession> GetSessionAsync(CancellationToken ct)
        => _session ??= await provider.ConnectAsync(profile, credentials, ct);
}

/// <summary>Credentials that answer with one stored password and refuse to prompt.</summary>
internal sealed class StoredCredentials(string? password) : ICredentialSource
{
    public ValueTask<string?> GetSecretAsync(ConnectionProfile connection, SecretKind kind, CancellationToken ct)
        => ValueTask.FromResult(kind == SecretKind.Password ? password : null);

    public ValueTask<string?> PromptAsync(ConnectionProfile connection, string prompt, bool echo, CancellationToken ct)
        => ValueTask.FromResult<string?>(null);

    public ValueTask<bool> ConfirmHostKeyAsync(ConnectionProfile connection, string algorithm, string fingerprint, CancellationToken ct)
        => ValueTask.FromResult(false);
}
