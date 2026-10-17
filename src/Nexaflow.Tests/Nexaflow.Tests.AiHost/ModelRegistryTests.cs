using Nexaflow.AiHost;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>The models in memory, GPU by GPU: what stays, what a picture's parts make way for, and what loads again.</summary>
[TestClass]
[CoversNode("ai-host")]
public class ModelRegistryTests
{
    private const long GB = 1L << 30;

    /// <summary>Two GPUs of a 10 GB budget each.</summary>
    private sealed class TwoGpus : IHostGpus
    {
        public HostGpu Gpu(int device) => device is 0 or 1 ? new HostGpu(device, $"GPU {device}", null, 12 * GB, 10 * GB, 11 * GB)
            : throw new HostRequestException(HostErrorKind.Invalid, $"GPU {device} was asked for, and this host has 2.");
    }

    /// <summary>A model that holds nothing but its spec, and counts its loads.</summary>
    private sealed class Runner : IModelRunner
    {
        public readonly List<string> Loads = [];
        public string Id => "fake";

        public IHostModel Load(ModelSpec spec)
        {
            lock (Loads) Loads.Add(spec.Id);
            return new Model(spec);
        }

        private sealed record Model(ModelSpec Spec) : IHostModel
        {
            public void Dispose() { }
        }
    }

    private static ModelSpec Spec(string id, double gb, int? device = 0) => new(id, "fake", @"C:\models\" + id, null, null, (long)(gb * GB), 0, Device: device);

    private static readonly Func<string, Task> Quiet = _ => Task.CompletedTask;

    private static (ModelRegistry Registry, Runner Runner) Registry()
    {
        var runner = new Runner();
        var log = new HostLog(TestTemp.CreateSubdirectory("nexaflow-registry-").FullName);
        return (new ModelRegistry(new TwoGpus(), new ModelAdapters([]), new ModelRunners([runner]), log), runner);
    }

    [TestMethod]
    public async Task EachGpu_HasABudgetOfItsOwn()
    {
        var (registry, _) = Registry();

        using var chat = await registry.AcquireAsync(Spec("chat", 8, device: 0), resident: true, Quiet, default);
        using var voice = await registry.AcquireAsync(Spec("voice", 8, device: 1), resident: true, Quiet, default);

        var refused = await Assert.ThrowsExactlyAsync<HostRequestException>(() => registry.AcquireAsync(Spec("more", 4, device: 0), resident: true, Quiet, default));
        Assert.AreEqual(HostErrorKind.Refused, refused.Kind);
        StringAssert.Contains(refused.Message, "GPU 0");
    }

    [TestMethod]
    public async Task APicture_HasIdleResidentsMakeWay_AndTheyLoadAgainOnceItIsDone()
    {
        var (registry, runner) = Registry();
        (await registry.AcquireAsync(Spec("chat", 6), resident: true, Quiet, default)).Dispose();

        var told = new List<string>();
        using (var room = registry.BorrowRoom(ids => { told.AddRange(ids); return Task.CompletedTask; }))
        using (await registry.AcquireAsync(Spec("denoiser", 9), resident: false, Quiet, default, room))
            CollectionAssert.AreEqual(new[] { "chat", "denoiser" }, runner.Loads.ToArray(), "the chat model made way");
        CollectionAssert.AreEqual(new[] { "chat" }, told, "whoever draws hears which residents made way");

        await WaitFor(() => runner.Loads.Count(id => id == "chat") == 2);
        using var chat = await registry.AcquireAsync(Spec("chat", 6), resident: true, Quiet, default);
        Assert.AreEqual(2, runner.Loads.Count(id => id == "chat"), "loaded again after the picture, and held from then on");
    }

    [TestMethod]
    public async Task APicture_WaitsForAReplyInProgress_RatherThanTakingItsModelAway()
    {
        var (registry, runner) = Registry();
        var reply = await registry.AcquireAsync(Spec("chat", 6), resident: true, Quiet, default);

        using var room = registry.BorrowRoom();
        var stages = new List<string>();
        var drawing = registry.AcquireAsync(Spec("denoiser", 9), resident: false, s => { lock (stages) stages.Add(s); return Task.CompletedTask; }, default, room);
        await WaitFor(() => { lock (stages) return stages.Contains("waiting"); });
        Assert.IsFalse(drawing.IsCompleted, "the reply keeps its model");

        reply.Dispose();
        using var denoiser = await drawing;
        CollectionAssert.AreEqual(new[] { "chat", "denoiser" }, runner.Loads.ToArray());
    }

    [TestMethod]
    public async Task WithoutAPicturesRoom_ResidentsNeverMakeWay()
    {
        var (registry, _) = Registry();
        (await registry.AcquireAsync(Spec("chat", 6), resident: true, Quiet, default)).Dispose();

        var refused = await Assert.ThrowsExactlyAsync<HostRequestException>(() => registry.AcquireAsync(Spec("large", 9), resident: false, Quiet, default));
        Assert.AreEqual(HostErrorKind.Refused, refused.Kind);
    }

    [TestMethod]
    public async Task AModelAskedForElsewhere_IsLoadedThere_AndStaysResident()
    {
        var (registry, runner) = Registry();
        (await registry.AcquireAsync(Spec("chat", 6, device: 0), resident: true, Quiet, default)).Dispose();

        (await registry.AcquireAsync(Spec("chat", 6, device: 1), resident: false, Quiet, default)).Dispose();
        using var held = await registry.AcquireAsync(Spec("chat", 6, device: 1), resident: false, Quiet, default);

        Assert.AreEqual(2, runner.Loads.Count, "once on each GPU");
        Assert.AreEqual(1, held.Model.Spec.Device);
        using var beside = await registry.AcquireAsync(Spec("other", 9, device: 0), resident: true, Quiet, default);
        Assert.AreEqual(3, runner.Loads.Count, "GPU 0 has its room back");
    }

    [TestMethod]
    public async Task AGpuTheHostDoesNotHave_IsRefused()
    {
        var (registry, _) = Registry();

        var refused = await Assert.ThrowsExactlyAsync<HostRequestException>(() => registry.AcquireAsync(Spec("chat", 1, device: 2), resident: true, Quiet, default));
        Assert.AreEqual(HostErrorKind.Invalid, refused.Kind);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.IsTrue(condition(), "it came about within five seconds");
    }
}
