using System.Diagnostics;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Models;

/// <summary>
/// The models in memory. The resident set — what the app asked to keep loaded — stays; any other model a request
/// names is loaded on demand into what its GPU's budget leaves, and is the first to go when another needs the room.
/// Admission is per GPU, against its budget, from each model's estimate: a model that would not fit is refused rather
/// than over-committed, after unloading what may go. A picture borrows a <see cref="Room"/>: while one is out,
/// residents nothing is using may make way for its parts — or the picture waits until they are idle — and they are
/// loaded again once the last picture is done, or as soon as a request needs one.
/// </summary>
/// <param name="memory">The share of this machine's memory models on the CPU may hold; none when unset.</param>
internal sealed class ModelRegistry(IHostGpus gpus, ModelAdapters adapters, ModelRunners runners, HostLog log, HostMemory? memory = null) : IDisposable
{
    /// <summary>How often a request waiting for room says so, so the app's watchdog keeps hearing from it.</summary>
    private static readonly TimeSpan WaitingReport = TimeSpan.FromSeconds(20);

    internal sealed class Entry(IHostModel model)
    {
        public IHostModel Model { get; } = model;
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
    private readonly SemaphoreSlim _loading = new(1, 1);
    private readonly object _gate = new();
    private TaskCompletionSource _idle = Signal();
    private int _rooms;

    /// <summary>The model, loaded where its spec places it — and kept loaded while the lease is held.</summary>
    /// <param name="resident">Keep it loaded after the lease, until it is unloaded.</param>
    /// <param name="room">The picture it is loaded for: residents nothing is using may make way for it.</param>
    /// <exception cref="HostRequestException">It names a GPU this host does not have, would not fit beside the models
    /// that must stay, or would not load.</exception>
    public async Task<Lease> AcquireAsync(ModelSpec spec, bool resident, Func<string, Task> stage, CancellationToken ct, Room? room = null)
    {
        if (spec.Device is { } device) gpus.Gpu(device);
        if (TryLease(spec, ref resident) is { } held) return held;

        while (true)
        {
            Task idle;
            await _loading.WaitAsync(ct);
            try
            {
                if (TryLease(spec, ref resident) is { } loaded) return loaded;
                lock (_gate) idle = _idle.Task;
                if (MakeRoomFor(spec, room, out var madeWay))
                {
                    if (madeWay.Count > 0) await room!.MadeWayAsync(madeWay);
                    return await LoadAsync(spec, resident, stage, ct);
                }
            }
            finally { _loading.Release(); }

            // Only models in use stand in the way: wait until one is let go, then plan again.
            while (!idle.IsCompleted)
            {
                await stage("waiting");
                await Task.WhenAny(idle, Task.Delay(WaitingReport, ct));
                ct.ThrowIfCancellationRequested();
            }
        }
    }

