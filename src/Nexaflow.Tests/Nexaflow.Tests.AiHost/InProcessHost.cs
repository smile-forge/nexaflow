using System.Threading.Channels;
using Nexaflow.AiHost;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Drawing;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Store;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The AI host's own parts in this test's process, over the test store (<see cref="TestStore"/>), built as the host builds them: the
/// registry that loads models, the holds that place them and the GPU's turns — for a test that runs a handler or a drawing method at a
/// placement it chooses, rather than the one a host's plan would choose for it. <see cref="OnTheGpu"/> runs on the first card, on the
/// CUDA pack bound in this process (<see cref="PackRuntime"/>), budgeting it as the host does from what is free on it;
/// <see cref="OnTheCpu"/> has no GPU, so every part is placed on the CPU — its CPU build where it has one — as on a machine without a
/// card.
/// </summary>
internal sealed class InProcessHost : IDisposable
{
    private readonly HostLog _log = new(null);

    private InProcessHost(string root, IHostGpus gpus, Func<IReadOnlyList<HostGpu>> gpusNow, string runtime)
    {
        var store  = new HostModelStore(root, [], MachineCards.Probe(), downloadsAllowed: false);
        var memory = HostMemory.Of(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, AiHostProtocol.DefaultMemoryShare);
        Models = new ModelRegistry(gpus, ModelRunners.Default, _log, store, memory);
        Held   = new HeldModels(Models, store, runtime, gpusNow, memory, _log);
    }

    /// <summary>What loads the models, each where the spec it is asked for places it.</summary>
    public ModelRegistry Models { get; }

    /// <summary>What places a part a request names, from the catalog, as the host's handlers ask it to.</summary>
    public HeldModels Held { get; }

    /// <summary>The GPU's turns, taken at a request's priority.</summary>
    public GpuScheduler Scheduler { get; } = new();

    /// <summary>A host on the first GPU and the CUDA pack. Inconclusive where the test store holds no pack this machine runs.</summary>
    public static InProcessHost OnTheGpu()
    {
        if (TestStore.Root is not { } root || PackRuntime.Bind() is null) throw new AssertInconclusiveException(TestStore.Missing());
        var gpu = new FirstGpu();
        return new InProcessHost(root, gpu, () => [gpu.Now()], HostCatalog.Cuda);
    }

    /// <summary>A host with no GPU, every part on the CPU. Inconclusive where no store is named.</summary>
    public static InProcessHost OnTheCpu()
    {
        if (TestStore.Root is not { } root) throw new AssertInconclusiveException(TestStore.Missing(gpu: false));
        // Bound as the host binds itself where the store holds the pack, so a later test in this process that runs on the GPU still can.
        PackRuntime.Bind();
        return new InProcessHost(root, new WithoutGpu(), () => [], HostCatalog.Cpu);
    }

    /// <summary>Runs <paramref name="request"/> through <paramref name="handler"/> as the host's server runs a request with no input to
    /// come, and gives back everything it replied, in order.</summary>
    public static async Task<List<HostReply>> RunAsync(IHostRequestHandler handler, HostRequest request, CancellationToken ct = default)
    {
        var replies = new List<HostReply>();
        var input = Channel.CreateUnbounded<RequestInput>();
        input.Writer.Complete();
        await handler.HandleAsync(request, reply =>
        {
            lock (replies) replies.Add(reply);
            return Task.CompletedTask;
        }, input.Reader, ct);
        return replies;
    }

    /// <summary>
    /// Draws <paramref name="request"/>'s picture with <paramref name="model"/> as it is given — each part where the test placed it — by
    /// <paramref name="method"/>, as the host's draw handler does once it has placed them: an edit read at the size its model reads its
    /// first picture at, the picture drawn in room borrowed for it.
    /// </summary>
    public async Task<PngReader.Picture> DrawAsync(IDrawingMethod method, Draw request, DrawingModel model, CancellationToken ct = default)
    {
        if (request.Sources.Count > 0) request = EditFitting.Fitted(request, method.ReadSize);
        using var room = Models.BorrowRoom();
        using var rgb = await method.DrawAsync(request, model, _ => Task.CompletedTask, (_, _) => Task.CompletedTask, room, ct);
        return new PngReader.Picture(request.Width, request.Height, rgb.ToArray(), []);
    }

    public void Dispose()
    {
        Held.Dispose();
        Models.Dispose();
        _log.Dispose();
    }

    /// <summary>The first card as the host finds it: its budget what was free on it when the host started, less what the runtime keeps,
    /// and now what is free with the host's own models counted back in.</summary>
    private sealed class FirstGpu : IHostGpus
    {
        private readonly HostGpu _started;

        public FirstGpu()
        {
            var (total, free) = GpuMemoryWatch.MemoryNow();
            _started = new HostGpu(0, "GPU 0", null, total, ExecutionRuntime.BudgetOf(free), free);
        }

        /// <summary>The card with what is free on it now.</summary>
        public HostGpu Now() => _started with { FreeBytes = GpuMemoryWatch.MemoryNow().Free };

        public HostGpu Gpu(int device)
            => device == 0 ? _started : throw new HostRequestException(HostErrorKind.Invalid, $"GPU {device} was asked for, and this host has 1.");

        public long BudgetNow(int device, long heldBytes) => ExecutionRuntime.BudgetOf(Gpu(device), GpuMemoryWatch.MemoryNow().Free, heldBytes);
    }

    /// <summary>No GPU: a host that runs models on the CPU.</summary>
    private sealed class WithoutGpu : IHostGpus
    {
        public HostGpu Gpu(int device)
            => throw new HostRequestException(HostErrorKind.Invalid, $"GPU {device} was asked for, and this host runs models on the CPU.");
    }
}
