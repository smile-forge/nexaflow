using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost;

/// <summary>
/// The shared sections the host made for one app's connection. Only the host makes them — in the global namespace when it is a
/// service, which holds the privilege, else in its session's — each open to the app's account and the host's alone. They are
/// kept and reused, not made for each block: a section made afresh takes a page fault at both ends for every page it holds, one
/// reused takes none, so a large picture crosses several times faster in a kept one than through the pipe. Each is the host's
/// while it fills it for a reply, the app's once sent or asked for, a request's while one that named it runs, and idle between;
/// one idle past <see cref="IdleClose"/>, or in the way of a new one past <see cref="MostBytes"/>, is closed and the app told
/// (<see cref="SectionClosed"/>). The host finds a section it is sent by its own id, never by a name, and only one the app holds;
/// an id is random and never reused, so one from a section since closed, or a host since restarted, names nothing.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class HostSections : ISectionHolder, IDisposable
{
    /// <summary>The most bytes one connection's sections may hold at once, idle ones with them: room for the largest pictures the
    /// tools take, with their results beside them.</summary>
    public const long MostBytes = 6L << 30;

    /// <summary>How long a section may sit idle before it is closed and its memory given back.</summary>
    public static readonly TimeSpan IdleClose = TimeSpan.FromMinutes(2);

    private readonly Lock _gate = new();
    private readonly Dictionary<long, Section> _sections = [];
    private readonly string _prefix;
    private readonly string _descriptor;
    private readonly TimeProvider _clock;
    private readonly Func<HostMessage, Task> _tell;
    private readonly ITimer _trimming;
    private long _held;
    private bool _disposed;

    private enum Use { Idle, Filling, Lent, Named }

    /// <summary>One section: its mapping, held for as long as the section is, and what it is for now — <see cref="Bytes"/>, this use of it.</summary>
    private sealed class Section(long id, string name, SharedSection mapping)
    {
        public long Id => id;

        public string Name => name;

        public SharedSection Mapping => mapping;

        public int Capacity => mapping.Memory.Length;

        public Use Use { get; set; }

        public SharedBytes? Bytes { get; set; }

        public long IdleSince { get; set; }
    }

    /// <param name="client">The account the app runs as, which alone besides the host may open these sections.</param>
    /// <param name="tell">Sends the app a message on this connection: the sections closed.</param>
    public HostSections(SecurityIdentifier client, TimeProvider clock, Func<HostMessage, Task> tell)
    {
        var host = WindowsIdentity.GetCurrent().User!;
        _descriptor = $"D:P(A;;GA;;;{host.Value})(A;;GA;;;{client.Value})";
        _prefix = Namespace(_descriptor);
        _clock = clock;
        _tell = tell;
        _trimming = clock.CreateTimer(_ => Trim(), null, IdleClose, IdleClose);
    }

    /// <summary>A section of at least <paramref name="length"/> bytes for the app to write a request's block into, the app's until it
    /// sends or releases it; null when its sections already hold all they may.</summary>
    public SharedBytes? Allocate(int length) => Take(length, Use.Lent);

    /// <summary>Bytes for a reply: in the reply's frame when small, else in a section, the host's until it is sent.</summary>
    /// <exception cref="HostRequestException">The app's sections already hold all they may.</exception>
    public SharedBytes Create(int length)
        => length <= SharedBytes.InlineLimit ? SharedBytes.Of(new byte[length])
           : Take(length, Use.Filling) ?? throw new HostRequestException(HostErrorKind.Refused,
                 $"The app's shared sections already hold {Volatile.Read(ref _held) >> 20} MiB; {length >> 20} MiB more would pass what one app may.");

    public SharedBytes Read(long id, string name, int length, int capacity)
    {
        lock (_gate)
        {
            if (!_sections.TryGetValue(id, out var section) || section.Use != Use.Lent || capacity != section.Capacity || length > capacity)
                throw new InvalidDataException($"A message names shared section {id}, which this app does not hold from this host.");
            section.Use = Use.Named;
            return section.Bytes = Wrap(section, length);
        }
    }

    /// <summary>The app is done with section <paramref name="id"/> — a reply's it has read, or one it asked for and will not send: idle
    /// again, to be reused.</summary>
    public void Release(long id)
    {
        lock (_gate)
            if (_sections.TryGetValue(id, out var section) && (section.Use == Use.Lent || section.Use == Use.Filling && section.Bytes!.Sent))
                Idle(section);
    }

    /// <summary>
    /// A request is done with <paramref name="bytes"/>: a section it was sent is idle again; one it made for a reply is the app's if it
    /// was sent, else idle. Bytes no longer this section's use — the app released them already — are left as they are.
    /// </summary>
    public void Return(SharedBytes bytes)
    {
        if (bytes.SectionId is not { } id) return;
        lock (_gate)
        {
            if (!_sections.TryGetValue(id, out var section) || !ReferenceEquals(section.Bytes, bytes)) return;
            if (section.Use == Use.Named) Idle(section);
            else if (section.Use == Use.Filling)
            {
                if (bytes.Sent) section.Use = Use.Lent;
                else Idle(section);
            }
        }
    }

    /// <summary>Closes every section idle for <see cref="IdleClose"/> or more, and tells the app.</summary>
    public void Trim()
    {
        List<long> closed = [];
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var section in _sections.Values.Where(s => s.Use == Use.Idle && _clock.GetElapsedTime(s.IdleSince) >= IdleClose).ToList())
            {
                Close(section);
                closed.Add(section.Id);
            }
        }
        Tell(closed);
    }

    /// <summary>The size a section is made at for <paramref name="length"/> bytes: rounded up to a quarter of the power of two at or below
    /// it, so pictures of about one size share sections, each at most a quarter larger than asked.</summary>
    internal static int Capacity(int length)
    {
        long step = Math.Max(Environment.SystemPageSize, (1L << BitOperations.Log2((uint)length)) / 4);
        return (int)Math.Min((length + step - 1) / step * step, Array.MaxLength);
    }

    private SharedBytes? Take(int length, Use use)
    {
        List<long> closed = [];
        SharedBytes? bytes = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var section = _sections.Values.Where(s => s.Use == Use.Idle && s.Capacity >= length).MinBy(s => s.Capacity);
            if (section is null)
            {
                int capacity = Capacity(length);
                // Idle sections, the longest idle first, give way to one that would pass what the app may hold.
                foreach (var idle in _sections.Values.Where(s => s.Use == Use.Idle).OrderBy(s => s.IdleSince).ToList())
                {
                    if (_held + capacity <= MostBytes) break;
                    Close(idle);
                    closed.Add(idle.Id);
                }
                if (_held + capacity <= MostBytes) section = Make(capacity);
            }
            if (section is not null)
            {
                section.Use = use;
                bytes = section.Bytes = Wrap(section, length);
            }
        }
        Tell(closed);
        return bytes;
    }

    private Section Make(int capacity)
    {
        long id = RandomNumberGenerator.GetInt32(1, int.MaxValue) * (long)int.MaxValue + RandomNumberGenerator.GetInt32(int.MaxValue);
        string name = $"{_prefix}SmileForge.AiHost.{Guid.NewGuid():N}";
        var section = new Section(id, name, SharedSection.Create(name, capacity, _descriptor));
        _sections[id] = section;
        _held += capacity;
        return section;
    }

    private SharedBytes Wrap(Section section, int length) => new(this, section.Id, section.Name, section.Mapping, length, released: null);

    private void Idle(Section section)
    {
        section.Bytes?.Retire();
        section.Bytes = null;
        section.Use = Use.Idle;
        section.IdleSince = _clock.GetTimestamp();
    }

    private void Close(Section section)
    {
        section.Bytes?.Retire();
        _sections.Remove(section.Id);
        _held -= section.Capacity;
        ((IDisposable)section.Mapping).Dispose();
    }

    private void Tell(List<long> closed)
    {
        foreach (long id in closed) _ = TellAsync(new SectionClosed(id));
    }

    private async Task TellAsync(HostMessage message)
    {
        try { await _tell(message); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { /* the app has gone, and its views with it */ }
    }

    /// <summary>The namespace this host makes sections in: the global one where it may — a service — else its session's.</summary>
    private static string Namespace(string descriptor)
    {
        try
        {
            using var probe = SharedSection.Create($@"Global\SmileForge.AiHost.{Guid.NewGuid():N}", 1, descriptor);
            return @"Global\";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return @"Local\";
        }
    }

    public void Dispose()
    {
        _trimming.Dispose();
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var section in _sections.Values.ToList()) Close(section);
        }
    }
}

