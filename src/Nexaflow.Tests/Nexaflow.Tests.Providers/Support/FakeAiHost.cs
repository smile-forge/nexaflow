using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.Tests.Providers.Support;

/// <summary>
/// The AI host as a provider sees it, in memory. Each request is recorded and answered by <see cref="Reply"/>; a
/// stream then answers each input sent into it with <see cref="Answer"/>, and ends when the app sends
/// <see cref="InputEnd"/>. A hold (<see cref="HoldModels"/>) is kept as the real host keeps it: every open hold's parts planned
/// together from the catalog (<see cref="ModelPlacement"/>) on <see cref="Gpus"/>, each hold told where its parts run — again when a
/// later hold moves them — until it is disposed; <see cref="MakeWay"/> and <see cref="ComeBack"/> tell holds their parts made way
/// for a picture. Asked to describe itself, it names <see cref="Gpus"/> when it runs from a runtime pack and none otherwise, and
/// where every held part runs, as the real host does; that is not recorded.
/// </summary>
internal sealed class FakeAiHost : IAiHost
{
    public readonly List<HostRequest> Requests = [];
    public readonly List<RequestInput> Inputs = [];

    /// <summary>The replies to a request, given how many of its kind came before it.</summary>
    public Func<HostRequest, int, IEnumerable<HostReply>> Reply = (r, _) => r is Generate g
        ? [new TextDelta(g.RequestId, "Hello"), new TextDelta(g.RequestId, " there."), new GenerationEnded(g.RequestId, StopReason.EndOfTurn, 10, 10, 3)]
        : [];

    /// <summary>What a stream replies to each input sent into it.</summary>
    public Func<RequestInput, IEnumerable<HostReply>> Answer = _ => [];

    public AiHostEnvironment Environment { get; set; } = AiHostEnvironment.Cpu;

