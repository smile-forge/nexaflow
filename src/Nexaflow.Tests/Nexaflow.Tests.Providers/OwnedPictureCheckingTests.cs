using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// Picture checking in the AI host: OwenElliott's image safety classifier, M, on the CPU however large the GPU,
/// catalogued as a tool pinned to its author's published graph, and never downloaded by the front itself.
/// </summary>
[TestClass]
public class OwnedPictureCheckingTests
{
    private static ComputeHardwareReport Nvidia() => new([new ComputeAdapter("gpu", ComputeVendor.Nvidia, 16L << 30, IsIntegrated: false, IsNpu: false)], CudaRuntimePack.MinimumDriver);

    private static ComputeHardwareReport NoGpu() => new([], null);

    private static OwnedPictureChecking Checking(FakeModelStore store, FakeAiHost host, ComputeHardwareReport? hardware = null)
        => new(new FakeActivityManager(), store, host, new HostModelSetup(host), hardware ?? Nvidia());

    private static IReadOnlyList<ModelAsset> Files => Checking(new FakeModelStore(), new FakeAiHost()).Catalog!.Files;

    private static FakeModelStore Here() => new([.. Files, .. CudaRuntimePack.Files]);

    [TestMethod]
    public void TheShippedTool_IsTheMClassifier_OnTheCpu_PinnedToItsAuthorsGraph()
    {
        var tool = OwnedModels.Tools.Single(t => t.Kind == OwnedToolKind.PictureChecking);
        var classifier = tool.Part(CheckingParts.SafetyClassifier, OwnedModels.Cuda);

        Assert.AreEqual(("image-safety-classifier-m", "picture-safety-classifier"), (tool.Id, classifier.Runner));
        Assert.AreEqual(0, classifier.MemoryBytes, "it holds no GPU memory");
        var file = Files.Single();
        StringAssert.Contains(file.Source.AbsoluteUri, "/OwenElliott/image-safety-classifier-m/resolve/a5ce9eec");
        Assert.IsTrue(Checking(new FakeModelStore(), new FakeAiHost(), NoGpu()).RunsHere, "a machine with no GPU checks pictures too");
        Assert.IsFalse(Checking(new FakeModelStore(), new FakeAiHost()).IsReady, "not until its file is here");
    }

    [TestMethod]
    public async Task APicture_IsCheckedOnTheCpu_AsItWasEncoded_AndItsVerdictGivenBack()
    {
        var host = new FakeAiHost
        {
            Reply = (r, _) => r is CheckPicture check ? [new PictureChecked(check.RequestId, 0.05f, 0.6f, 0.35f), new Completed(check.RequestId)] : [],
        };

        var verdict = await Checking(Here(), host).CheckAsync([0x89, 0x50, 0x4E, 0x47], CancellationToken.None);

        var request = (CheckPicture)host.Requests.Single();
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, request.Picture, "the picture goes as it was encoded");
        Assert.IsNull(request.Classifier.Device, "the CPU, not the GPU");
        Assert.AreEqual(new PictureVerdict(0.05f, 0.6f, 0.35f), verdict);
    }

    [TestMethod]
    public async Task ACheckBeforeTheToolIsHere_IsRefused_NotDownloadedByTheFront()
    {
        var store = new FakeModelStore();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Checking(store, new FakeAiHost()).CheckAsync([1], CancellationToken.None));
        Assert.AreEqual(0, store.Fetched.Count);
    }
}
