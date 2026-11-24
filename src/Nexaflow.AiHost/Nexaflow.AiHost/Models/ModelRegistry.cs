using System.Diagnostics;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Models;

/// <summary>
/// The models in memory. The resident set — what is held (<see cref="HeldModels"/>) — stays; any other model a request
/// names is loaded on demand into what its GPU's budget leaves, and is the first to go when another needs the room.
/// Admission is per GPU, against its budget, from each model's estimate: a model that would not fit is refused rather
/// than over-committed, after unloading what may go. A picture borrows a <see cref="Room"/>: while one is out,
/// residents nothing is using may make way for its parts — or the picture waits until they are idle — and they are
/// loaded again once the last picture is done, or as soon as a request needs one.
/// <para>
/// A model already loaded is leased at once, behind nobody. A model not loaded is admitted in the order the requests for it
/// came, whichever client they are from: one at a time, each planning against the room as it then is, and loading. One that
/// finds only models in use in its way keeps its place in line while later ones plan, and once one of those models is let go
/// it plans again ahead of every request that came after it.
/// </para>
/// </summary>
/// <param name="memory">The share of this machine's memory models on the CPU may hold; none when unset.</param>
/// <param name="idleUnload">How long a model nothing holds stays loaded once nothing uses it (<see cref="HostPolicy.IdleUnload"/>);
/// null keeps it until another needs its room.</param>
internal sealed class ModelRegistry(IHostGpus gpus, ModelRunners runners, HostLog log, Store.IModelFiles files, HostMemory? memory = null,
                                    TimeSpan? idleUnload = null) : IDisposable
{
    /// <summary>Where the files a request names lie in the model store.</summary>
    public Store.IModelFiles Files => files;
    /// <summary>How often a request waiting for room says so, so the app's watchdog keeps hearing from it.</summary>
    private static readonly TimeSpan WaitingReport = TimeSpan.FromSeconds(20);

    /// <param name="requested">The spec as a request named it — its folder the store's name for it — which a later request is held
    /// to, and which a model that made way is loaded again from; the model itself holds the spec it was loaded from.</param>
    internal sealed class Entry(IHostModel model, ModelSpec requested)
    {
        public IHostModel Model { get; } = model;
        public ModelSpec Requested { get; } = requested;
        public bool Resident { get; set; }
        /// <summary>The app let it go while a request was using it.</summary>
        public bool UnloadWhenIdle { get; set; }
        public int Users { get; set; }
        public long LastUsed { get; set; } = Stopwatch.GetTimestamp();
    }

    private readonly Dictionary<string, Entry> _loaded = new(StringComparer.Ordinal);
    /// <summary>Models asked for in another place or at another size while a request still used them: they hold
    /// their memory until it ends.</summary>
    private readonly List<Entry> _replaced = [];
    /// <summary>Residents unloaded to make way for a picture.</summary>
    private readonly Dictionary<string, ModelSpec> _displaced = new(StringComparer.Ordinal);
    /// <summary>The requests for a model not loaded, in the order they came (<see cref="AdmitEarliest"/>).</summary>
    private readonly LinkedList<PlaceInLine> _line = new();
    private readonly object _gate = new();
    /// <summary>The request planning, or loading what it planned, now: one at a time, so no two plan against the same room.</summary>
    private PlaceInLine? _admitting;
    /// <summary>How many times room may have come — a model in use let go, or one unloaded — so a request that found none
    /// knows whether to plan again.</summary>
    private long _roomChanges;
    private bool _disposed;
    private int _rooms;

    /// <summary>The model, loaded where its spec places it — and kept loaded while the lease is held.</summary>
    /// <param name="resident">Keep it loaded after the lease, until it is unloaded.</param>
    /// <param name="room">The picture it is loaded for: residents nothing is using may make way for it.</param>
    /// <exception cref="HostRequestException">It names a GPU this host does not have, would not fit beside the models
    /// that must stay, or would not load.</exception>
    /// <exception cref="ObjectDisposedException">The host is closing.</exception>
    public async Task<Lease> AcquireAsync(ModelSpec spec, bool resident, Func<string, Task> stage, CancellationToken ct, Room? room = null)
    {
        if (spec.Device is { } device) gpus.Gpu(device);
        if (TryLease(spec, ref resident) is { } held) return held;

        var place = JoinLine();
        try
        {
            while (true)
            {
                await TurnToPlanAsync(place, stage, ct);
                if (TryLease(spec, ref resident) is { } loaded) return loaded;
                if (MakeRoomFor(spec, room, out var madeWay))
                {
                    if (madeWay.Count > 0) PartsAway?.Invoke(madeWay);
                    return await LoadAsync(spec, resident, stage, ct);
                }
                KeepPlaceForRoom(place);   // only models in use stand in the way
            }
        }
        finally { LeaveLine(place); }
    }

    /// <summary>Takes a place at the back of the line, given the turn to plan at once when nobody is ahead.</summary>
    private PlaceInLine JoinLine()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var place = new PlaceInLine();
            _line.AddLast(place.Node);
            AdmitEarliest();
            return place;
        }
    }

    /// <summary>Waits for <paramref name="place"/>'s turn to plan. Waiting for room, it says so every <see cref="WaitingReport"/>
    /// so the app's watchdog keeps hearing from it; behind other requests' loads, the wait is marked on its request.</summary>
    private async Task TurnToPlanAsync(PlaceInLine place, Func<string, Task> stage, CancellationToken ct)
    {
        while (true)
        {
            Task turn;
            bool forRoom;
            lock (_gate)
            {
                turn = place.Turn.Task;
                forRoom = WaitsForRoom(place);
            }
            if (!forRoom)
            {
                await RequestActivity.WaitAsync(turn.WaitAsync(ct));
                return;
            }
            await stage("waiting");
            try
            {
                await turn.WaitAsync(WaitingReport, ct);
                return;
            }
            catch (TimeoutException) { }   // still waiting: say so again
        }
    }

    /// <summary><paramref name="place"/> found only models in use in its way: it keeps its place, and the turn passes to the
    /// next that may plan. It plans again once room may have come — at once, if some came while it planned.</summary>
    private void KeepPlaceForRoom(PlaceInLine place)
    {
        lock (_gate)
        {
            place.FoundNoRoom = true;
            place.Turn = Signal();
            _admitting = null;
            AdmitEarliest();
        }
    }

    /// <summary>Takes <paramref name="place"/> out of the line — loaded, refused, failed or given up on — passing the turn on if it
    /// had it.</summary>
    private void LeaveLine(PlaceInLine place)
    {
        lock (_gate)
        {
            if (place.Node.List is not null) _line.Remove(place.Node);
            if (_admitting != place) return;
            _admitting = null;
            AdmitEarliest();
        }
    }

    /// <summary>Whether <paramref name="place"/> waits for room: it found none, and none may have come since. Called under the
    /// gate.</summary>
    private bool WaitsForRoom(PlaceInLine place) => place.FoundNoRoom && place.PlannedAt == _roomChanges;

    /// <summary>Room may have come — a model in use was let go, or one unloaded: the requests waiting for it may plan again, in
    /// the order they came. Called under the gate.</summary>
    private void RoomMayHaveCome()
    {
        _roomChanges++;
        AdmitEarliest();
    }

    /// <summary>Gives the turn to plan to the earliest request in line not waiting for room, unless one has it now; once the
    /// registry is disposed, every request in line is told so. Called under the gate.</summary>
    private void AdmitEarliest()
    {
        if (_disposed)
        {
            foreach (var waiting in _line) waiting.Turn.TrySetException(new ObjectDisposedException(nameof(ModelRegistry)));
            return;
        }
        if (_admitting is not null) return;
        for (var node = _line.First; node is not null; node = node.Next)
        {
            var place = node.Value;
            if (WaitsForRoom(place)) continue;
            place.FoundNoRoom = false;
            place.PlannedAt = _roomChanges;
            _admitting = place;
            place.Turn.TrySetResult();
            return;
        }
    }

    /// <summary>A lease on the model as loaded, when it is loaded as <paramref name="spec"/> places it. One loaded
    /// elsewhere or at another size is let go — its residence passing to the one about to load.</summary>
    private Lease? TryLease(ModelSpec spec, ref bool resident)
    {
        lock (_gate)
        {
            if (!_loaded.TryGetValue(spec.Id, out var entry)) return null;
            if (!entry.Requested.PlacedAs(spec))
            {
                resident |= entry.Resident;
                _loaded.Remove(spec.Id);
                if (entry.Users == 0)
                {
                    Free(entry);
                    RoomMayHaveCome();
                }
                else _replaced.Add(entry);
                return null;
            }
            entry.Resident |= resident;
            if (resident) entry.UnloadWhenIdle = false;
            entry.Users++;
            entry.LastUsed = Stopwatch.GetTimestamp();
            return new Lease(this, entry);
        }
    }

    private async Task<Lease> LoadAsync(ModelSpec spec, bool resident, Func<string, Task> stage, CancellationToken ct)
    {
        await stage("loading");
        var started = Stopwatch.GetTimestamp();
        IHostModel model;
        try
        {
            var placed = Placed(spec);
            model = await Task.Run(() => runners.Load(placed), ct);
        }
        catch (Exception ex) when (ex is not HostRequestException and not OperationCanceledException)
        {
            throw new HostRequestException(HostErrorKind.Runtime, $"{spec.Id} did not load: {ex.Message}");
        }
        log.WriteActivity($"loaded {spec.Id} on {Where(spec)} in {Stopwatch.GetElapsedTime(started).TotalSeconds:0.0} s");

        bool displaced;
        Lease lease;
        lock (_gate)
        {
            // A resident that made way for a picture is resident again, whoever loads it.
            displaced = _displaced.Remove(spec.Id);
            var entry = new Entry(model, spec) { Resident = resident || displaced, Users = 1 };
            _loaded[spec.Id] = entry;
            SweepFromNow();
            lease = new Lease(this, entry);
        }
        if (displaced) PartBack?.Invoke(spec.Id);
        return lease;
    }

    /// <summary><paramref name="spec"/> as it is loaded: its folder where it lies in the model store, and its files plain names in it —
    /// a request names a model's files, never where they are.</summary>
    /// <exception cref="HostRequestException">It names a folder or a file that is not a plain path inside the store.</exception>
    private ModelSpec Placed(ModelSpec spec)
    {
        foreach (var name in new[] { spec.File, spec.Projector }.OfType<string>())
            if (!Provisioning.StorePath.IsPlain(name))
                throw new HostRequestException(HostErrorKind.Invalid, $"{spec.Id} names a file, '{name}', that is not a plain path inside its folder.");
        return spec with { Folder = files.PathOf(spec.Folder) };
    }

    /// <summary>Unloads what must go for <paramref name="spec"/> to fit: on its GPU, and in this machine's memory for what it holds
    /// there — the whole of a model on the CPU, the CPU's layers of one cut between a GPU and the CPU.</summary>
    /// <param name="madeWay">The residents that made way for the picture <paramref name="room"/> is borrowed for — they load
    /// again once it is done.</param>
    /// <returns>False when only models in use stand in the way and it may wait for them: a picture's part, or a
    /// resident that made way for one.</returns>
    /// <exception cref="HostRequestException">It cannot fit beside what must stay.</exception>
    private bool MakeRoomFor(ModelSpec spec, Room? room, out IReadOnlyList<string> madeWay)
    {
        madeWay = [];
        lock (_gate)
        {
            var entries = _loaded.Values.Concat(_replaced).ToList();
            var unload = new List<string>();

            // A GPU's budget is a wall: past it a load fails in the middle of somebody's reply.
            if (spec.Device is { } gpu)
            {
                var held = Held(entries.Where(e => e.Model.Spec.Device == gpu), e => e.Model.Spec.MemoryBytes);
                long budget = gpus.BudgetNow(gpu, held.Sum(m => m.Bytes));
                var admission = VramBudget.Plan(budget, held, spec.MemoryBytes, displaceResidents: room is not null);
                if (admission is null || admission.Waits)
                {
                    if (admission is null || room is null && !_displaced.ContainsKey(spec.Id))
                        throw new HostRequestException(HostErrorKind.Refused,
                            $"{spec.Id} needs {spec.MemoryBytes >> 20} MiB of GPU {gpu}, and the {budget >> 20} MiB the card leaves this host cannot make that room"
                            + (held.Count == 0 ? "." : " beside " + string.Join(", ", held.Select(m => m.Id)) + "."));
                    return false;
                }
                unload.AddRange(admission.Unload);
            }

            // This machine's memory is a share of it, kept by letting idle models go — never by refusing a model, or making it
            // wait, for it.
            if (InMemory(spec) is > 0 and var bytes)
            {
                long budget = (memory ?? HostMemory.Unbounded).BudgetBytes;
                var held = Held(entries.Where(e => InMemory(e.Model.Spec) > 0 && !unload.Contains(e.Model.Spec.Id)), e => InMemory(e.Model.Spec));
                var admission = VramBudget.Plan(budget, held, bytes, displaceResidents: room is not null);
                if (admission is null || admission.Waits)
                {
                    admission = new VramBudget.Admission([.. held.Where(m => !m.InUse && (!m.Resident || room is not null)).Select(m => m.Id)], Waits: false);
                    log.WriteActivity($"{spec.Id} needs {bytes >> 20} MiB of this machine's memory, past its {budget >> 20} MiB share beside the models in use; it loads past it");
                }
                unload.AddRange(admission.Unload);
            }

            var residents = new List<string>();
            foreach (var id in unload.Distinct())
            {
                var entry = _loaded[id];
                if (entry.Resident)
                {
                    _displaced[id] = entry.Requested;
                    residents.Add(id);
                }
                Unload(entry);
            }
            madeWay = residents;
            return true;
        }

        static List<VramBudget.Loaded> Held(IEnumerable<Entry> entries, Func<Entry, long> bytes)
            => [.. entries.Select(e => new VramBudget.Loaded(e.Model.Spec.Id, bytes(e), e.Resident, e.Users > 0, e.LastUsed))];
    }

    /// <summary>What <paramref name="spec"/> holds in this machine's memory, against the host's share of it.</summary>
    private static long InMemory(ModelSpec spec) => spec.Device is null ? spec.MemoryBytes : spec.HostBytes;

    /// <summary>A lease on every model loaded now — held or loaded on demand — so each can be asked something without being unloaded
    /// meanwhile; each is let go as its lease is disposed.</summary>
    public IReadOnlyList<Lease> LeaseLoaded()
    {
        lock (_gate)
        {
            var leases = new List<Lease>(_loaded.Count);
            foreach (var entry in _loaded.Values)
            {
                entry.Users++;
                leases.Add(new Lease(this, entry));
            }
            return leases;
        }
    }

    /// <summary>
    /// What GPU <paramref name="device"/> has to spare now beyond the models loaded there: its budget as the card says it now
    /// (<see cref="IHostGpus.BudgetNow"/>), less what they hold. Room a model takes for itself outside admission — a lane for work
    /// nobody is watching — is taken only within it, so it never eats what the runtime keeps back, nor what another program holds.
    /// </summary>
    public long SpareOn(int device)
    {
        lock (_gate)
        {
            long held = _loaded.Values.Concat(_replaced).Where(e => e.Model.Spec.Device == device).Sum(e => e.Model.Spec.MemoryBytes);
            return Math.Max(0, gpus.BudgetNow(device, held) - held);
        }
    }

    /// <summary>Room for one picture: while any is out, residents nothing is using may make way for its parts
    /// (<see cref="PartsAway"/>).</summary>
    public Room BorrowRoom()
    {
        lock (_gate) _rooms++;
        return new Room(this);
    }

    /// <summary>Residents made way on their GPU for a picture, by their ids: unloaded until the last picture is done, or a request
    /// needs one.</summary>
    public event Action<IReadOnlyList<string>>? PartsAway;

    /// <summary>A resident that made way for a picture is loaded again.</summary>
    public event Action<string>? PartBack;

    /// <summary>Once the last picture is done, loads the residents that made way for pictures — those no request has
    /// loaded since.</summary>
    private void GiveBack()
    {
        List<ModelSpec> displaced;
        lock (_gate)
        {
            if (--_rooms > 0) return;
            displaced = [.. _displaced.Values];
        }
        foreach (var spec in displaced) _ = ReloadAsync(spec);
    }

    private async Task ReloadAsync(ModelSpec spec)
    {
        try
        {
            using var lease = await AcquireAsync(spec, resident: true, _ => Task.CompletedTask, CancellationToken.None);
        }
        catch (Exception ex) when (ex is HostRequestException or ObjectDisposedException)
        {
            log.Write($"{spec.Id} was not loaded again after a picture: {ex.Message}");
        }
    }

    /// <summary>Looks for models gone idle a quarter of <c>idleUnload</c> apart, and no less than a second — from the first model loaded.</summary>
    private Timer? _sweeping;

    /// <summary>Starts looking for models gone idle, where the registry lets them go in time and is not yet looking. Called under the gate.</summary>
    private void SweepFromNow()
    {
        if (_sweeping is not null || idleUnload is not { } idle || _disposed) return;
        var every = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(1).Ticks, idle.Ticks / 4));
        _sweeping = new Timer(_ => UnloadIdle(idle), null, every, every);
    }
    /// <summary>Unloads every model nothing holds and nothing has used for <paramref name="idle"/>: what is left loaded after a request,
    /// with nobody holding it, goes in time rather than only when another model needs its room.</summary>
    internal void UnloadIdle(TimeSpan idle)
    {
        lock (_gate)
        {
            var gone = _loaded.Values.Where(e => !e.Resident && e.Users == 0 && Stopwatch.GetElapsedTime(e.LastUsed) >= idle).ToList();
            foreach (var entry in gone) Unload(entry);
            if (gone.Count > 0) RoomMayHaveCome();
        }
    }

    /// <summary>Takes a model out of the resident set; it is unloaded now if nothing is using it, or when the last
    /// request using it ends.</summary>
    public void Release(string id)
    {
        lock (_gate)
        {
            _displaced.Remove(id);
            if (!_loaded.TryGetValue(id, out var entry)) return;
            entry.Resident = false;
            if (entry.Users == 0)
            {
                Unload(entry);
                RoomMayHaveCome();
            }
            else entry.UnloadWhenIdle = true;
        }
    }

    private void Ended(Entry entry)
    {
        lock (_gate)
        {
            entry.Users--;
            entry.LastUsed = Stopwatch.GetTimestamp();
            if (entry.Users > 0) return;
            if (_replaced.Remove(entry)) Free(entry);
            else if (entry.UnloadWhenIdle) Unload(entry);

            RoomMayHaveCome();
        }
    }

    private void Unload(Entry entry)
    {
        _loaded.Remove(entry.Model.Spec.Id);
        Free(entry);
    }

    private void Free(Entry entry)
    {
        entry.Model.Dispose();
        log.WriteActivity($"unloaded {entry.Model.Spec.Id} from {Where(entry.Model.Spec)}");
    }

    private static string Where(ModelSpec spec) => spec.Device is { } device ? $"GPU {device}" : "the CPU";

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _sweeping?.Dispose();
            AdmitEarliest();
            foreach (var entry in _loaded.Values.ToList()) Unload(entry);
            foreach (var entry in _replaced) Free(entry);
            _replaced.Clear();
        }
    }

    /// <summary>One request's place in line for a model not loaded.</summary>
    private sealed class PlaceInLine
    {
        public PlaceInLine() => Node = new LinkedListNode<PlaceInLine>(this);

        public LinkedListNode<PlaceInLine> Node { get; }

        /// <summary>Completes when it is its turn to plan; a new one each time it goes back to waiting for room.</summary>
        public TaskCompletionSource Turn { get; set; } = Signal();

        /// <summary>It found only models in use in its way the last time it planned.</summary>
        public bool FoundNoRoom { get; set; }

        /// <summary>The times room may have come (<see cref="_roomChanges"/>) as it was last given its turn.</summary>
        public long PlannedAt { get; set; }
    }

    /// <summary>A model held for one request.</summary>
    public sealed class Lease : IDisposable
    {
        private readonly ModelRegistry _registry;
        private readonly Entry _entry;
        private int _ended;

        internal Lease(ModelRegistry registry, Entry entry)
        {
            _registry = registry;
            _entry    = entry;
        }

        public IHostModel Model => _entry.Model;

        /// <summary>The model, as the kind of model a request needs.</summary>
        /// <exception cref="HostRequestException">The request named a model of another kind.</exception>
        public T As<T>() where T : class, IHostModel
            => Model as T ?? throw new HostRequestException(HostErrorKind.Invalid,
                $"{Model.Spec.Id} is a {Model.Spec.Runner} model, which cannot serve this request.");

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0) _registry.Ended(_entry);
        }
    }

    /// <summary>Room borrowed for one picture; giving it back lets what made way for it load again.</summary>
    public sealed class Room : IDisposable
    {
        private readonly ModelRegistry _registry;
        private int _returned;

        internal Room(ModelRegistry registry) => _registry = registry;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _returned, 1) == 0) _registry.GiveBack();
        }
    }
}