    /// <summary>Why its models cannot run: reachable until a test says otherwise, which tells whoever listens.</summary>
    public string? Unreachable
    {
        get => _unreachable;
        set
        {
            _unreachable = value;
            ReachabilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private string? _unreachable;

    public event EventHandler? ReachabilityChanged;

    /// <summary>Whether its host is running: so until a test says it has gone.</summary>
    public bool IsRunning { get; set; } = true;

    /// <summary>The share of memory its host's models may hold: none unless a test sets one.</summary>
    public HostMemory Memory { get; set; } = HostMemory.Unbounded;

    /// <summary>The GPUs it finds when started from a pack: one 24 GB card, unless a test gives others.</summary>
    public IReadOnlyList<HostGpu> Gpus { get; set; } = [new HostGpu(0, "NVIDIA GeForce RTX 4090", "0000:01:00.0", 24L << 30, 22L << 30, 23L << 30)];

    /// <summary>Like the real client: a <see cref="Failed"/> reply arrives as an <see cref="AiHostException"/>, and
    /// <see cref="Completed"/> ends the replies.</summary>
    public async IAsyncEnumerable<HostReply> SendAsync(Func<long, HostRequest> create, TimeSpan stallLimit,
                                                       [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (create(0) is DescribeHost)
        {
            await Task.Yield();
            HostPlacement held;
            lock (_holds) held = Described();
            yield return new HostDescription(0, "1.30.0", ["CPUExecutionProvider"], Environment.Cuda ? CudaRuntimePack.Id : null, held.Gpus,
                                             Held: held.Held, Unplaced: held.Unplaced);
            yield break;
        }
        var request = Record(create);
        foreach (var reply in Reply(request, Attempt(request)))
        {
            await Task.Yield();
            if (reply is Failed failed) throw new AiHostException(AiHostFailure.Request, failed.Message, failed.Kind);
            if (reply is Completed) yield break;
            yield return reply;
        }
    }

    public Task<IHostStream> OpenStreamAsync(Func<long, HostRequest> create, TimeSpan stallLimit, CancellationToken ct = default)
    {
        var request = Record(create);
        var stream  = new Stream(this, request.RequestId);
        if (request is HoldModels hold) Hold(stream, hold);
        else foreach (var reply in Reply(request, Attempt(request))) stream.Push(reply);
        return Task.FromResult<IHostStream>(stream);
    }

    private readonly List<(Stream Stream, HoldModels Hold)> _holds = [];
    private Placement _plan = new([], [], []);

    /// <summary>The holds open now.</summary>
    public IReadOnlyList<HoldModels> Holds
    {
        get { lock (_holds) return [.. _holds.Select(h => h.Hold)]; }
    }

    /// <summary>What the host runs on: the CUDA pack's build of each part where it runs from the pack, the CPU's otherwise.</summary>
    private string Runtime => Environment.Cuda ? HostCatalog.Cuda : HostCatalog.Cpu;

    private void Hold(Stream stream, HoldModels hold)
    {
        if (hold.Models.FirstOrDefault(m => NeedOf(m) is null) is { } unknown)
        {
            stream.Push(new Failed(hold.RequestId, HostErrorKind.NotFound, $"'{unknown}' is not a part this host's catalog names."));
            return;
        }
        lock (_holds)
        {
            _holds.Add((stream, hold));
            var before = _plan;
            var held = _holds.SelectMany(h => h.Hold.Models.Select(m => (Part: m, h.Hold.Priority)))
                             .GroupBy(h => h.Part.Id, StringComparer.Ordinal)
                             .Select(g => (g.First().Part, Priority: g.Min(h => h.Priority)))
                             .ToList();
            _plan = ModelPlacement.Plan(Environment.Cuda ? Gpus : [], [.. held.Select(h => NeedOf(h.Part)!)], Memory.BudgetBytes,
                                        held.ToDictionary(h => h.Part.Id, h => h.Priority, StringComparer.Ordinal));
            foreach (var (open, theirs) in _holds)
                if (open == stream || theirs.Models.Any(m => !Same(before.Of(m.Id), _plan.Of(m.Id))))
                    open.Push(Placed(theirs));
        }

        static bool Same(ModelSpec? was, ModelSpec? now) => was is null ? now is null : now is not null && was.PlacedAs(now);
    }

    private PlacementNeed? NeedOf(ModelRef part)
        => HostCatalog.Find(part.Model) is { } entry && entry.PartsFor(Runtime).FirstOrDefault(p => p.Name == part.Part) is { } built
            ? PlacementNeeds.Of(entry, built)
            : null;

    private ModelsPlaced Placed(HoldModels hold)
        => new(hold.RequestId,
               [.. hold.Models.Where(m => _plan.Of(m.Id) is not null).Select(m => PlacementOf(m, _plan.Of(m.Id)!))],
               [.. hold.Models.Where(m => _plan.Of(m.Id) is null).Select(m => new PartUnplaced(m, "no room on this machine's GPUs, and no CPU build"))]);

    private static PartPlacement PlacementOf(ModelRef part, ModelSpec spec)
        => new(part, spec.Device, spec.MemoryBytes, spec.HostBytes, spec.ContextTokens, spec.Sequences, spec.BackgroundSequences,
               spec.BackgroundTokens, spec.GpuLayers, spec.ExpertsInHost);

    /// <summary>Where every held part runs, as the host describes itself.</summary>
    private HostPlacement Described()
    {
        var held = _holds.SelectMany(h => h.Hold.Models).DistinctBy(m => m.Id).ToList();
        return new HostPlacement(Environment.Cuda ? Gpus : [],
                                 [.. held.Where(m => _plan.Of(m.Id) is not null).Select(m => PlacementOf(m, _plan.Of(m.Id)!))],
                                 [.. held.Where(m => _plan.Of(m.Id) is null).Select(m => new PartUnplaced(m, "no room"))]);
    }

    /// <summary>A hold ends: the parts no other hold holds go, and the rest stay where they are.</summary>
    private void LetGo(Stream stream)
    {
        lock (_holds)
        {
            if (_holds.RemoveAll(h => h.Stream == stream) == 0) return;
            var still = _holds.SelectMany(h => h.Hold.Models.Select(m => m.Id)).ToHashSet(StringComparer.Ordinal);
            _plan = _plan with { Placed = [.. _plan.Placed.Where(s => still.Contains(s.Id))], Unplaced = [.. _plan.Unplaced.Where(still.Contains)] };
        }
    }

    /// <summary>Tells each hold holding any of <paramref name="parts"/> that they made way for a picture.</summary>
    public void MakeWay(params ModelRef[] parts) => Tell(parts, (id, held) => new ModelsAway(id, held));

    /// <summary>Tells each hold holding any of <paramref name="parts"/> that they are loaded again after a picture.</summary>
    public void ComeBack(params ModelRef[] parts) => Tell(parts, (id, held) => new ModelsBack(id, held));

    private void Tell(IReadOnlyList<ModelRef> parts, Func<long, IReadOnlyList<ModelRef>, HostReply> message)
    {
        lock (_holds)
            foreach (var (stream, hold) in _holds)
                if (hold.Models.Where(m => parts.Any(p => p.Id == m.Id)).ToList() is { Count: > 0 } held)
                    stream.Push(message(hold.RequestId, held));
    }

    public Task<SharedBytes> AllocateAsync(int length, CancellationToken ct = default) => Task.FromResult(SharedBytes.Of(new byte[length]));

    private HostRequest Record(Func<long, HostRequest> create)
    {
        var request = create(Requests.Count + 1);
        Requests.Add(request);
        return request;
    }

    private int Attempt(HostRequest request) => Requests.Count(r => r.GetType() == request.GetType()) - 1;

    private sealed class Stream(FakeAiHost host, long id) : IHostStream
    {
        private readonly Channel<HostReply> _replies = Channel.CreateUnbounded<HostReply>();

        public long RequestId => id;

        public bool Disposed { get; private set; }

        public void Push(HostReply reply) => _replies.Writer.TryWrite(reply);

        public Task WriteAsync(RequestInput input, CancellationToken ct = default)
        {
            lock (host.Inputs) host.Inputs.Add(input);
            foreach (var reply in host.Answer(input)) Push(reply);
            if (input is InputEnd) _replies.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<HostReply> ReadAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var reply in _replies.Reader.ReadAllAsync(ct))
            {
                if (reply is Failed failed) throw new AiHostException(AiHostFailure.Request, failed.Message, failed.Kind);
                if (reply is Completed) yield break;
                yield return reply;
            }
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            host.LetGo(this);
            _replies.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    public event EventHandler<IReadOnlyList<string>>? ModelFilesChanged { add { } remove { } }
}
