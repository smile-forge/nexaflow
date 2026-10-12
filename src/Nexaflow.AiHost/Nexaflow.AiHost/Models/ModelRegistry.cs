using System.Diagnostics;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Models;

/// <summary>
/// The models in memory. The resident set — what the app asked to keep loaded — stays; any other model a request
/// names is loaded on demand into what the budget leaves, and is the first to go when another needs the room.
/// Admission is against the runtime's VRAM budget, from each model's estimate: a model that would not fit is refused
/// rather than over-committed, after unloading what may go.
/// </summary>
internal sealed class ModelRegistry(ExecutionRuntime runtime, ModelAdapters adapters, HostLog log) : IDisposable
{
    internal sealed class Entry(LoadedModel model)
    {
        public LoadedModel Model { get; } = model;
        public bool Resident { get; set; }
        /// <summary>The app let it go while a request was using it.</summary>
        public bool UnloadWhenIdle { get; set; }
        public int Users { get; set; }
        public long LastUsed { get; set; } = Stopwatch.GetTimestamp();
    }

    private readonly Dictionary<string, Entry> _loaded = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _loading = new(1, 1);
    private readonly object _gate = new();

    public string ExecutionProvider => runtime.ExecutionProvider;

    /// <summary>The model, loaded — and kept loaded while the lease is held.</summary>
    /// <param name="resident">Keep it loaded after the lease, until it is unloaded.</param>
    /// <exception cref="HostRequestException">It would not fit beside the models that must stay, or would not load.</exception>
    public async Task<Lease> AcquireAsync(ModelSpec spec, bool resident, Func<string, Task> stage, CancellationToken ct)
    {
        if (TryLease(spec.Id, resident) is { } held) return held;

        await _loading.WaitAsync(ct);
        try
        {
            if (TryLease(spec.Id, resident) is { } loaded) return loaded;

            MakeRoomFor(spec);
            await stage("loading");
            var started = Stopwatch.GetTimestamp();
            LoadedModel model;
            try
            {
                model = await Task.Run(() => LoadedModel.Load(spec, runtime.ExecutionProvider, adapters), ct);
            }
            catch (Exception ex) when (ex is not HostRequestException and not OperationCanceledException)
            {
                throw new HostRequestException(HostErrorKind.Runtime, $"{spec.Id} did not load: {ex.Message}");
            }
            log.Write($"loaded {spec.Id} on {runtime.ExecutionProvider} in {Stopwatch.GetElapsedTime(started).TotalSeconds:0.0} s");

            lock (_gate)
            {
                var entry = new Entry(model) { Resident = resident, Users = 1 };
                _loaded[spec.Id] = entry;
                return new Lease(this, entry);
            }
        }
        finally { _loading.Release(); }
    }

    private Lease? TryLease(string id, bool resident)
    {
        lock (_gate)
        {
            if (!_loaded.TryGetValue(id, out var entry)) return null;
            entry.Resident |= resident;
            if (resident) entry.UnloadWhenIdle = false;
            entry.Users++;
            entry.LastUsed = Stopwatch.GetTimestamp();
            return new Lease(this, entry);
        }
    }

    /// <summary>Unloads what the budget says must go for <paramref name="spec"/> to fit, or refuses it.</summary>
    private void MakeRoomFor(ModelSpec spec)
    {
        if (runtime.BudgetBytes is not { } budget) return;

        lock (_gate)
        {
            var loaded = _loaded.Values.Select(e => new VramBudget.Loaded(e.Model.Spec.Id, e.Model.Spec.MemoryBytes, e.Resident, e.Users > 0, e.LastUsed)).ToList();
            if (VramBudget.Plan(budget, loaded, spec.MemoryBytes) is not { } unload)
                throw new HostRequestException(HostErrorKind.Refused,
                    $"{spec.Id} needs {spec.MemoryBytes >> 20} MiB of VRAM, and this GPU's {budget >> 20} MiB budget cannot make that room beside "
                    + string.Join(", ", _loaded.Keys) + ".");

            foreach (var id in unload) Unload(_loaded[id]);
        }
    }

    /// <summary>Takes a model out of the resident set; it is unloaded now if nothing is using it, or when the last
    /// request using it ends.</summary>
    public void Release(string id)
    {
        lock (_gate)
        {
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
            if (entry.Users == 0 && entry.UnloadWhenIdle) Unload(entry);
        }
    }

    private void Unload(Entry entry)
    {
        _loaded.Remove(entry.Model.Spec.Id);
        entry.Model.Dispose();
        log.Write($"unloaded {entry.Model.Spec.Id}");
    }

    public void Dispose()
    {
        lock (_gate)
            foreach (var entry in _loaded.Values.ToList()) Unload(entry);
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

        public LoadedModel Model => _entry.Model;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0) _registry.Ended(_entry);
        }
    }
}
