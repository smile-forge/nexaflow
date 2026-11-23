using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.Versioning;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Client;

/// <summary>
/// The app's side of the shared sections large blocks of bytes live in (<see cref="SharedBytes"/>). The host makes every section and
/// reuses it block after block; the app opens each by the name the host gave it once, and keeps that view mapped until the host says
/// it has closed the section (<see cref="SectionClosed"/>) and nothing of the app's still reads it — mapping a view afresh costs a soft
/// page fault for every page. A block the app is given — a reply's, or one it asked for to write a request into — is the host's again
/// when the app disposes it, or once the request it was sent in ends.
/// </summary>
/// <param name="send">Sends a message on the connection the sections belong to.</param>
[SupportedOSPlatform("windows")]
internal sealed class ClientSections(Func<HostMessage, Task> send) : ISectionHolder
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource<HostMessage>> _asked = new();
    private readonly Lock _gate = new();
    private readonly Dictionary<long, View> _views = [];
    private long _nextTicket;

    /// <summary>The app's view of one section, and how many of its blocks are out.</summary>
    private sealed class View(SharedSection mapping)
    {
        public SharedSection Mapping => mapping;

        public int Users { get; set; }

        /// <summary>The host has closed it, or the connection is gone: unmapped once no block reads it.</summary>
        public bool Closing { get; set; }
    }

    public SharedBytes Read(long id, string name, int length, int capacity) => Lend(id, name, length, capacity);

    /// <summary>Bytes to fill and send in a request: an array when small, else a section the host gave for them.</summary>
    /// <exception cref="AiHostException">The host refused one, or the connection was lost while it was asked for.</exception>
    public async Task<SharedBytes> AllocateAsync(int length, CancellationToken ct)
    {
        if (length <= SharedBytes.InlineLimit) return SharedBytes.Of(new byte[length]);
        long ticket = Interlocked.Increment(ref _nextTicket);
        var answer = new TaskCompletionSource<HostMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _asked[ticket] = answer;
        try
        {
            await send(new AllocateSection(ticket, length)).ConfigureAwait(false);
            var allocated = await answer.Task.WaitAsync(ct).ConfigureAwait(false) switch
            {
                SectionAllocated made => made,
                SectionRefused refused => throw new AiHostException(AiHostFailure.Request, refused.Message, HostErrorKind.Refused),
                var other => throw new InvalidDataException($"The AI host answered a section with {other.GetType().Name}."),
            };
            return Lend(allocated.Id, allocated.Name, length, allocated.Capacity);
        }
        finally { _asked.TryRemove(ticket, out _); }
    }

    /// <summary>The host's answer to an asking.</summary>
    public void Answer(long ticket, HostMessage answer)
    {
        if (_asked.TryGetValue(ticket, out var asked)) asked.TrySetResult(answer);
    }

    /// <summary>The host has closed section <paramref name="id"/>: its view goes once no block of the app's reads it.</summary>
    public void Closed(long id)
    {
        lock (_gate)
            if (_views.TryGetValue(id, out var view)) Close(id, view);
    }

    /// <summary>The connection is gone: every asking still open fails, and each view goes once no block reads it.</summary>
    public void Lose(string reason)
    {
        foreach (var asked in _asked.Values) asked.TrySetException(new AiHostException(AiHostFailure.Lost, reason));
        lock (_gate)
            foreach (var (id, view) in _views.ToList()) Close(id, view);
    }

    /// <summary>A block of section <paramref name="id"/>, through the app's view of it — opened now if it is the first.</summary>
    private SharedBytes Lend(long id, string name, int length, int capacity)
    {
        View view;
        lock (_gate)
        {
            if (!_views.TryGetValue(id, out view!))
            {
                SharedSection mapping;
                try { mapping = SharedSection.Open(name, capacity); }
                catch (Win32Exception ex) { throw new InvalidDataException($"The AI host's shared section {name} could not be opened: {ex.Message}", ex); }
                _views[id] = view = new View(mapping);
            }
            else if (view.Closing || view.Mapping.Memory.Length != capacity)
                throw new InvalidDataException($"The AI host named section {id}, which it has closed or made at another size.");
            if (length > capacity) throw new InvalidDataException($"The AI host named {length} bytes of a {capacity}-byte section.");
            view.Users++;
        }
        return new SharedBytes(this, id, name, view.Mapping, length, released: bytes => Returned(id, view, release: !bytes.Sent));
    }

    /// <summary>A block is disposed: the host is told it may reuse the section — unless the block was sent in a request, which gives it back
    /// itself — and the view goes if the host has closed the section and this was its last block.</summary>
    private void Returned(long id, View view, bool release)
    {
        if (release) _ = SayAsync(new ReleaseSection(id));
        lock (_gate)
        {
            view.Users--;
            if (view.Closing) Close(id, view);
        }
    }

    private void Close(long id, View view)
    {
        view.Closing = true;
        if (view.Users > 0) return;
        if (_views.TryGetValue(id, out var held) && ReferenceEquals(held, view)) _views.Remove(id);
        ((IDisposable)view.Mapping).Dispose();
    }

    private async Task SayAsync(HostMessage message)
    {
        try { await send(message).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { /* the host is gone, and its sections with it */ }
    }
}