    /// <summary>A lease on the model as loaded, when it is loaded as <paramref name="spec"/> places it. One loaded
    /// elsewhere or at another size is let go — its residence passing to the one about to load.</summary>
    private Lease? TryLease(ModelSpec spec, ref bool resident)
    {
        lock (_gate)
        {
            if (!_loaded.TryGetValue(spec.Id, out var entry)) return null;
            if (!entry.Model.Spec.PlacedAs(spec))
            {
                resident |= entry.Resident;
                _loaded.Remove(spec.Id);
                if (entry.Users == 0) Free(entry);
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
            model = await Task.Run(() =>
            {
                if (spec.Adapter is { } adapter) adapters.EnsurePrepared(spec.Folder, adapter);
                return runners.Load(spec);
            }, ct);
        }
        catch (Exception ex) when (ex is not HostRequestException and not OperationCanceledException)
        {
            throw new HostRequestException(HostErrorKind.Runtime, $"{spec.Id} did not load: {ex.Message}");
        }
        log.Write($"loaded {spec.Id} on {Where(spec)} in {Stopwatch.GetElapsedTime(started).TotalSeconds:0.0} s");

        lock (_gate)
        {
            // A resident that made way for a picture is resident again, whoever loads it.
            bool displaced = _displaced.Remove(spec.Id);
            var entry = new Entry(model) { Resident = resident || displaced, Users = 1 };
            _loaded[spec.Id] = entry;
            return new Lease(this, entry);
        }
    }

    /// <summary>Unloads what must go for <paramref name="spec"/> to fit on its GPU.</summary>
    /// <param name="madeWay">The residents that made way for the picture <paramref name="room"/> is borrowed for — they load
    /// again once it is done.</param>
    /// <returns>False when only models in use stand in the way and it may wait for them: a picture's part, or a
    /// resident that made way for one.</returns>
    /// <exception cref="HostRequestException">It cannot fit beside what must stay.</exception>
    private bool MakeRoomFor(ModelSpec spec, Room? room, out IReadOnlyList<string> madeWay)
    {
        madeWay = [];
        // A GPU's budget is a wall: past it a load fails in the middle of somebody's reply. The CPU's is a share of this
        // machine's memory, kept by letting idle models go — never by refusing a model, or making it wait, for it.
        long budget = spec.Device is { } device ? gpus.Gpu(device).BudgetBytes : (memory ?? HostMemory.Unbounded).BudgetBytes;

        lock (_gate)
        {
            var held = _loaded.Values.Concat(_replaced).Where(e => e.Model.Spec.Device == spec.Device)
                              .Select(e => new VramBudget.Loaded(e.Model.Spec.Id, e.Model.Spec.MemoryBytes, e.Resident, e.Users > 0, e.LastUsed))
                              .ToList();
            var admission = VramBudget.Plan(budget, held, spec.MemoryBytes, displaceResidents: room is not null);
            if (admission is null || admission.Waits)
            {
                if (spec.Device is { } gpu)
                {
                    if (admission is null || room is null && !_displaced.ContainsKey(spec.Id))
                        throw new HostRequestException(HostErrorKind.Refused,
                            $"{spec.Id} needs {spec.MemoryBytes >> 20} MiB of GPU {gpu}, and its {budget >> 20} MiB budget cannot make that room beside "
                            + string.Join(", ", held.Select(m => m.Id)) + ".");
                    return false;
                }
                admission = new VramBudget.Admission([.. held.Where(m => !m.InUse && (!m.Resident || room is not null)).Select(m => m.Id)], Waits: false);
                log.Write($"{spec.Id} needs {spec.MemoryBytes >> 20} MiB on the CPU, past its {budget >> 20} MiB share beside the models in use; it loads past it");
            }

            var residents = new List<string>();
            foreach (var id in admission.Unload)
            {
                var entry = _loaded[id];
                if (entry.Resident)
                {
                    _displaced[id] = entry.Model.Spec;
                    residents.Add(id);
                }
                Unload(entry);
            }
            madeWay = residents;
            return true;
        }
    }

    /// <summary>Room for one picture: while any is out, residents nothing is using may make way for its parts.</summary>
    /// <param name="madeWay">Told the residents that make way for this picture, as they do.</param>
    public Room BorrowRoom(Func<IReadOnlyList<string>, Task>? madeWay = null)
    {
        lock (_gate) _rooms++;
        return new Room(this, madeWay);
    }

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

    /// <summary>Takes a model out of the resident set; it is unloaded now if nothing is using it, or when the last
    /// request using it ends.</summary>
    public void Release(string id)
    {
        lock (_gate)
        {
            _displaced.Remove(id);
            if (!_loaded.TryGetValue(id, out var entry)) return;
            entry.Resident = false;
            if (entry.Users == 0) Unload(entry);
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

            var idle = _idle;
            _idle = Signal();
            idle.TrySetResult();
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
        log.Write($"unloaded {entry.Model.Spec.Id} from {Where(entry.Model.Spec)}");
    }

    private static string Where(ModelSpec spec) => spec.Device is { } device ? $"GPU {device}" : "the CPU";

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var entry in _loaded.Values.ToList()) Unload(entry);
            foreach (var entry in _replaced) Free(entry);
            _replaced.Clear();
        }
        _loading.Dispose();
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
        private readonly Func<IReadOnlyList<string>, Task>? _madeWay;
        private int _returned;

        internal Room(ModelRegistry registry, Func<IReadOnlyList<string>, Task>? madeWay)
        {
            _registry = registry;
            _madeWay  = madeWay;
        }

        /// <summary>Residents have made way for this picture.</summary>
        internal Task MadeWayAsync(IReadOnlyList<string> ids) => _madeWay?.Invoke(ids) ?? Task.CompletedTask;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _returned, 1) == 0) _registry.GiveBack();
        }
    }
}
