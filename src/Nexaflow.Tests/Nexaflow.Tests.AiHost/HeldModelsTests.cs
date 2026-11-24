using Nexaflow.AiHost;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Provisioning;
using Nexaflow.AiHost.Store;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The parts the host holds, planned together from its catalog on its own GPUs: where each hold is told its parts run, what a later
/// hold moves, what a voice is placed ahead of, what goes when, where a request's unheld part runs, and what a hold hears while a
/// picture borrows its room. The catalog is the real one; its parts load as fakes, and its files are in the store unless a test says.
/// </summary>
[TestClass]
[CoversNode("model-residency")]
public class HeldModelsTests
{
    private static readonly string Runtime = HostCatalog.Cuda;

    /// <summary>A language model's chat part, run as it is.</summary>
    private static readonly (HostedModel Model, HostedPart Part) Chat
        = HostCatalog.Models.Where(m => m.Kind == HostedKind.Language && m.RunsOn(Runtime))
                            .Select(m => (m, m.PartsFor(Runtime).First(p => p.Role == HostedRoles.Chat)))
                            .First();

    /// <summary>A held part with no context that holds room on a GPU — a speech model's.</summary>
    private static readonly (HostedModel Model, HostedPart Part) Speech
        = HostCatalog.Models.Where(m => !m.LoadsOnDemand && m.Kind != HostedKind.Language && m.RunsOn(Runtime))
                            .SelectMany(m => PlacementNeeds.Held(m, Runtime).Select(p => (m, p)))
                            .Where(c => c.p.BytesPerToken == 0 && c.p.MemoryBytes > 0)
                            .OrderByDescending(c => c.p.MemoryBytes)
                            .First();

    private static ModelRef RefOf((HostedModel Model, HostedPart Part) part) => new(part.Model.Id, part.Part.Name);

    private static PlacementNeed NeedOf((HostedModel Model, HostedPart Part) part) => PlacementNeeds.Of(part.Model, part.Part);

    /// <summary>One GPU of <see cref="Budget"/>, as the host finds it.</summary>
    internal sealed class OneGpu(long budget) : IHostGpus
    {
        public HostGpu Gpu(int device) => device == 0 ? new HostGpu(0, "GPU 0", null, budget + (2L << 30), budget, budget + (1L << 30))
            : throw new HostRequestException(HostErrorKind.Invalid, $"GPU {device} was asked for, and this host has 1.");

        public long BudgetNow(int device, long heldBytes) => Gpu(device).BudgetBytes;
    }

    /// <summary>Loads every runner's models as fakes that hold nothing, counting loads and unloads.</summary>
    internal sealed class Runners
    {
        public readonly List<string> Loads = [];
        public readonly List<string> Unloads = [];

        public ModelRunners All()
            => new(HostCatalog.Models.Concat<HostedEntry>(HostCatalog.Tools).SelectMany(e => e.Parts).Select(p => p.Runner).Distinct()
                                     .Select(id => new Runner(id, this)));

        private sealed class Runner(string id, Runners counts) : IModelRunner
        {
            public string Id => id;

            public IHostModel Load(ModelSpec spec)
            {
                lock (counts.Loads) counts.Loads.Add(spec.Id);
                return new Model(spec, counts);
            }
        }

        private sealed record Model(ModelSpec Spec, Runners Counts) : IHostModel
        {
            public void Dispose()
            {
                lock (Counts.Loads) Counts.Unloads.Add(Spec.Id);
            }
        }
    }

    /// <summary>The store, holding every file but those a test names missing, fetching them when asked.</summary>
    internal sealed class Store : IModelStore
    {
        public readonly HashSet<string> Missing = new(StringComparer.Ordinal);
        public readonly List<string> Fetched = [];

        public string PathOf(string relative) => relative;

        public bool Holds(IEnumerable<ModelAsset> files) => files.All(f => !Missing.Contains(f.RelativePath));

        public Task FetchAsync(IReadOnlyList<string> paths, IProgress<AssetProgress>? progress, CancellationToken ct)
        {
            Fetched.AddRange(paths);
            Missing.ExceptWith(paths);
            return Task.CompletedTask;
        }
    }

