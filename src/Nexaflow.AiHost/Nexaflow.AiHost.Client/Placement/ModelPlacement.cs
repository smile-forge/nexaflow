using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;

namespace Nexaflow.AiHost.Client;

/// <summary>What a model needs to be placed on this machine.</summary>
/// <param name="OnGpu">How it runs on a GPU: <see cref="ModelSpec.MemoryBytes"/> is what it holds there with no
/// context, and <see cref="ModelSpec.ContextTokens"/> the context it is given where there is room. A spec that holds
/// nothing on a GPU runs on the CPU, whatever the machine.</param>
/// <param name="OnCpu">How it runs on the CPU when no GPU has room for it — the same model, or a build of its own — its
/// <see cref="ModelSpec.MemoryBytes"/> what it holds there with no context; null for a model that runs on a GPU or not
/// at all.</param>
/// <param name="BytesPerToken">What each token of context holds, wherever it runs; 0 for a model with no context.</param>
/// <param name="ShortestContext">The shortest context a language model is any use with.</param>
/// <param name="Layers">How many layers a language model has; 0 for one never cut between a GPU and the CPU.</param>
/// <param name="WeightsBytes">What a language model's weights hold, all its layers' together — the rest of what it holds on a
/// GPU being what it holds there however few of them are.</param>
/// <param name="BatchBytesPerToken">What more it holds for each token read in a step past <see cref="ModelSpec.DefaultBatch"/>;
/// 0 where that is not known, and it reads in steps of that many.</param>
/// <param name="BytesPerSequence">What more it holds for each sequence its context holds past <see cref="ModelSpec.DefaultSequences"/>;
/// 0 where that is not known, and it holds that many.</param>
public sealed record PlacementNeed(ModelSpec OnGpu, ModelSpec? OnCpu = null, long BytesPerToken = 0, int ShortestContext = 0, int Layers = 0,
                                   long WeightsBytes = 0, long BatchBytesPerToken = 0, long BytesPerSequence = 0)
{
    public string Id => OnGpu.Id;

    /// <summary>It runs on the CPU wherever it is placed.</summary>
    public bool CpuOnly => OnGpu.MemoryBytes == 0;

    /// <summary>A language model, whose context can be shortened to fit.</summary>
    public bool HasContext => BytesPerToken > 0;

    /// <summary>What it holds on a GPU with <paramref name="context"/> tokens of context, <paramref name="layers"/> of its layers
    /// there — all of them, when null — reading <paramref name="batch"/> tokens a step, and holding <paramref name="sequences"/>. Cut,
    /// the GPU holds its layers' share of the weights and of the context, and everything else whole.</summary>
    public long BytesAt(int context, int? layers = null, int batch = ModelSpec.DefaultBatch, int sequences = ModelSpec.DefaultSequences)
    {
        long more = BatchBytesPerToken * Math.Max(0, batch - ModelSpec.DefaultBatch) + BytesPerSequence * Math.Max(0, sequences - ModelSpec.DefaultSequences);
        return layers is { } some
            ? OnGpu.MemoryBytes - WeightsBytes + (WeightsBytes + BytesPerToken * context) * some / Layers + more
            : OnGpu.MemoryBytes + BytesPerToken * context + more;
    }

    /// <summary>The longest context, in whole <see cref="ModelPlacement.ContextStep"/>s and no longer than its own, that
    /// fits in <paramref name="room"/>; below its shortest when nothing useful does.</summary>
    internal int LongestIn(long room)
        => room < OnGpu.MemoryBytes ? 0
           : (int)Math.Min(OnGpu.ContextTokens, (room - OnGpu.MemoryBytes) / BytesPerToken / ModelPlacement.ContextStep * ModelPlacement.ContextStep);

    /// <summary>As <see cref="LongestIn"/>, for its CPU build.</summary>
    internal int LongestOnCpuIn(long room, ModelSpec onCpu)
        => room < onCpu.MemoryBytes ? 0
           : (int)Math.Min(onCpu.ContextTokens, (room - onCpu.MemoryBytes) / BytesPerToken / ModelPlacement.ContextStep * ModelPlacement.ContextStep);

    /// <summary>The most of its layers, fewer than all of them, that fit in <paramref name="room"/> with <paramref name="context"/>
    /// tokens of context — 0 where fewer than half of them do, or it is never cut.</summary>
    internal int LayersIn(long room, int context)
    {
        if (Layers == 0 || WeightsBytes <= 0) return 0;
        long whole = OnGpu.MemoryBytes - WeightsBytes;
        long each = (WeightsBytes + BytesPerToken * context + Layers - 1) / Layers;
        int fit = room <= whole ? 0 : (int)Math.Min(Layers - 1, (room - whole) / each);
        return fit * 2 >= Layers ? fit : 0;
    }
}

