using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Provisioning;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// Scene depth in the AI host: Depth Anything V2 Small — the Apache-2.0 model alone — catalogued as a tool pinned to its
/// published graph, peaking under 3 GB so a 4 GB card runs it, on the CPU where there is no GPU, and never downloaded by the
/// front itself.
/// </summary>
[TestClass]
public class OwnedSceneDepthTests
{
    private static MachineCards Nvidia(long memory = 16L << 30) => new([new MachineCard("gpu", memory)], CudaRuntimePack.MinimumDriver);

    private static MachineCards NoGpu() => new([], null);

    private static OwnedSceneDepth Depth(FakeModelStore store, FakeAiHost host, MachineCards? hardware = null)
        => new(new FakeActivityManager(), store, host, new HostHolds(host), hardware ?? Nvidia());

    private static IReadOnlyList<ModelAsset> Files => Depth(new FakeModelStore(), new FakeAiHost()).Catalog!.Files;

    private static FakeModelStore Here() => new([.. Files, .. CudaRuntimePack.Files]);

    [TestMethod]
    public void TheShippedTool_IsDepthAnythingV2Small_PinnedToItsPublishedGraph_Under3Gb()
    {
        var tool = OwnedModels.Tools.Single(t => t.Kind == HostedToolKind.SceneDepth);
        var estimator = tool.Part(SceneDepthParts.Estimator, OwnedModels.Cuda);

        Assert.AreEqual(("depth-anything-v2-small", "scene-depth-estimator"), (tool.Id, estimator.Runner));
        Assert.IsTrue(estimator.MemoryBytes is > 0 and < 3L << 30, "it peaks under 3 GB, so a 4 GB card runs it");
        Assert.IsTrue(Files.All(f => f.Source.AbsoluteUri.Contains("/smile-forge/depth-anything-v2-small-onnx/resolve/9d4c581b")), "pinned to its published commit");
        Assert.IsTrue(Files.Any(f => f.RelativePath == "tools/depth-anything-v2-small/estimator/LICENSE"), "the makers' licence comes with the weights");
        Assert.IsFalse(Files.Any(f => f.Source.AbsoluteUri.Contains("Base", StringComparison.OrdinalIgnoreCase) || f.Source.AbsoluteUri.Contains("Large", StringComparison.OrdinalIgnoreCase)),
                       "only the Apache-2.0 Small model, never the non-commercial Base or Large");
        Assert.IsTrue(Depth(new FakeModelStore(), new FakeAiHost(), NoGpu()).RunsHere, "a machine with no GPU estimates depth on its CPU");
        Assert.IsTrue(Depth(new FakeModelStore(), new FakeAiHost(), Nvidia(4L << 30)).RunsHere, "a 4 GB card runs it");
        Assert.IsFalse(Depth(new FakeModelStore(), new FakeAiHost()).IsReady, "not until its files are here");
        Assert.IsTrue(Depth(Here(), new FakeAiHost()).IsReady);
    }

    [TestMethod]
    public async Task APicture_IsHandedOver_AndItsDepthGivenBackAtItsWorkingSize()
    {
        var disparity = SharedBytes.Of(new byte[784 * 518 * sizeof(float)]);
        var host = new FakeAiHost
        {
            Reply = (r, _) => r is EstimateSceneDepth estimate ? [new SceneDepthEstimated(estimate.RequestId, 784, 518, disparity), new Completed(estimate.RequestId)] : [],
        };
        byte[] picture = [1, 2, 3, 4, 5, 6, 7, 8];

        using var estimated = await Depth(Here(), host).EstimateAsync(picture, 2, 1, CancellationToken.None);

        var request = (EstimateSceneDepth)host.Requests.Single();
        Assert.AreEqual((2, 1), (request.Width, request.Height));
        CollectionAssert.AreEqual(picture, request.Pixels.ToArray(), "the picture goes to the host as it was given");
        Assert.AreEqual("depth-anything-v2-small", request.Tool, "the tool by the catalog's id: its estimator, and where it runs, are the host's to take from it");
        Assert.AreEqual((784, 518), (estimated.Width, estimated.Height));
        Assert.AreSame(disparity, estimated.Disparity, "the host's disparity, as it sent it");
    }

    [TestMethod]
    public async Task AnEstimateBeforeTheToolIsHere_IsRefused_NotDownloadedByTheFront()
    {
        var store = new FakeModelStore();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Depth(store, new FakeAiHost()).EstimateAsync(new byte[4], 1, 1, CancellationToken.None));
        Assert.AreEqual(0, store.Fetched.Count);
    }
}