    /// <summary>What a hold is told, in order.</summary>
    private sealed class Told
    {
        private readonly List<HostReply> _replies = [];

        public Task Reply(HostReply reply)
        {
            lock (_replies) _replies.Add(reply);
            return Task.CompletedTask;
        }

        public IReadOnlyList<T> Of<T>() where T : HostReply
        {
            lock (_replies) return [.. _replies.OfType<T>()];
        }

        public ModelsPlaced Last => Of<ModelsPlaced>()[^1];
    }

    internal sealed record Host(HeldModels Held, ModelRegistry Registry, Runners Runners, Store Store) : IDisposable
    {
        public void Dispose()
        {
            Held.Dispose();
            Registry.Dispose();
        }
    }

    /// <summary>A host with one GPU of <paramref name="budget"/> for models, running <paramref name="runtime"/>'s builds — the CUDA pack's
    /// unless a test says.</summary>
    internal static Host HostWith(long budget, string? runtime = null)
    {
        var gpus = new OneGpu(budget);
        var runners = new Runners();
        var store = new Store();
        var log = new HostLog(TestTemp.CreateSubdirectory("nexaflow-held-").FullName, modelCalls: true);
        var registry = new ModelRegistry(gpus, runners.All(), log, store);
        return new Host(new HeldModels(registry, store, runtime ?? Runtime, () => runtime == HostCatalog.Cpu ? [] : [gpus.Gpu(0)], HostMemory.Unbounded, log), registry, runners, store);
    }

    private static HoldModels Hold(long id, RequestPriority priority, params ModelRef[] parts) => new(id, parts, priority);

