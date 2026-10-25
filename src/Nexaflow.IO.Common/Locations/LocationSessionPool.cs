using Nexaflow.IO.Common.Terminal;

namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// One runtime's live sessions to its connected locations: one per connection, shared by everything that reaches it —
/// the browser, a viewer, a transfer, a terminal — and pooled as provider instances are.
/// <list type="bullet">
/// <item><see cref="Acquire"/> hands out a <see cref="LocationSet"/> of mounts for a list of connections; an entry is
/// keyed by everything that decides the endpoint (the connection less its label), so renaming one keeps its session.
/// Entries are counted by the sets holding them: acquire the new set before releasing the old, and a connection in
/// both never drops.</item>
/// <item>A session connects on first use and closes once nothing has used it for the idle time — no operation under
/// way, no stream, write, watch or terminal still open — so a quiet connection costs the server nothing. The next use
/// connects again.</item>
/// <item>A session whose link drops (<see cref="LocationError.Disconnected"/>) is let go, and the next use reconnects.</item>
/// </list>
/// </summary>
public sealed class LocationSessionPool : IAsyncDisposable
{
    /// <summary>How long a session may sit unused before it closes.</summary>
    public static readonly TimeSpan DefaultIdleTime = TimeSpan.FromMinutes(10);

    private readonly LocationProviderRegistry _providers;
    private readonly ICredentialSource _credentials;
    private readonly TimeSpan _idleTime;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<ConnectionProfile, PooledConnection> _entries = [];

    /// <param name="idleTime">How long a session may sit unused before it closes; <see cref="DefaultIdleTime"/> when not given.</param>
    /// <param name="time">The clock the idle time is kept by.</param>
    public LocationSessionPool(LocationProviderRegistry providers, ICredentialSource credentials, TimeSpan? idleTime = null,
                               TimeProvider? time = null)
    {
        _providers   = providers;
        _credentials = credentials;
        _idleTime    = idleTime ?? DefaultIdleTime;
        _time        = time ?? TimeProvider.System;
    }

    /// <summary>The live entries, for the tests that weigh sharing.</summary>
    internal int EntryCount { get { lock (_gate) return _entries.Count; } }

    /// <summary>
    /// The mounts for <paramref name="connections"/>, holding each one's pooled session. A connection no backend reaches
    /// is listed in <see cref="LocationSet.Unreachable"/> with why, and mounted nowhere.
    /// </summary>
    public LocationSet Acquire(IReadOnlyList<ConnectionProfile> connections)
    {
        var mounts      = new List<LocationMount>(connections.Count);
        var unreachable = new List<UnreachableLocation>();
        var held        = new List<ConnectionProfile>(connections.Count);
        lock (_gate)
        {
            foreach (var connection in connections)
            {
                if (!_providers.TryGet(connection.Kind, out var provider))
                {
                    unreachable.Add(new UnreachableLocation(connection, $"No backend reaches '{connection.Kind}' locations."));
                    continue;
                }

                var key = KeyOf(connection);
                if (!_entries.TryGetValue(key, out var entry))
                    _entries[key] = entry = new PooledConnection(connection, provider, _credentials, _idleTime, _time);
                entry.Holders++;
                held.Add(key);
                mounts.Add(new LocationMount(connection, provider.Capabilities, entry));
            }
        }
        return new LocationSet(mounts, unreachable, held);
    }

    /// <summary>Lets go of what <paramref name="set"/> holds; a connection no other set holds closes its session.</summary>
    public void Release(LocationSet? set)
    {
        if (set is null) return;
        var closing = new List<PooledConnection>();
        lock (_gate)
        {
            foreach (var key in set.Held)
            {
                if (!_entries.TryGetValue(key, out var entry) || --entry.Holders > 0) continue;
                _entries.Remove(key);
                closing.Add(entry);
            }
        }
        foreach (var entry in closing) _ = entry.CloseAsync();
    }

    /// <summary>Closes every session, whatever holds it: the runtime is going.</summary>
    public async ValueTask DisposeAsync()
    {
        PooledConnection[] closing;
        lock (_gate)
        {
            closing = [.. _entries.Values];
            _entries.Clear();
        }
        foreach (var entry in closing) await entry.CloseAsync().ConfigureAwait(false);
    }