/// <summary>Where the resident models run: each one's spec as placed — its GPU, or the CPU, and a language model's
/// context — and the models no GPU had room for and that have no CPU build.</summary>
public sealed record Placement(IReadOnlyList<HostGpu> Gpus, IReadOnlyList<ModelSpec> Placed, IReadOnlyList<string> Unplaced)
{
    public ModelSpec? Of(string id) => Placed.FirstOrDefault(s => s.Id == id);

    /// <summary>What the placed models leave of <paramref name="gpu"/>'s budget.</summary>
    public long FreeOn(HostGpu gpu) => gpu.BudgetBytes - Placed.Where(s => s.Device == gpu.Device).Sum(s => s.MemoryBytes);

    /// <summary>Whether loading <paramref name="onDemand"/>, as placed, makes resident models on its GPU make way.</summary>
    public bool Displaces(ModelSpec onDemand)
        => onDemand.Device is { } device && Gpus.First(g => g.Device == device) is var gpu && FreeOn(gpu) < onDemand.MemoryBytes;
}

/// <summary>
/// Plans where models run, from the GPUs there are and each GPU's budget. Language models are placed first, then the
/// rest from the largest down, each on the GPU it fills most nearly — so a GPU is left as whole as can be for a
/// picture. A language model is given its own context where there is room for it, and a shorter one where there is
/// not; a model that finds no room takes it from the contexts of the language models on a GPU, down to their
/// shortest; a language model that still finds none runs cut between the GPU with the most room and the CPU, where at
/// least half its layers fit there — and only then on the CPU, and only a model with no CPU build is left out. On the
/// CPU the other models hold what their builds hold, and a language model is given the longest context the memory budget
/// has left for it, never shorter than its shortest: the budget is a share of the machine, not a wall. What every GPU
/// has left once all are placed lets each language model wholly on it read a prompt in longer steps.
/// </summary>
public static class ModelPlacement
{
    /// <summary>Contexts are given in whole steps of this many tokens.</summary>
    public const int ContextStep = 1024;

    /// <summary>What the desktop and the programs around it are taken to hold on a GPU, where the host has not said
    /// what it found free.</summary>
    public const long DesktopBytes = 1L << 30;

    /// <summary>The longest step a language model reads a prompt in: measured on a Gemma 4 26B, a step twice as long reads no
    /// faster and holds 3 GB more.</summary>
    public const int LongestBatch = 2048;

    /// <summary>
    /// The most sequences a language model's context is given, where room is left for them. Every step reads against every
    /// sequence's window, so past this many — measured on a Gemma 4 26B, its replies each a passage of a document long — more
    /// replies at once write no faster together, and each holds 110 MB.
    /// </summary>
    public const int MostSequences = 16;

    /// <summary>The context a language model cut between a GPU and the CPU is given: what it reads beyond is read slowly
    /// wherever it is held, so its layers come before its length.</summary>
    public const int CutContext = 32768;

    private sealed class Seat(PlacementNeed need, int device, int context)
    {
        public PlacementNeed Need { get; } = need;
        public int Device { get; } = device;
        public int Context { get; set; } = context;

