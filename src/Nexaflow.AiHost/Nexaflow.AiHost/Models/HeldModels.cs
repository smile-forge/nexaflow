using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Provisioning;
using Nexaflow.AiHost.Store;

namespace Nexaflow.AiHost.Models;

/// <summary>
/// The parts held loaded (<see cref="HoldModels"/>), and where every part a request names runs. The host plans the held parts together
/// (<see cref="ModelPlacement"/>) — every hold's, each at the highest priority it is held at — and loads each where the plan places it,
/// keeping it loaded while any hold holds it. A hold that comes may move the others' parts — a shorter context, another GPU — and each
/// hold whose parts moved is told where they now run; one that goes leaves the rest where they are, so nothing loads again for it. A
/// held part that makes way for a picture is told of to every hold that holds it, as it goes and as it comes back.
/// <para>
/// A part a request names that no hold holds runs where the plan would place it beside the held ones: a picture's, a clip's or a tool's
/// on demand beside them, a language or speech model's as though it were held. A build the placement needs that is not in the store —
/// a GPU part's CPU build, for a GPU without the room — is fetched first, for a model whose files are here; a model that is not here is
/// not found.
/// </para>
/// </summary>
internal sealed class HeldModels : IDisposable
{
    private readonly ModelRegistry _registry;
    private readonly IModelStore _store;
    private readonly string _runtime;
    private readonly Func<IReadOnlyList<HostGpu>> _gpus;
    private readonly HostMemory _memory;
    private readonly HostLog _log;

    /// <param name="runtime">What this host runs on — <see cref="HostCatalog.Cuda"/> or <see cref="HostCatalog.Cpu"/> — and so which of a
    /// part's builds the catalog gives it.</param>
    /// <param name="gpus">The GPUs as they stand now, each with its budget.</param>
    /// <param name="memory">The share of this machine's memory models on the CPU may hold.</param>
    public HeldModels(ModelRegistry registry, IModelStore store, string runtime, Func<IReadOnlyList<HostGpu>> gpus, HostMemory memory, HostLog log)
    {
        _registry = registry;
        _store    = store;
        _runtime  = runtime;
        _gpus     = gpus;
        _memory   = memory;
        _log      = log;
        // Every hold holding the parts that made way for a picture is told, and again as each comes back.
        registry.PartsAway += ids => Tell(ids, (request, parts) => new ModelsAway(request, parts));
        registry.PartBack  += id => Tell([id], (request, parts) => new ModelsBack(request, parts));
    }
    /// <summary>The stage a request reports while a build its placement needs is fetched.</summary>
    public const string Fetching = "fetching";

    /// <summary>A part as a hold names it: its catalog entry, its build here, and what placing it takes.</summary>
    private sealed record HeldPart(ModelRef Ref, HostedEntry Entry, HostedPart Part, PlacementNeed Need);

    /// <summary>One hold: its parts, its priority, and what it is told — in the order it is told.</summary>
    private sealed class Hold(IReadOnlyList<HeldPart> parts, RequestPriority priority, Func<HostReply, Task> reply, long requestId)
    {
        private readonly Lock _gate = new();
        private Task _told = Task.CompletedTask;

        public IReadOnlyList<HeldPart> Parts { get; } = parts;
        public RequestPriority Priority { get; } = priority;
        public long RequestId { get; } = requestId;

        public bool Holds(string id) => Parts.Any(p => p.Ref.Id == id);

        /// <summary>Tells the hold's app <paramref name="message"/> after whatever it was told before; an app that has gone hears nothing.</summary>
        public Task TellAsync(HostReply message)
        {
            lock (_gate)
                return _told = _told.ContinueWith(async _ =>
                {
                    try { await reply(message); }
                    catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
                }, TaskScheduler.Default).Unwrap();
        }
    }