    /// <summary>Everything that decides the endpoint: the connection less its label, which only people read.</summary>
    private static ConnectionProfile KeyOf(ConnectionProfile connection) => connection with { Label = string.Empty };
}

/// <summary>A connection that could not be mounted, and why.</summary>
public sealed record UnreachableLocation(ConnectionProfile Connection, string Reason);

/// <summary>What one <see cref="LocationSessionPool.Acquire"/> handed out: a mount per reachable connection, each with its
/// pooled session, held until the set is released.</summary>
public sealed class LocationSet
{
    internal LocationSet(IReadOnlyList<LocationMount> mounts, IReadOnlyList<UnreachableLocation> unreachable,
                         IReadOnlyList<ConnectionProfile> held)
    {
        Mounts      = mounts;
        Unreachable = unreachable;
        Held        = held;
    }

    /// <summary>What a runtime's file system registers (<see cref="LocationFileSystem.RegisterLocation"/>).</summary>
    public IReadOnlyList<LocationMount> Mounts { get; }

    public IReadOnlyList<UnreachableLocation> Unreachable { get; }

    /// <summary>The pool entries this set holds.</summary>
    internal IReadOnlyList<ConnectionProfile> Held { get; }
}

/// <summary>
/// One connection's place in the pool: its live session, connected once however many ask at the same time, and how much
/// of it is in use — operations under way plus streams, writes, watches and terminals still open — which is what keeps
/// it from closing while idle.
/// </summary>
internal sealed class PooledConnection(ConnectionProfile connection, ILocationProvider provider, ICredentialSource credentials,
                                       TimeSpan idleTime, TimeProvider time) : ILocationSessionSource
{
    private readonly Lock _gate = new();
    private Task<PooledLocationSession>? _connecting;
    private PooledLocationSession? _session;
    private ITimer? _idle;
    private int _inUse;
    private bool _closed;

    /// <summary>How many sets hold this entry. Guarded by the pool's lock.</summary>
    public int Holders;

    public async ValueTask<ILocationSession> GetSessionAsync(CancellationToken ct)
    {
        Task<PooledLocationSession> connecting;
        lock (_gate)
        {
            if (_closed) throw Gone();
            if (_session is { } live)
            {
                Restart();
                return live;
            }
            // Started apart from the lock: a connect that finishes at once must not clear _connecting before it is set.
            connecting = _connecting ??= Task.Run(ConnectAsync);
        }
        return await connecting.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Connects once for every caller waiting; one caller giving up does not stop the others' connect.</summary>
    private async Task<PooledLocationSession> ConnectAsync()
    {
        ILocationSession inner;
        try { inner = await provider.ConnectAsync(connection, credentials, CancellationToken.None).ConfigureAwait(false); }
        catch
        {
            lock (_gate) _connecting = null;
            throw;
        }

        lock (_gate)
        {
            _connecting = null;
            if (!_closed)
            {
                _session = new PooledLocationSession(inner, this);
                Restart();
                return _session;
            }
        }
        await inner.DisposeAsync().ConfigureAwait(false);
        throw Gone();
    }

    /// <summary>Something began using the session: an operation, or a handle it opened.</summary>
    public void Enter()
    {
        lock (_gate)
        {
            _inUse++;
            _idle?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Something stopped using the session; once nothing does, the idle time starts.</summary>
    public void Exit()
    {
        lock (_gate)
        {
            if (--_inUse == 0) Restart();
        }
    }

    /// <summary>The session's link dropped: let it go, so the next use connects afresh.</summary>
    public void Lost(PooledLocationSession session)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_session, session)) return;
            _session = null;
        }
        _ = session.RetireAsync();
    }

    /// <summary>Starts the idle time over, unless something is using the session. Under the lock.</summary>
    private void Restart()
    {
        if (_session is null || _inUse > 0) return;
        _idle ??= time.CreateTimer(_ => Idled(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _idle.Change(idleTime, Timeout.InfiniteTimeSpan);
    }

    private void Idled()
    {
        PooledLocationSession? session;
        lock (_gate)
        {
            if (_inUse > 0 || _session is null) return;
            session  = _session;
            _session = null;
        }
        _ = session.RetireAsync();
    }

    public async Task CloseAsync()
    {
        PooledLocationSession? session;
        lock (_gate)
        {
            _closed  = true;
            session  = _session;
            _session = null;
            _idle?.Dispose();
            _idle = null;
        }
        if (session is not null) await session.RetireAsync().ConfigureAwait(false);
    }

    private LocationException Gone()
        => new(LocationError.Disconnected, null, $"The connection to '{connection.Label}' is no longer held by this window.");
}

/// <summary>
/// A pooled session as callers see it: the provider's session, with every operation and every handle it opens counted as
/// use, and a dropped link reported to the pool. Once retired — idle, lost or closed — it refuses further work as
/// <see cref="LocationError.Disconnected"/>, and the next <see cref="ILocationSessionSource.GetSessionAsync"/> connects anew.
/// </summary>
internal sealed class PooledLocationSession(ILocationSession inner, PooledConnection owner) : ILocationSession
{
    private int _retired;

    public async Task RetireAsync()
    {
        if (Interlocked.Exchange(ref _retired, 1) == 1) return;
        try { await inner.DisposeAsync().ConfigureAwait(false); }
        catch { /* a session already broken has nothing left to close */ }
    }

    private void Begin()
    {
        if (Volatile.Read(ref _retired) == 1)
            throw new LocationException(LocationError.Disconnected, null, "The session closed; ask for it again.");
        owner.Enter();
    }

    /// <summary>Hands a dropped link to the pool; every other failure is the caller's.</summary>
    private bool Dropped(Exception error)
    {
        if (error is LocationException { Error: LocationError.Disconnected }) owner.Lost(this);
        return false;
    }

    private async ValueTask<T> Run<T>(Func<ValueTask<T>> operation)
    {
        Begin();
        try { return await operation().ConfigureAwait(false); }
        catch (Exception error) when (Dropped(error)) { throw; }
        finally { owner.Exit(); }
    }

    private async ValueTask Run(Func<ValueTask> operation)
    {
        Begin();
        try { await operation().ConfigureAwait(false); }
        catch (Exception error) when (Dropped(error)) { throw; }
        finally { owner.Exit(); }
    }

    public async IAsyncEnumerable<LocationEntry> EnumerateAsync(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        Begin();
        try
        {
            await using var listing = inner.EnumerateAsync(path, ct).GetAsyncEnumerator(ct);
            while (true)
            {
                bool more;
                try { more = await listing.MoveNextAsync().ConfigureAwait(false); }
                catch (Exception error) when (Dropped(error)) { throw; }
                if (!more) yield break;
                yield return listing.Current;
            }
        }
        finally { owner.Exit(); }
    }

    public ValueTask<LocationEntry?> GetEntryAsync(string path, CancellationToken ct) => Run(() => inner.GetEntryAsync(path, ct));

    public async ValueTask<Stream> OpenReadAsync(string path, ReadIntent intent, CancellationToken ct)
    {
        var stream = await Run(() => inner.OpenReadAsync(path, intent, ct)).ConfigureAwait(false);
        owner.Enter();
        return new HeldStream(stream, owner);
    }

    public async ValueTask<ILocationWrite> BeginWriteAsync(string path, WriteMode mode, LocationEntry? expected, CancellationToken ct)
    {
        var write = await Run(() => inner.BeginWriteAsync(path, mode, expected, ct)).ConfigureAwait(false);
        owner.Enter();
        return new HeldWrite(write, this, owner);
    }

    public ValueTask MoveAsync(string from, string to, bool replace, CancellationToken ct) => Run(() => inner.MoveAsync(from, to, replace, ct));

    public ValueTask CopyWithinAsync(string from, string to, bool replace, CancellationToken ct) => Run(() => inner.CopyWithinAsync(from, to, replace, ct));

    public ValueTask DeleteAsync(string path, bool recursive, CancellationToken ct) => Run(() => inner.DeleteAsync(path, recursive, ct));

    public ValueTask CreateDirectoryAsync(string path, CancellationToken ct) => Run(() => inner.CreateDirectoryAsync(path, ct));

    public ILocationWatch? Watch(string path, bool recursive)
    {
        Begin();
        try
        {
            if (inner.Watch(path, recursive) is not { } watch) return null;
            owner.Enter();
            return new HeldWatch(watch, owner);
        }
        finally { owner.Exit(); }
    }

    public ILocationShell? Shell => inner.Shell is { } shell ? new HeldShell(shell, this, owner) : null;

    /// <summary>The pool owns the provider's session; a caller letting go of this one closes nothing.</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal ValueTask CommitAsync(ILocationWrite write, CancellationToken ct) => Run(() => write.CommitAsync(ct));

    internal ValueTask<ITerminalTransport> OpenShellAsync(ILocationShell shell, string workingDirectory, int columns, int rows, CancellationToken ct)
        => Run(() => shell.OpenAsync(workingDirectory, columns, rows, ct));

    /// <summary>A stream the session opened, counted as use until it is closed.</summary>
    private sealed class HeldStream(Stream inner, PooledConnection owner) : Stream
    {
        private int _released;

        public override bool CanRead  => inner.CanRead;
        public override bool CanSeek  => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length   => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => inner.ReadAsync(buffer, offset, count, ct);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                Release();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync().ConfigureAwait(false);
            Release();
            await base.DisposeAsync().ConfigureAwait(false);
        }

        private void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) owner.Exit();
        }
    }

    /// <summary>A write the session began, counted as use until it is committed or abandoned.</summary>
    private sealed class HeldWrite(ILocationWrite inner, PooledLocationSession session, PooledConnection owner) : ILocationWrite
    {
        private int _released;

        public Stream Content => inner.Content;

        public ValueTask CommitAsync(CancellationToken ct) => session.CommitAsync(inner, ct);

        public async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync().ConfigureAwait(false);
            if (Interlocked.Exchange(ref _released, 1) == 0) owner.Exit();
        }
    }

    /// <summary>A watch the session holds open, counted as use until it is disposed.</summary>
    private sealed class HeldWatch(ILocationWatch inner, PooledConnection owner) : ILocationWatch
    {
        private int _released;

        public event Action<string>? Changed
        {
            add    => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public void Dispose()
        {
            inner.Dispose();
            if (Interlocked.Exchange(ref _released, 1) == 0) owner.Exit();
        }
    }

    /// <summary>The location's shell, its terminals counted as use until each is closed.</summary>
    private sealed class HeldShell(ILocationShell inner, PooledLocationSession session, PooledConnection owner) : ILocationShell
    {
        public async ValueTask<ITerminalTransport> OpenAsync(string workingDirectory, int columns, int rows, CancellationToken ct)
        {
            var transport = await session.OpenShellAsync(inner, workingDirectory, columns, rows, ct).ConfigureAwait(false);
            owner.Enter();
            return new HeldTerminal(transport, owner);
        }
    }

    /// <summary>A terminal on the location, counted as use until it is closed.</summary>
    private sealed class HeldTerminal(ITerminalTransport inner, PooledConnection owner) : ITerminalTransport
    {
        private int _released;

        public event Action<ReadOnlyMemory<byte>>? Output { add => inner.Output += value; remove => inner.Output -= value; }
        public event Action<string>? Failed { add => inner.Failed += value; remove => inner.Failed -= value; }
        public event Action<int>? Exited { add => inner.Exited += value; remove => inner.Exited -= value; }

        public bool IsRunning => inner.IsRunning;
        public void Write(ReadOnlySpan<byte> input) => inner.Write(input);
        public void Resize(int columns, int rows) => inner.Resize(columns, rows);
        public void Interrupt() => inner.Interrupt();
        public void TerminateForeground() => inner.TerminateForeground();

        public void Dispose()
        {
            inner.Dispose();
            if (Interlocked.Exchange(ref _released, 1) == 0) owner.Exit();
        }
    }
}