        /// <summary>How many of its layers it has on its GPU; null for all of them.</summary>
        public int? Layers { get; init; }

        public int Batch { get; set; } = ModelSpec.DefaultBatch;
        public int Sequences { get; set; } = ModelSpec.DefaultSequences;
        public long Bytes => Need.BytesAt(Context, Layers, Batch, Sequences);
        public long Spare => Need.HasContext && Layers is null ? Need.BytesPerToken * (Context - Need.ShortestContext) : 0;
    }

    /// <param name="memoryBudget">What models on the CPU may hold — <see cref="HostMemory.BudgetBytes"/>.</param>
    public static Placement Plan(IReadOnlyList<HostGpu> gpus, IReadOnlyList<PlacementNeed> needs, long memoryBudget = long.MaxValue)
    {
        var seats = new List<Seat>();
        var onCpu = new List<PlacementNeed>();
        var unplaced = new List<string>();
        long Free(HostGpu gpu) => gpu.BudgetBytes - seats.Where(s => s.Device == gpu.Device).Sum(s => s.Bytes);

        foreach (var need in needs.OrderByDescending(n => n.HasContext).ThenByDescending(n => n.OnGpu.MemoryBytes).ThenBy(n => n.Id, StringComparer.Ordinal))
        {
            var seat = need.CpuOnly ? null : Seated(need);
            if (seat is not null) seats.Add(seat);
            else if (need.CpuOnly || need.OnCpu is not null) onCpu.Add(need);
            else unplaced.Add(need.Id);
        }

        // Longer steps, then more replies at once, for a language model wholly on a GPU, from what is left there once everything
        // is placed.
        foreach (var seat in seats.Where(s => s.Layers is null))
        {
            var gpu = gpus.First(g => g.Device == seat.Device);
            if (seat.Need.BatchBytesPerToken > 0 && Free(gpu) >= seat.Need.BatchBytesPerToken * (LongestBatch - ModelSpec.DefaultBatch))
                seat.Batch = LongestBatch;
            if (seat.Need.BytesPerSequence > 0 && Free(gpu) >= seat.Need.BytesPerSequence * (MostSequences - ModelSpec.DefaultSequences))
                seat.Sequences = MostSequences;
        }

        var placed = CpuPlaced(onCpu, memoryBudget);
        placed.AddRange(seats.Select(s => s.Need.OnGpu with { Device = s.Device, ContextTokens = s.Context, MemoryBytes = s.Bytes, BatchTokens = s.Batch,
                                                              GpuLayers = s.Layers, Sequences = s.Sequences }));
        return new Placement(gpus, placed, unplaced);

        Seat? Seated(PlacementNeed need)
        {
            // Whole, on the GPU it fills most nearly.
            long whole = need.BytesAt(need.OnGpu.ContextTokens);
            if (gpus.Where(g => Free(g) >= whole).OrderBy(Free).ThenBy(g => g.Device).FirstOrDefault() is { } fits)
                return new Seat(need, fits.Device, need.OnGpu.ContextTokens);

            // A language model with a shorter context, on the GPU with the most room.
            if (need.HasContext && gpus.OrderByDescending(Free).ThenBy(g => g.Device).FirstOrDefault() is { } roomiest
                && need.LongestIn(Free(roomiest)) is var context && context >= need.ShortestContext)
                return new Seat(need, roomiest.Device, context);

            // Room the language models already placed give up, down to their shortest contexts.
            long needed = need.HasContext ? need.BytesAt(need.ShortestContext) : whole;
            foreach (var gpu in gpus.OrderByDescending(g => Free(g) + Spare(g)).ThenBy(g => g.Device))
            {
                if (Free(gpu) + Spare(gpu) < needed) break;
                foreach (var seat in seats.Where(s => s.Device == gpu.Device && s.Spare > 0).OrderByDescending(s => s.Spare))
                {
                    long short_ = needed - Free(gpu);
                    if (short_ <= 0) break;
                    long steps = (short_ + seat.Need.BytesPerToken * ContextStep - 1) / (seat.Need.BytesPerToken * ContextStep);
                    seat.Context = Math.Max(seat.Need.ShortestContext, seat.Context - (int)steps * ContextStep);
                }
                return new Seat(need, gpu.Device, need.HasContext ? need.LongestIn(Free(gpu)) : need.OnGpu.ContextTokens);
            }

            // Cut between the GPU with the most room and the CPU, where at least half its layers fit there: each layer on the
            // GPU is read many times faster than on the CPU.
            if (need.HasContext && gpus.OrderByDescending(Free).ThenBy(g => g.Device).FirstOrDefault() is { } most
                && Math.Max(need.ShortestContext, Math.Min(need.OnGpu.ContextTokens, CutContext)) is var cut
                && need.LayersIn(Free(most), cut) is > 0 and var layers)
                return new Seat(need, most.Device, cut) { Layers = layers };
            return null;
        }

        long Spare(HostGpu gpu) => seats.Where(s => s.Device == gpu.Device).Sum(s => s.Spare);
    }