    private static readonly Func<string, Task> Quiet = _ => Task.CompletedTask;

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("the condition did not come about within 10 seconds");
            await Task.Delay(20);
        }
    }

    [TestMethod]
    public async Task AHold_IsLoadedWhereThePlanPlacesIt_AndToldWhereItRuns()
    {
        var need = NeedOf(Chat);
        using var host = HostWith(need.BytesAt(need.OnGpu.ContextTokens) + (1L << 30));
        var told = new Told();

        await using var hold = await host.Held.HoldAsync(Hold(1, RequestPriority.Interactive, RefOf(Chat)), told.Reply, default);

        var placed = told.Last.Placed.Single();
        Assert.AreEqual((RefOf(Chat).Id, (int?)0, need.OnGpu.ContextTokens), (placed.Part.Id, placed.Device, placed.ContextTokens),
                        "on the GPU, with its own context where there is room for it");
        CollectionAssert.AreEqual(new[] { RefOf(Chat).Id }, host.Runners.Loads);
        Assert.AreEqual(placed, host.Held.Describe().Held.Single(), "the host describes where every held part runs");
    }

    [TestMethod]
    public async Task ALaterHold_ThatShortensAnEarliersContext_TellsTheEarlierHoldToo()
    {
        var chat = NeedOf(Chat);
        using var host = HostWith(chat.BytesAt(chat.ShortestContext) + NeedOf(Speech).OnGpu.MemoryBytes);
        var first = new Told();
        var second = new Told();

        await using var chatHold = await host.Held.HoldAsync(Hold(1, RequestPriority.Interactive, RefOf(Chat)), first.Reply, default);
        int alone = first.Last.Placed.Single().ContextTokens;
        await using var speechHold = await host.Held.HoldAsync(Hold(2, RequestPriority.Interactive, RefOf(Speech)), second.Reply, default);

        await WaitFor(() => first.Of<ModelsPlaced>().Count == 2);
        Assert.IsTrue(first.Last.Placed.Single().ContextTokens < alone, "the chat model gave the speech model room from its context");
        Assert.AreEqual(0, second.Last.Placed.Single().Device);
        Assert.AreEqual(2, host.Runners.Loads.Count(id => id == RefOf(Chat).Id), "loaded again at its shorter context");
    }

    [TestMethod]
    public async Task AVoiceHold_IsPlacedAheadOfAReplys_WhereTheyDoNotBothFit()
    {
        var chat = NeedOf(Chat);
        long budget = chat.BytesAt(chat.ShortestContext) + NeedOf(Speech).OnGpu.MemoryBytes - 1;

        async Task<int?> SpeechDeviceAsync(RequestPriority speechPriority)
        {
            using var host = HostWith(budget);
            var told = new Told();
            await using var chat = await host.Held.HoldAsync(Hold(1, RequestPriority.Interactive, RefOf(Chat)), _ => Task.CompletedTask, default);
            await using var speech = await host.Held.HoldAsync(Hold(2, speechPriority, RefOf(Speech)), told.Reply, default);
            return told.Last.Placed.SingleOrDefault()?.Device;
        }

        Assert.AreEqual(0, await SpeechDeviceAsync(RequestPriority.Voice), "a voice's part is placed first, on the GPU");
        Assert.AreNotEqual(0, await SpeechDeviceAsync(RequestPriority.Interactive), "beside a reply's at the same priority, the language model is placed first");
    }

    [TestMethod]
    public async Task APartTwoHoldsHold_IsLoadedOnce_AndGoesOnlyOnceBothLetGo()
    {
        var need = NeedOf(Chat);
        using var host = HostWith(need.BytesAt(need.OnGpu.ContextTokens) + (1L << 30));

        var first = await host.Held.HoldAsync(Hold(1, RequestPriority.Interactive, RefOf(Chat)), _ => Task.CompletedTask, default);
        var second = await host.Held.HoldAsync(Hold(2, RequestPriority.Interactive, RefOf(Chat)), _ => Task.CompletedTask, default);
        Assert.AreEqual(1, host.Runners.Loads.Count, "held twice, loaded once");

        await first.DisposeAsync();
        Assert.AreEqual(0, host.Runners.Unloads.Count, "the other hold still holds it");
        await second.DisposeAsync();
        CollectionAssert.AreEqual(new[] { RefOf(Chat).Id }, host.Runners.Unloads, "held by nobody and used by nothing, it goes");
        Assert.AreEqual(0, host.Held.Describe().Held.Count);
    }

    [TestMethod]
    public async Task AHoldGoing_MovesNoOtherHoldsParts()
    {
        var chat = NeedOf(Chat);
        using var host = HostWith(chat.BytesAt(chat.ShortestContext) + NeedOf(Speech).OnGpu.MemoryBytes);
        var first = new Told();

        await using var chatHold = await host.Held.HoldAsync(Hold(1, RequestPriority.Interactive, RefOf(Chat)), first.Reply, default);
        var speechHold = await host.Held.HoldAsync(Hold(2, RequestPriority.Interactive, RefOf(Speech)), _ => Task.CompletedTask, default);
        await WaitFor(() => first.Of<ModelsPlaced>().Count == 2);
        int loads = host.Runners.Loads.Count;

        await speechHold.DisposeAsync();

        Assert.AreEqual(loads, host.Runners.Loads.Count, "the chat model stays where it is, its context as it was");
        Assert.AreEqual(2, first.Of<ModelsPlaced>().Count, "and its hold hears nothing");
    }

    [TestMethod]
    public async Task APartNoHoldHolds_RunsAsItWouldBeHeld_AndAPicturesPartOnDemandBesideTheHeld()
    {
        var need = NeedOf(Chat);
        using var host = HostWith(need.BytesAt(need.OnGpu.ContextTokens) + (1L << 30));

        var chat = await host.Held.ResolveAsync(RefOf(Chat), Quiet, default);
        Assert.AreEqual(((int?)0, need.OnGpu.ContextTokens), (chat.Device, chat.ContextTokens), "placed as a hold would place it");

        var image = HostCatalog.Models.First(m => m.Kind == HostedKind.ImageGeneration && m.RunsOn(Runtime));
        var denoiser = image.PartsFor(Runtime).First(p => p.Role == DrawingParts.Denoiser);
        var spec = await host.Held.ResolveAsync(new ModelRef(image.Id, denoiser.Name), Quiet, default);
        var expected = ModelPlacement.PlaceOnDemand(new Placement([new OneGpu(need.BytesAt(need.OnGpu.ContextTokens) + (1L << 30)).Gpu(0)], [], []),
                                                    PlacementNeeds.Of(image, denoiser));
        Assert.AreEqual(expected, spec, "a picture's part is placed on demand beside what is held");
        Assert.AreEqual(0, host.Runners.Loads.Count, "resolving a part loads nothing");
    }

    [TestMethod]
    public async Task APartTheCatalogDoesNotName_OrAModelNotInTheStore_IsNotFound()
    {
        using var host = HostWith(24L << 30);
        var unnamed = await Assert.ThrowsExactlyAsync<HostRequestException>(() => host.Held.ResolveAsync(new ModelRef(Chat.Model.Id, "no-such-part"), Quiet, default));
        var unknown = await Assert.ThrowsExactlyAsync<HostRequestException>(
            () => host.Held.HoldAsync(Hold(1, RequestPriority.Interactive, new ModelRef("no-such-model", "chat")), _ => Task.CompletedTask, default));
        host.Store.Missing.Add(Chat.Model.FilesFor(Runtime)[0].RelativePath);
        var absent = await Assert.ThrowsExactlyAsync<HostRequestException>(() => host.Held.ResolveAsync(RefOf(Chat), Quiet, default));

        Assert.AreEqual((HostErrorKind.NotFound, HostErrorKind.NotFound, HostErrorKind.NotFound), (unnamed.Kind, unknown.Kind, absent.Kind));
        Assert.AreEqual(0, host.Store.Fetched.Count, "nothing is fetched for a model that is not here");
    }

    [TestMethod]
    public async Task ACpuBuildThePlacementNeeds_IsFetchedFirst_ForAModelThatIsHere()
    {
        var (entry, gpuPart, cpuPart) = HostCatalog.Models.Concat<HostedEntry>(HostCatalog.Tools)
            .SelectMany(e => e.Parts.Where(p => p.ExecutionProvider == Runtime && p.MemoryBytes > 0 && p.BytesPerToken == 0
                                                && p.Streamed is null && p.Tiled is null)
                                    .Select(p => (e, p, Cpu: e.Parts.FirstOrDefault(c => c.Name == p.Name && c.ExecutionProvider == HostCatalog.Cpu))))
            .First(c => c.Cpu is not null && c.Cpu.Folder != c.p.Folder);
        using var host = HostWith(gpuPart.MemoryBytes / 2);   // no GPU here holds the GPU build, and it has no lighter one
        var cpuFiles = PlacementNeeds.FilesOf(entry, cpuPart!).Select(f => f.RelativePath).ToList();
        host.Store.Missing.UnionWith(cpuFiles);

        var stages = new List<string>();
        var spec = await host.Held.ResolveAsync(new ModelRef(entry.Id, gpuPart.Name), s => { stages.Add(s); return Task.CompletedTask; }, default);

        Assert.AreEqual((null, cpuPart!.Folder), (spec.Device, spec.Folder), "on the CPU, its CPU build");
        CollectionAssert.AreEquivalent(cpuFiles, host.Store.Fetched, "that build's files, and only them");
        CollectionAssert.Contains(stages, HeldModels.Fetching);
    }


    [TestMethod]
    public async Task AHeldPartThatMakesWayForAPicture_IsToldAway_ThenBackOnceItIsLoadedAgain()
    {
        var need = NeedOf(Chat);
        long budget = need.BytesAt(need.OnGpu.ContextTokens) + (1L << 30);
        using var host = HostWith(budget);
        var told = new Told();
        await using var hold = await host.Held.HoldAsync(Hold(1, RequestPriority.Interactive, RefOf(Chat)), told.Reply, default);

        var picture = new ModelSpec("picture/denoiser", Chat.Part.Runner, "models/picture", null, budget, 0, Device: 0);
        using (var room = host.Held.BorrowRoom())
        using (await host.Registry.AcquireAsync(picture, resident: false, Quiet, default, room))
        {
            await WaitFor(() => told.Of<ModelsAway>().Count == 1);
            Assert.AreEqual(RefOf(Chat), told.Of<ModelsAway>()[0].Parts.Single());
        }

        await WaitFor(() => told.Of<ModelsBack>().Count == 1);
        Assert.AreEqual(RefOf(Chat), told.Of<ModelsBack>()[0].Parts.Single());
        Assert.AreEqual(2, host.Runners.Loads.Count(id => id == RefOf(Chat).Id), "loaded again once the picture was done");
    }
}