    private readonly Lock _gate = new();
    /// <summary>One plan at a time: a hold coming or going, and the loads it starts.</summary>
    private readonly SemaphoreSlim _planning = new(1, 1);
    private readonly List<Hold> _holds = [];
    private Placement _plan = new([], [], []);
    /// <summary>Each held part loaded where the plan placed it, as it is held loaded.</summary>
    private readonly Dictionary<string, ModelSpec> _loaded = new(StringComparer.Ordinal);
    /// <summary>Why a held part placed somewhere did not load there.</summary>
    private readonly Dictionary<string, string> _failed = new(StringComparer.Ordinal);

    /// <summary>Holds <paramref name="request"/>'s parts, loaded where the plan places them, and tells it where they run
    /// (<see cref="ModelsPlaced"/>) — until the returned value is disposed.</summary>
    /// <exception cref="HostRequestException">A part the catalog does not name, or a model whose files are not in the store.</exception>
    /// <summary>Whether any client holds a model now.</summary>
    public bool AnyHeld
    {
        get
        {
            lock (_gate) return _holds.Count > 0;
        }
    }

    public async Task<IAsyncDisposable> HoldAsync(HoldModels request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        if (request.Models.Count == 0) throw new HostRequestException(HostErrorKind.Invalid, "A hold names at least one part.");
        var parts = request.Models.Select(PartOf).DistinctBy(p => p.Ref.Id).ToList();
        foreach (var entry in parts.Select(p => p.Entry).Distinct()) EnsureHere(entry);

        var hold = new Hold(parts, ClientIdentity.Current.Granted(request.Priority), reply, request.RequestId);
        Task Stage(string stage) => reply(new Progress(request.RequestId, stage, 0, 0));
        await _planning.WaitAsync(ct);
        try
        {
            lock (_gate) _holds.Add(hold);
            await PlanAsync(hold, Stage, ct);
        }
        catch
        {
            LetGo(hold);
            throw;
        }
        finally { _planning.Release(); }
        return new Holding(this, hold);
    }

