using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Client;

/// <summary>
/// The parts an app has the AI host hold loaded (<see cref="HoldModels"/>). The host decides where each runs, planning every app's holds
/// together, and says so as it changes: a hold here is that word as it last came — where each part runs, which are held nowhere and why,
/// which have made way for a picture. An app holds a model while it expects to use it — a conversation, a voice — and lets it go after;
/// what nobody holds the host unloads once nothing is using it.
/// </summary>
public sealed class HostHolds(IAiHost host)
{
    /// <summary>How long describing the host may go unanswered — starting it takes seconds.</summary>
    private static readonly TimeSpan DescribeLimit = TimeSpan.FromMinutes(1);

    private int _open;

    /// <summary>Whether any hold is open: a host holding models is not one standing idle.</summary>
    public bool HoldsModels => Volatile.Read(ref _open) > 0;

    /// <summary>Has the host hold <paramref name="parts"/> at <paramref name="priority"/>, starting it where none runs; the hold is
    /// placed — its parts loaded where the host placed them, or held nowhere with why — when this returns.</summary>
    /// <param name="loading">Told what the host is doing meanwhile — fetching a build the plan needs, loading.</param>
    /// <exception cref="AiHostException">The host refused the hold — a part its catalog does not name, a model not in its store — or
    /// could not be reached.</exception>
    public async Task<HostHold> HoldAsync(IReadOnlyList<ModelRef> parts, RequestPriority priority, IProgress<string>? loading = null,
                                          CancellationToken ct = default)
    {
        // A hold is quiet for as long as nothing moves its parts: the host's heartbeat is what says it is alive.
        var stream = await host.OpenStreamAsync(id => new HoldModels(id, parts, priority), Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
        Interlocked.Increment(ref _open);
        try
        {
            return await HostHold.StartAsync(stream, parts, () => Interlocked.Decrement(ref _open), loading, ct).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _open);
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>What the host is, the GPUs it found and where every held part runs, starting it where none runs.</summary>
    /// <exception cref="AiHostException">It could not be reached.</exception>
    public async Task<HostDescription> DescribeAsync(CancellationToken ct = default)
    {
        await foreach (var reply in host.SendAsync(id => new DescribeHost(id), DescribeLimit, ct).ConfigureAwait(false))
            if (reply is HostDescription description) return description;
        throw new AiHostException(AiHostFailure.Request, "The host did not describe itself.", HostErrorKind.Invalid);
    }
}

/// <summary>
/// One hold of parts in the AI host, open until it is disposed: where each part runs as the host last said, those held nowhere and why,
/// and those that have made way for a picture — until it is loaded again. A hold the host ends — gone, or failed — is
/// <see cref="Ended"/>; its parts are no longer held.
/// </summary>
public sealed class HostHold : IAsyncDisposable
{
    private readonly IHostStream _stream;
    private readonly Action _closed;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private Dictionary<string, PartPlacement> _placed = new(StringComparer.Ordinal);
    private Dictionary<string, string> _unplaced = new(StringComparer.Ordinal);
    private readonly HashSet<string> _away = new(StringComparer.Ordinal);
    private Task _reading = Task.CompletedTask;
    private int _ended;

    private HostHold(IHostStream stream, IReadOnlyList<ModelRef> parts, Action closed)
    {
        _stream = stream;
        _closed = closed;
        Parts   = parts;
    }

    /// <summary>The parts it holds.</summary>
    public IReadOnlyList<ModelRef> Parts { get; }

    /// <summary>Whether the host has ended it — gone, or failed; nothing it names is held any longer.</summary>
    public bool Ended => Volatile.Read(ref _ended) != 0;

    /// <summary>Raised, on whichever thread it happened, when the host says where its parts run now, a part makes way for a picture or
    /// is back after one, or the hold ends.</summary>
    public event EventHandler? Changed;

    /// <summary>Where <paramref name="part"/> runs; null where it is held nowhere, or the hold has ended.</summary>
    public PartPlacement? PlacementOf(ModelRef part)
    {
        lock (_gate) return Ended ? null : _placed.GetValueOrDefault(part.Id);
    }

    /// <summary>Why <paramref name="part"/> is held nowhere; null where it is placed.</summary>
    public string? WhyUnplaced(ModelRef part)
    {
        lock (_gate) return _unplaced.GetValueOrDefault(part.Id);
    }

    /// <summary>Whether <paramref name="part"/> has made way on its GPU for a picture: until it is back, a request for it waits.</summary>
    public bool IsAway(ModelRef part)
    {
        lock (_gate) return _away.Contains(part.Id);
    }

    /// <summary>Whether any part it holds has made way for a picture.</summary>
    public bool AnyAway
    {
        get { lock (_gate) return _away.Count > 0; }
    }

    internal static async Task<HostHold> StartAsync(IHostStream stream, IReadOnlyList<ModelRef> parts, Action closed, IProgress<string>? loading,
                                                    CancellationToken ct)
    {
        var hold = new HostHold(stream, parts, closed);
        var replies = stream.ReadAsync(hold._stop.Token).GetAsyncEnumerator(hold._stop.Token);
        try
        {
            // Placed first: until then the host is fetching and loading, and the caller may give up on it.
            using var giveUp = ct.Register(hold._stop.Cancel);
            while (true)
            {
                if (!await replies.MoveNextAsync().ConfigureAwait(false))
                    throw new AiHostException(AiHostFailure.Request, "The host ended the hold before placing it.", HostErrorKind.Runtime);
                if (replies.Current is Progress { Stage: var stage }) loading?.Report(stage);
                if (replies.Current is ModelsPlaced placed)
                {
                    hold.Take(placed);
                    break;
                }
            }
        }
        catch
        {
            await replies.DisposeAsync().ConfigureAwait(false);
            hold.End();
            throw;
        }
        hold._reading = hold.ReadAsync(replies);
        return hold;
    }

    /// <summary>Takes in what the host says of the hold until it ends.</summary>
    private async Task ReadAsync(IAsyncEnumerator<HostReply> replies)
    {
        try
        {
            while (await replies.MoveNextAsync().ConfigureAwait(false))
            {
                switch (replies.Current)
                {
                    case ModelsPlaced placed:
                        Take(placed);
                        break;
                    case ModelsAway away:
                        lock (_gate) _away.UnionWith(away.Parts.Select(p => p.Id));
                        break;
                    case ModelsBack back:
                        lock (_gate) _away.ExceptWith(back.Parts.Select(p => p.Id));
                        break;
                    default:
                        continue;
                }
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) when (ex is AiHostException or OperationCanceledException) { }
        finally
        {
            await replies.DisposeAsync().ConfigureAwait(false);
            End();
        }
    }

    private void Take(ModelsPlaced placed)
    {
        lock (_gate)
        {
            _placed   = placed.Placed.ToDictionary(p => p.Part.Id, StringComparer.Ordinal);
            _unplaced = placed.Unplaced.ToDictionary(p => p.Part.Id, p => p.Reason, StringComparer.Ordinal);
        }
    }

    private void End()
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0) return;
        lock (_gate) _away.Clear();
        _closed();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Lets the parts go: the host unloads those no other hold holds once nothing is using them.</summary>
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await _stream.DisposeAsync().ConfigureAwait(false);
        await _reading.ConfigureAwait(false);
        End();
        _stop.Dispose();
    }
}
