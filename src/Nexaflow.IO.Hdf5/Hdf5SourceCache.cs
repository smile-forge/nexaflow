namespace Nexaflow.IO.Hdf5;

/// <summary>
/// Keeps recently used files open between calls. The VFS opens a session for every listing, stat and read, so
/// without this each one would open and re-parse the file. A <see cref="Hdf5Lease"/> holds a source open; when
/// the last lease on it ends the source lingers for <see cref="Linger"/> for the next call, then closes — no
/// handle outlives its use by long. A file is keyed by path, length and write time, so a changed file opens
/// afresh and the stale source closes once nothing holds it.
/// </summary>
public sealed class Hdf5SourceCache : IDisposable
{
    /// <summary>The cache the VFS handler and the viewer share, so browsing and viewing parse a file once.</summary>
    public static Hdf5SourceCache Shared { get; } = new(TimeSpan.FromSeconds(20));

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, IHdf5Source> _open;
    private readonly TimeProvider _time;
    private readonly ITimer _sweep;
    private bool _disposed;

    public Hdf5SourceCache(TimeSpan linger, TimeProvider? time = null, Func<string, IHdf5Source>? open = null)
    {
        Linger = linger;
        _time  = time ?? TimeProvider.System;
        _open  = open ?? Hdf5Source.Open;
        _sweep = _time.CreateTimer(_ => Sweep(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public TimeSpan Linger { get; }

    /// <summary>How many sources are open, leased or lingering.</summary>
    public int OpenCount { get { lock (_gate) return _entries.Count; } }

    /// <summary>A lease on the file at a path, opening it unless an unchanged copy is already open.</summary>
    public Hdf5Lease Acquire(string path)
    {
        var full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        if (!info.Exists) throw new FileNotFoundException("No such HDF5 file.", full);
        var key = $"{full}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(key, out var hit)) return Lease(hit);
        }

        var source = _open(full);
        lock (_gate)
        {
            if (_disposed) { source.Dispose(); throw new ObjectDisposedException(nameof(Hdf5SourceCache)); }
            if (_entries.TryGetValue(key, out var raced)) { source.Dispose(); return Lease(raced); }
            var entry = new Entry(key, source);
            _entries[key] = entry;
            return Lease(entry);
        }
    }

    /// <summary>
    /// A lease for a stream the caller hands over. A stream on a file on disk is closed and the file leased by
    /// path, so it joins the cache; any other stream becomes a source of its own that closes with the lease.
    /// </summary>
    public Hdf5Lease Acquire(Stream stream)
    {
        if (stream is FileStream fs && File.Exists(fs.Name))
        {
            var path = fs.Name;
            stream.Dispose();
            return Acquire(path);
        }
        return new Hdf5Lease(Hdf5Source.Open(stream), release: null);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _sweep.Dispose();
            foreach (var e in _entries.Values) e.Source.Dispose();
            _entries.Clear();
        }
    }

    /// <summary>Closes every source idle for at least <see cref="Linger"/>.</summary>
    internal void Sweep()
    {
        lock (_gate)
        {
            if (_disposed) return;
            var now = _time.GetUtcNow();
            foreach (var e in _entries.Values.Where(e => e.Leases == 0 && now - e.IdleSince >= Linger).ToList())
            {
                _entries.Remove(e.Key);
                e.Source.Dispose();
            }
            if (_entries.Values.Any(e => e.Leases == 0)) _sweep.Change(Linger, Timeout.InfiniteTimeSpan);
        }
    }

    private Hdf5Lease Lease(Entry e)
    {
        e.Leases++;
        return new Hdf5Lease(e.Source, () => Release(e));
    }

    private void Release(Entry e)
    {
        lock (_gate)
        {
            if (_disposed || --e.Leases > 0) return;
            e.IdleSince = _time.GetUtcNow();
            _sweep.Change(Linger, Timeout.InfiniteTimeSpan);
        }
    }

    private sealed class Entry(string key, IHdf5Source source)
    {
        public string Key { get; } = key;
        public IHdf5Source Source { get; } = source;
        public int Leases { get; set; }
        public DateTimeOffset IdleSince { get; set; }
    }
}

/// <summary>Use of an open source; disposing it ends the use, once.</summary>
public sealed class Hdf5Lease : IDisposable
{
    private Action? _release;
    private readonly bool _ownsSource;

    internal Hdf5Lease(IHdf5Source source, Action? release)
    {
        Source      = source;
        _release    = release;
        _ownsSource = release is null;
    }

    public IHdf5Source Source { get; }

    public void Dispose()
    {
        var release = Interlocked.Exchange(ref _release, null);
        if (release is not null) release();
        else if (_ownsSource) Source.Dispose();
    }
}
