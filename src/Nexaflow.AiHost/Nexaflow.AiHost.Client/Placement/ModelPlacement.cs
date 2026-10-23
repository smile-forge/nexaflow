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
public sealed record PlacementNeed(ModelSpec OnGpu, ModelSpec? OnCpu = null, long BytesPerToken = 0, int ShortestContext = 0)
{
    public string Id => OnGpu.Id;

    /// <summary>It runs on the CPU wherever it is placed.</summary>
    public bool CpuOnly => OnGpu.MemoryBytes == 0;

    /// <summary>A language model, whose context can be shortened to fit.</summary>
    public bool HasContext => BytesPerToken > 0;

    /// <summary>What it holds on a GPU with <paramref name="context"/> tokens of context.</summary>
    public long BytesAt(int context) => OnGpu.MemoryBytes + BytesPerToken * context;

    /// <summary>The longest context, in whole <see cref="ModelPlacement.ContextStep"/>s and no longer than its own, that
    /// fits in <paramref name="room"/>; below its shortest when nothing useful does.</summary>
    internal int LongestIn(long room)
        => room < OnGpu.MemoryBytes ? 0
           : (int)Math.Min(OnGpu.ContextTokens, (room - OnGpu.MemoryBytes) / BytesPerToken / ModelPlacement.ContextStep * ModelPlacement.ContextStep);

    /// <summary>As <see cref="LongestIn"/>, for its CPU build.</summary>
    internal int LongestOnCpuIn(long room, ModelSpec onCpu)
        => room < onCpu.MemoryBytes ? 0
           : (int)Math.Min(onCpu.ContextTokens, (room - onCpu.MemoryBytes) / BytesPerToken / ModelPlacement.ContextStep * ModelPlacement.ContextStep);
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
/// shortest, before it runs on the CPU — and only a model with no CPU build is left out. On the CPU the other models
/// hold what their builds hold, and a language model is given the longest context the memory budget has left for it,
/// never shorter than its shortest: the budget is a share of the machine, not a wall.
/// </summary>
public static class ModelPlacement
{
    /// <summary>Contexts are given in whole steps of this many tokens.</summary>
    public const int ContextStep = 1024;

    /// <summary>What the desktop and the programs around it are taken to hold on a GPU, where the host has not said
    /// what it found free.</summary>
    public const long DesktopBytes = 1L << 30;

    private sealed class Seat(PlacementNeed need, int device, int context)
    {
        public PlacementNeed Need { get; } = need;
        public int Device { get; } = device;
        public int Context { get; set; } = context;
        public long Bytes => Need.BytesAt(Context);
        public long Spare => Need.HasContext ? Need.BytesPerToken * (Context - Need.ShortestContext) : 0;
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
        var placed = CpuPlaced(onCpu, memoryBudget);
        placed.AddRange(seats.Select(s => s.Need.OnGpu with { Device = s.Device, ContextTokens = s.Context, MemoryBytes = s.Bytes }));
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