    private sealed class Holding(HeldModels owner, Hold hold) : IAsyncDisposable
    {
        private int _ended;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _ended, 1) != 0) return;
            await owner._planning.WaitAsync();
            try { owner.LetGo(hold); }
            finally { owner._planning.Release(); }
        }
    }

    /// <summary>Plans every held part again, loads each whose placement changed where it is now placed, and tells each hold whose
    /// parts moved — <paramref name="opened"/>, which is new, whatever moved.</summary>
    private async Task PlanAsync(Hold opened, Func<string, Task> stage, CancellationToken ct)
    {
        IReadOnlyList<(HeldPart Part, RequestPriority Priority)> held;
        lock (_gate) held = Held();
        var plan = ModelPlacement.Plan(_gpus(), [.. held.Select(h => h.Part.Need)], _memory.BudgetBytes,
                                       held.ToDictionary(h => h.Part.Ref.Id, h => h.Priority, StringComparer.Ordinal));
        var moved = new HashSet<string>(StringComparer.Ordinal);
        lock (_gate)
        {
            _plan = plan;
            foreach (var id in _loaded.Keys.Where(id => plan.Of(id) is null).ToList())
            {
                _loaded.Remove(id);
                _registry.Release(id);
                moved.Add(id);
            }
            foreach (var id in plan.Unplaced) moved.Add(id);
        }

        foreach (var (part, _) in held)
        {
            if (plan.Of(part.Ref.Id) is not { } spec) continue;
            lock (_gate)
                if (_loaded.TryGetValue(part.Ref.Id, out var was) && was.PlacedAs(spec)) continue;
            moved.Add(part.Ref.Id);
            // The opening hold's own parts load as it waits; another's moved parts load whatever becomes of the opening one.
            bool own = opened.Holds(part.Ref.Id);
            await LoadAsync(part, spec, own ? stage : _ => Task.CompletedTask, own ? ct : CancellationToken.None);
        }

        List<Hold> told;
        lock (_gate) told = [.. _holds.Where(h => h == opened || h.Parts.Any(p => moved.Contains(p.Ref.Id)))];
        foreach (var hold in told)
        {
            var placed = Placed(hold);
            if (hold == opened) await hold.TellAsync(placed);
            else _ = hold.TellAsync(placed);
        }
    }

    /// <summary>Loads a held part where it is placed, its build fetched first where it is not here; one that will not load there is
    /// held nowhere, saying why.</summary>
    private async Task LoadAsync(HeldPart part, ModelSpec spec, Func<string, Task> stage, CancellationToken ct)
    {
        try
        {
            await EnsureBuildAsync(part, spec, stage, ct);
            using (await _registry.AcquireAsync(spec, resident: true, stage, ct)) { }
            lock (_gate)
            {
                _loaded[part.Ref.Id] = spec;
                _failed.Remove(part.Ref.Id);
            }
        }
        catch (HostRequestException ex)
        {
            _log.Write($"{part.Ref} is held nowhere: {ex.Message}");
            lock (_gate)
            {
                _loaded.Remove(part.Ref.Id);
                _failed[part.Ref.Id] = ex.Message;
            }
        }
    }

    /// <summary>Ends a hold: the parts no other hold holds go once nothing is using them, and the rest stay where they are.</summary>
    private void LetGo(Hold hold)
    {
        lock (_gate)
        {
            if (!_holds.Remove(hold)) return;
            var still = Held().Select(h => h.Part.Ref.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var id in hold.Parts.Select(p => p.Ref.Id).Where(id => !still.Contains(id)))
            {
                _loaded.Remove(id);
                _failed.Remove(id);
                _registry.Release(id);
            }
            _plan = _plan with { Placed = [.. _plan.Placed.Where(s => still.Contains(s.Id))], Unplaced = [.. _plan.Unplaced.Where(still.Contains)] };
        }
    }

    /// <summary>Every part held, once, at the highest priority a hold holds it at.</summary>
    private List<(HeldPart Part, RequestPriority Priority)> Held()
        => [.. _holds.SelectMany(h => h.Parts.Select(p => (Part: p, h.Priority)))
                     .GroupBy(h => h.Part.Ref.Id, StringComparer.Ordinal)
                     .Select(g => (g.First().Part, g.Min(h => h.Priority)))];

    /// <summary>Where <paramref name="hold"/>'s parts run now, and those held nowhere.</summary>
    private ModelsPlaced Placed(Hold hold)
    {
        lock (_gate)
            return new ModelsPlaced(hold.RequestId,
                [.. hold.Parts.Where(p => _loaded.ContainsKey(p.Ref.Id)).Select(p => PlacementOf(p.Ref, _loaded[p.Ref.Id]))],
                [.. hold.Parts.Where(p => !_loaded.ContainsKey(p.Ref.Id)).Select(p => new PartUnplaced(p.Ref, WhyUnplaced(p)))]);
    }

    private string WhyUnplaced(HeldPart part)
        => _failed.GetValueOrDefault(part.Ref.Id)
           ?? $"{part.Entry.DisplayName}'s {part.Part.Role} has no room on this machine's GPUs beside the other held models, and no build for its CPU.";

    private static PartPlacement PlacementOf(ModelRef part, ModelSpec spec)
        => new(part, spec.Device, spec.MemoryBytes, spec.HostBytes, spec.ContextTokens, spec.Sequences, spec.BackgroundSequences,
               spec.BackgroundTokens, spec.GpuLayers, spec.ExpertsInHost);

    /// <summary>Where every held part runs, whoever holds it, and those held nowhere — as the host describes itself.</summary>
    public (IReadOnlyList<PartPlacement> Held, IReadOnlyList<PartUnplaced> Unplaced) Describe()
    {
        lock (_gate)
        {
            var held = Held();
            return ([.. held.Where(h => _loaded.ContainsKey(h.Part.Ref.Id)).Select(h => PlacementOf(h.Part.Ref, _loaded[h.Part.Ref.Id]))],
                    [.. held.Where(h => !_loaded.ContainsKey(h.Part.Ref.Id)).Select(h => new PartUnplaced(h.Part.Ref, WhyUnplaced(h.Part)))]);
        }
    }

    /// <summary>
    /// How <paramref name="part"/> runs for a request: as it is held, where a hold holds it; otherwise where the plan would place it
    /// beside the held parts — on demand for a picture's, a clip's or a tool's part, as though held for a language or speech model's —
    /// its build fetched first where it is not here.
    /// </summary>
    /// <exception cref="HostRequestException">A part the catalog does not name; a model whose files are not in the store; a part no
    /// GPU has room for, with no CPU build.</exception>
    public async Task<ModelSpec> ResolveAsync(ModelRef part, Func<string, Task> stage, CancellationToken ct)
    {
        var named = PartOf(part);
        EnsureHere(named.Entry);
        ModelSpec spec;
        lock (_gate)
        {
            if (_loaded.TryGetValue(part.Id, out var held)) return held;
            spec = named.Entry.LoadsOnDemand || named.Part.LoadsOnDemand
                ? ModelPlacement.PlaceOnDemand(_plan with { Gpus = _gpus() }, named.Need)   // the GPUs as they are now, held parts or none
                  ?? throw new HostRequestException(HostErrorKind.Refused,
                         $"{named.Entry.DisplayName} needs {Gib(PlacementNeeds.Lighter(named.Entry, named.Part).Select(b => b.MemoryBytes).Append(named.Part.MemoryBytes).Min())} "
                         + $"of GPU memory for its {named.Part.Role}, more than any GPU here has for models.")
                : AsHeld(named) ?? throw new HostRequestException(HostErrorKind.Refused, WhyUnplaced(named));
        }
        await EnsureBuildAsync(named, spec, stage, ct);
        return spec;
    }

    /// <summary>Where <paramref name="part"/> would be placed were it held beside the held parts — at the priority a request for
    /// it is answered at.</summary>
    private ModelSpec? AsHeld(HeldPart part)
    {
        var held = Held();
        return ModelPlacement.Plan(_gpus(), [.. held.Select(h => h.Part.Need), part.Need], _memory.BudgetBytes,
                                   held.ToDictionary(h => h.Part.Ref.Id, h => h.Priority, StringComparer.Ordinal)).Of(part.Ref.Id);
    }

    /// <summary>The part, loaded where it runs for a request (<see cref="ResolveAsync"/>), held for it while the lease is.</summary>
    public async Task<ModelRegistry.Lease> AcquireAsync(ModelRef part, Func<string, Task> stage, CancellationToken ct, ModelRegistry.Room? room = null)
        => await _registry.AcquireAsync(await ResolveAsync(part, stage, ct), resident: false, stage, ct, room);

    /// <summary>Room for one picture (<see cref="ModelRegistry.BorrowRoom"/>).</summary>
    public ModelRegistry.Room BorrowRoom() => _registry.BorrowRoom();

    /// <summary>The part of the tool the catalog calls <paramref name="tool"/> that does <paramref name="role"/> here.</summary>
    public ModelRef ToolPart(string tool, string role) => RefOf(ToolOf(tool), role);

    /// <summary>Each of <paramref name="roles"/> of <paramref name="entry"/> as a request runs it, by role.</summary>
    public async Task<IReadOnlyDictionary<string, ModelSpec>> PartsAsync(HostedEntry entry, IEnumerable<string> roles, Func<string, Task> stage,
                                                                          CancellationToken ct)
    {
        var parts = new Dictionary<string, ModelSpec>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            var part = entry.PartsFor(_runtime).FirstOrDefault(p => p.Role == role)
                       ?? throw new HostRequestException(HostErrorKind.NotFound, $"{entry.DisplayName} has no {role} this host runs.");
            parts[role] = await ResolveAsync(new ModelRef(entry.Id, part.Name), stage, ct);
        }
        return parts;
    }

    /// <summary>The model the catalog calls <paramref name="id"/>.</summary>
    /// <exception cref="HostRequestException">It names none.</exception>
    public HostedModel ModelOf(string id)
        => HostCatalog.Find(id) as HostedModel ?? throw new HostRequestException(HostErrorKind.NotFound, $"'{id}' is not a model this host's catalog names.");

    /// <summary>The tool the catalog calls <paramref name="id"/>.</summary>
    /// <exception cref="HostRequestException">It names none.</exception>
    public HostedTool ToolOf(string id)
        => HostCatalog.Find(id) as HostedTool ?? throw new HostRequestException(HostErrorKind.NotFound, $"'{id}' is not a tool this host's catalog names.");

    /// <summary>The part of <paramref name="entry"/> that does <paramref name="role"/> here, as a request names it.</summary>
    public ModelRef RefOf(HostedEntry entry, string role)
        => entry.PartsFor(_runtime).FirstOrDefault(p => p.Role == role) is { } part ? new ModelRef(entry.Id, part.Name)
           : throw new HostRequestException(HostErrorKind.NotFound, $"{entry.DisplayName} has no {role} this host runs.");

    /// <summary>Whether <paramref name="entry"/> has a part that does <paramref name="role"/> here.</summary>
    public bool Has(HostedEntry entry, string role) => entry.PartsFor(_runtime).Any(p => p.Role == role);

    /// <summary>Where <paramref name="relative"/>, a file of the store, lies.</summary>
    public string PathOf(string relative) => _store.PathOf(relative);

    /// <summary>The files of <paramref name="lora"/>, which a picture asks for: where they lie in the store.</summary>
    /// <exception cref="HostRequestException">They are not there.</exception>
    public (string Adapter, string? Sampler) FilesOf(HostedLora lora)
        => _store.Holds(lora.Files) ? (_store.PathOf(lora.Adapter.RelativePath), lora.Sampler is { } sampler ? _store.PathOf(sampler.RelativePath) : null)
           : throw new HostRequestException(HostErrorKind.NotFound, $"The {lora.DisplayName} LoRA is not in the model store.");

    private HeldPart PartOf(ModelRef part)
    {
        var entry = HostCatalog.Find(part.Model)
                    ?? throw new HostRequestException(HostErrorKind.NotFound, $"'{part.Model}' is not a model this host's catalog names.");
        var built = entry.PartsFor(_runtime).FirstOrDefault(p => p.Name == part.Part)
                    ?? throw new HostRequestException(HostErrorKind.NotFound, $"{entry.DisplayName} has no part '{part.Part}' this host runs.");
        return new HeldPart(part, entry, built, PlacementNeeds.Of(entry, built));
    }

    /// <summary>The files of <paramref name="entry"/> this host runs are in the store.</summary>
    /// <exception cref="HostRequestException">They are not.</exception>
    private void EnsureHere(HostedEntry entry)
    {
        if (!_store.Holds(entry.FilesFor(_runtime)))
            throw new HostRequestException(HostErrorKind.NotFound, $"{entry.DisplayName} is not in the model store.");
    }

    /// <summary>Fetches the build <paramref name="spec"/> runs — another build of a part whose model is here — where it is not in the
    /// store.</summary>
    private async Task EnsureBuildAsync(HeldPart part, ModelSpec spec, Func<string, Task> stage, CancellationToken ct)
    {
        var build = part.Entry.Parts.First(p => p.Name == part.Ref.Part && p.Folder == spec.Folder);
        var files = PlacementNeeds.FilesOf(part.Entry, build);
        if (_store.Holds(files)) return;
        await stage(Fetching);
        try
        {
            await _store.FetchAsync([.. files.Select(f => f.RelativePath)], null, ct);
        }
        catch (Exception ex) when (ex is ModelRoomException or ModelDownloadsOffException or HttpRequestException or InvalidDataException or IOException)
        {
            throw new HostRequestException(HostErrorKind.Refused, $"{part.Entry.DisplayName}'s {build.Role} for this machine could not be fetched: {ex.Message}");
        }
    }


    private void Tell(IReadOnlyList<string> ids, Func<long, IReadOnlyList<ModelRef>, HostReply> message)
    {
        List<Hold> holds;
        lock (_gate) holds = [.. _holds];
        foreach (var hold in holds)
        {
            var parts = hold.Parts.Where(p => ids.Contains(p.Ref.Id)).Select(p => p.Ref).ToList();
            if (parts.Count > 0) _ = hold.TellAsync(message(hold.RequestId, parts));
        }
    }

    private static string Gib(long bytes) => $"{bytes / (double)(1L << 30):0.#} GB";

    public void Dispose() => _planning.Dispose();
}