/// <summary>
/// The sections one request uses: those its message and its input name, which go back to the pool when it ends, and those it makes
/// for its replies — the app's once sent, back to the pool if never sent. Handlers make reply bytes through <see cref="Current"/>;
/// outside a request, or where the host makes no sections, bytes cross in their frames.
/// </summary>
internal sealed class RequestSections(HostSections? sections)
{
    private static readonly AsyncLocal<RequestSections?> _current = new();
    private readonly List<SharedBytes> _named = [];
    private readonly List<SharedBytes> _made = [];

    /// <summary>The running request's.</summary>
    public static RequestSections? Current => _current.Value;

    /// <summary>Bytes for a reply of the running request: a section when large and the host makes them, else an array.</summary>
    public static SharedBytes Create(int length) => Current?.Make(length) ?? SharedBytes.Of(new byte[length]);

    /// <summary>Makes this the request running on the calling flow, until the returned value is disposed.</summary>
    public IDisposable Enter()
    {
        var outer = _current.Value;
        _current.Value = this;
        return new Exit(outer);
    }

    /// <summary>Ties sections a message of this request named to it.</summary>
    public void Take(IEnumerable<SharedBytes> named)
    {
        lock (_named) _named.AddRange(named);
    }

    /// <summary>A section the request was sent and has read all it needs of: back to the pool now rather than when the request ends —
    /// a request that lasts, as a selection does, need not hold the picture it was sent.</summary>
    public static void Done(SharedBytes bytes) => Current?.Close(bytes);

    private void Close(SharedBytes bytes)
    {
        if (sections is null || bytes.SectionId is null) return;
        lock (_named)
            if (_named.Remove(bytes)) sections.Return(bytes);
    }

    private SharedBytes? Make(int length)
    {
        if (sections is null) return null;
        var bytes = sections.Create(length);
        if (bytes.SectionId is not null) lock (_made) _made.Add(bytes);
        return bytes;
    }

    /// <summary>The request has ended: what it was sent goes back to the pool, what it made goes to the app if sent, else back too.</summary>
    public void End()
    {
        if (sections is null) return;
        lock (_named) foreach (var bytes in _named) sections.Return(bytes);
        lock (_made) foreach (var bytes in _made) sections.Return(bytes);
    }

    private sealed class Exit(RequestSections? outer) : IDisposable
    {
        public void Dispose() => _current.Value = outer;
    }
}