    /// <summary>The models that run on the CPU, each as its CPU build: the others holding what they hold, the language
    /// models — in the order planned — each given what is left of <paramref name="memoryBudget"/>, down to its shortest
    /// context.</summary>
    private static List<ModelSpec> CpuPlaced(IReadOnlyList<PlacementNeed> onCpu, long memoryBudget)
    {
        static ModelSpec Build(PlacementNeed need) => (need.OnCpu ?? need.OnGpu) with { Device = null };
        long left = memoryBudget - onCpu.Where(n => !n.HasContext).Sum(n => Build(n).MemoryBytes);
        var placed = new List<ModelSpec>();
        foreach (var need in onCpu)
        {
            var build = Build(need);
            if (need.HasContext)
            {
                int context = Math.Max(need.ShortestContext, need.LongestOnCpuIn(left, build));
                build = build with { ContextTokens = context, MemoryBytes = build.MemoryBytes + need.BytesPerToken * context };
                left -= build.MemoryBytes;
            }
            placed.Add(build);
        }
        return placed;
    }

    /// <summary>Where a model loaded on demand — a picture's part — runs: on a GPU it fits beside the resident models
    /// if one has the room, else on the GPU with the most room once residents make way for it; on the CPU, or nowhere,
    /// when no GPU's whole budget is enough.</summary>
    public static ModelSpec? PlaceOnDemand(Placement residents, PlacementNeed need)
    {
        if (need.CpuOnly) return (need.OnCpu ?? need.OnGpu) with { Device = null };
        long bytes = need.OnGpu.MemoryBytes;
        var gpu = residents.Gpus.Where(g => g.BudgetBytes >= bytes)
                                .OrderByDescending(g => residents.FreeOn(g) >= bytes)
                                .ThenByDescending(residents.FreeOn)
                                .ThenBy(g => g.Device)
                                .FirstOrDefault();
        return gpu is not null ? need.OnGpu with { Device = gpu.Device }
             : need.OnCpu is { } cpu ? cpu with { Device = null }
             : null;
    }

    /// <summary>The GPUs the host would find on this machine, as far as its adapters tell before the host has run:
    /// each NVIDIA GPU of its own where the CUDA pack suits the machine, less what the desktop is taken to hold and the
    /// runtime keeps back.</summary>
    public static IReadOnlyList<HostGpu> Expected(ComputeHardwareReport hardware)
        => !CudaRuntimePack.Applies(hardware) ? []
           : [.. hardware.Adapters.Where(a => a.Vendor == ComputeVendor.Nvidia && !a.IsIntegrated && !a.IsNpu)
                                  .Select((a, i) => new HostGpu(i, a.Name, null, a.DedicatedMemoryBytes,
                                                                Math.Max(0, a.DedicatedMemoryBytes - DesktopBytes - HostGpu.ReservedBytes),
                                                                a.DedicatedMemoryBytes - DesktopBytes))];
}
