using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Llama;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Generation;

/// <summary>
/// What a conversation log keeps for the clients its requests serve (<see cref="ClientIdentity"/>) — kept conversations, openings,
/// prompts read lately — each client's apart from every other's, most recently used first, and only its own found by its requests. One
/// capacity holds them all: a lone client may fill it, and beside others each is owed an equal part of it. Room is made first from the
/// client holding the most, its least recently used first — between clients holding as many, whichever's was used least recently — so a
/// busy client lets its own go before another's. Guarded by its log's gate.
/// </summary>
/// <remarks>Letting one go weighs each client present once, by what it holds and its least recently used, never every entry: clients
/// are few, and each client's own are found from the end they are let go from.</remarks>
internal sealed class ClientShares<T>(int capacity) where T : class
{
    /// <summary>An entry, and when it was last used: what tells the least recently used apart across clients.</summary>
    private readonly record struct Entry(T Item, long Used);

    /// <summary>Each client present's entries, most recently used first; a client holding none is not present.</summary>
    private readonly Dictionary<ClientIdentity, LinkedList<Entry>> _clients = [];

    /// <summary>Uses counted so far: each entry's <see cref="Entry.Used"/>.</summary>
    private long _uses;

    /// <summary>How many it holds, every client's together, before room is to be made.</summary>
    public int Capacity { get; } = capacity;

    /// <summary>How many it holds, every client's together.</summary>
    public int Count { get; private set; }

    /// <summary>Every client's entries.</summary>
    public IEnumerable<T> All => _clients.Values.SelectMany(entries => entries.Select(entry => entry.Item));

    /// <summary><paramref name="client"/>'s entries, most recently used first — none of another client's.</summary>
    public IEnumerable<T> Of(ClientIdentity client)
        => _clients.TryGetValue(client, out var entries) ? entries.Select(entry => entry.Item) : [];

    /// <summary>Holds <paramref name="item"/> for <paramref name="client"/>, as its most recently used.</summary>
    public void Add(ClientIdentity client, T item)
    {
        if (!_clients.TryGetValue(client, out var entries)) _clients.Add(client, entries = new());
        entries.AddFirst(new Entry(item, ++_uses));
        Count++;
    }

    /// <summary>Whether it holds <paramref name="item"/> for <paramref name="client"/>.</summary>
    public bool Contains(ClientIdentity client, T item) => Of(client).Any(held => ReferenceEquals(held, item));

    /// <summary><paramref name="client"/>'s most recently used entry that <paramref name="match"/> takes, made its most recently used;
    /// null where it has none.</summary>
    public T? Use(ClientIdentity client, Func<T, bool> match)
    {
        if (!_clients.TryGetValue(client, out var entries)) return null;
        for (var node = entries.First; node is not null; node = node.Next)
        {
            if (!match(node.Value.Item)) continue;
            entries.Remove(node);
            node.Value = node.Value with { Used = ++_uses };
            entries.AddFirst(node);
            return node.Value.Item;
        }
        return null;
    }

    /// <summary>Takes out <paramref name="client"/>'s most recently used entry that <paramref name="match"/> takes; null where it has
    /// none.</summary>
    public T? Take(ClientIdentity client, Func<T, bool> match)
    {
        if (!_clients.TryGetValue(client, out var entries)) return null;
        for (var node = entries.First; node is not null; node = node.Next)
            if (match(node.Value.Item))
            {
                Unlink(client, entries, node);
                return node.Value.Item;
            }
        return null;
    }

    /// <summary>Takes out <paramref name="item"/>, held for <paramref name="client"/>; false where it is not held for it.</summary>
    public bool Remove(ClientIdentity client, T item) => Take(client, held => ReferenceEquals(held, item)) is not null;

    /// <summary>
    /// Lets one entry go and gives it back; null where none may go. It is the least recently used of those <paramref name="spare"/> lets
    /// go — every one, where null — of the client holding the most, between clients holding as many the one whose is the least recently
    /// used. <paramref name="adding"/>, where given, is about to hold one more and is counted with it, so it lets its own go where it would
    /// otherwise hold as many as another. Where <paramref name="beyondShare"/>, only a client holding more than an equal part of
    /// <see cref="Capacity"/> among the clients present, <paramref name="adding"/> among them, gives one up: past capacity, at least one
    /// always does.
    /// </summary>
    public T? Evict(ClientIdentity? adding, Func<T, bool>? spare = null, bool beyondShare = false)
    {
        int present = _clients.Count + (adding is not null && !_clients.ContainsKey(adding) ? 1 : 0);
        ClientIdentity? owner = null;
        LinkedList<Entry>? from = null;
        LinkedListNode<Entry>? victim = null;
        int most = 0;
        foreach (var (client, entries) in _clients)
        {
            int load = entries.Count + (client.Equals(adding) ? 1 : 0);
            if (beyondShare && load * present <= Capacity) continue;
            var oldest = entries.Last;
            while (oldest is not null && spare is not null && !spare(oldest.Value.Item)) oldest = oldest.Previous;
            if (oldest is null || victim is not null && !GoesBefore(load, oldest.Value.Used, most, victim.Value.Used)) continue;
            (owner, from, victim, most) = (client, entries, oldest, load);
        }
        if (victim is null) return null;
        Unlink(owner!, from!, victim);
        return victim.Value.Item;
    }

    /// <summary>Every entry, in the order <see cref="Evict"/> would let them go one by one were each spare, for room
    /// <paramref name="adding"/> is to take.</summary>
    public List<T> InEvictionOrder(ClientIdentity? adding)
    {
        var next = _clients.Select(pair => (Load: pair.Value.Count + (pair.Key.Equals(adding) ? 1 : 0), Oldest: pair.Value.Last)).ToArray();
        var order = new List<T>(Count);
        while (order.Count < Count)
        {
            int at = -1;
            for (int i = 0; i < next.Length; i++)
                if (next[i].Oldest is { } oldest
                    && (at < 0 || GoesBefore(next[i].Load, oldest.Value.Used, next[at].Load, next[at].Oldest!.Value.Used)))
                    at = i;
            var chosen = next[at].Oldest!;
            order.Add(chosen.Value.Item);
            next[at] = (next[at].Load - 1, chosen.Previous);
        }
        return order;
    }

    /// <summary>Lets go of every entry.</summary>
    public void Clear()
    {
        _clients.Clear();
        Count = 0;
    }

    /// <summary>Whether the entry used at <paramref name="used"/>, of a client holding <paramref name="load"/>, goes before the one used at
    /// <paramref name="otherUsed"/>, of a client holding <paramref name="otherLoad"/>.</summary>
    private static bool GoesBefore(int load, long used, int otherLoad, long otherUsed)
        => load > otherLoad || load == otherLoad && used < otherUsed;

    private void Unlink(ClientIdentity client, LinkedList<Entry> entries, LinkedListNode<Entry> node)
    {
        entries.Remove(node);
        Count--;
        if (entries.Count == 0) _clients.Remove(client);
    }
}
