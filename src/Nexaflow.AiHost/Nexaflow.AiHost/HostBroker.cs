using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Drawing;
using Nexaflow.AiHost.Filming;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Reading;
using Nexaflow.AiHost.Restoring;
using Nexaflow.AiHost.Selecting;
using Nexaflow.AiHost.Speech;
using Nexaflow.AiHost.Store;

namespace Nexaflow.AiHost;

/// <summary>
/// What a host serves with, however many clients it has and however it runs — for an app, or as the service's worker: its model
/// store, the runtime loaded from it, the models and what holds them, and a handler for each request. Made once as the host starts,
/// let go as it ends.
/// </summary>
internal sealed class HostBroker : IDisposable
{
    private readonly ExecutionRuntime _runtime;
    private readonly ModelRegistry _models;

    /// <param name="memoryShare">The share of this machine's memory, in percent, models on the CPU may hold.</param>
    public HostBroker(HostModelStore store, int memoryShare, HostPolicy policy, HostLog log)
    {
        Store = store;
        // The store is the host's alone; its runtime is the CUDA pack from it, where this machine's cards and driver run the pack.
        RuntimePack = store.RuntimePack();
        NativeRuntime.Load(RuntimePack);
        _runtime = new ExecutionRuntime(log, memoryShare);
        _models  = new ModelRegistry(_runtime, ModelRunners.Default, log, store, _runtime.Memory, policy.IdleUnload);
        Held     = new HeldModels(_models, store, NativeRuntime.Pack is null ? Catalog.HostCatalog.Cpu : Catalog.HostCatalog.Cuda,
                                  _runtime.GpusNow, _runtime.Memory, log);
        var requests  = new RequestModels(Held);
        var scheduler = new GpuScheduler();
        Handlers =
        [
            new DescribeHostHandler(_runtime, Held),
            new DescribeModelStoreHandler(store),
            new FetchModelFilesHandler(store),
            new RemoveModelFilesHandler(store),
            new HoldModelsHandler(Held),
            new GenerateHandler(_models, Held, scheduler),
            new ForgetConversationHandler(_models),
            new ListenHandler(_models, requests, scheduler),
            new SpeakHandler(_models, requests, scheduler, new SpeechPhonemizers()),
            new DrawHandler(_models, requests, [new ZImageDrawing(_models, scheduler), new HiDreamDrawing(_models, scheduler), new MossDrawing(_models, scheduler),
                                                new KleinDrawing(_models, scheduler), new QwenEditDrawing(_models, scheduler)]),
            new FilmHandler(_models, requests, [new WanFilming(_models, scheduler)]),
            new RestoreHandler(Held, scheduler),
            new SelectHandler(Held, scheduler),
            new FindTextHandler(Held),
            new Erasing.EraseHandler(Held, scheduler),
            new Checking.CheckPictureHandler(Held),
            new SceneDepth.SceneDepthHandler(Held, scheduler),
        ];
    }

    public HostModelStore Store { get; }

    /// <summary>The CUDA pack the runtime was loaded from; null for the install's CPU runtime.</summary>
    public string? RuntimePack { get; }

    public HeldModels Held { get; }

    /// <summary>This machine's memory, and the share of it models on the CPU may hold.</summary>
    public HostMemory Memory => _runtime.Memory;

    public IReadOnlyList<IHostRequestHandler> Handlers { get; }

    public void Dispose()
    {
        Held.Dispose();
        _models.Dispose();
        _runtime.Dispose();
    }
}
