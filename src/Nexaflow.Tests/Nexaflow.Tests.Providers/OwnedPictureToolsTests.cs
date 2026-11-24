using System.Text;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Provisioning;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// The picture tools Nexaflow runs in its AI host: Real-ESRGAN and GFPGAN, catalogued as a tool rather than a model, run
/// for a restoration at the scale asked, its faces too where asked, never downloaded unasked.
/// </summary>
[TestClass]
public class OwnedPictureToolsTests
{
    private static MachineCards Nvidia() => new([new MachineCard("gpu", 8L << 30)], CudaRuntimePack.MinimumDriver);

    private static MachineCards NoGpu() => new([], null);

    private static OwnedPictureTools Tools(FakeModelStore store, FakeAiHost host, MachineCards? hardware = null)
        => new(new FakeActivityManager(), store, host, new HostHolds(host), hardware ?? Nvidia());

    private static IReadOnlyList<ModelAsset> Files => Tools(new FakeModelStore(), new FakeAiHost()).Catalog!.Files;

    private static FakeModelStore Here() => new([.. Files, .. CudaRuntimePack.Files]);

    private static FakeAiHost Restoring(params HostReply[] replies) => new()
    {
        Reply = (r, _) => r is RestorePicture p ? [.. replies.Select(reply => reply with { RequestId = p.RequestId }), new Completed(p.RequestId)] : [],
    };

    [TestMethod]
    public void TheShippedTool_IsRealEsrganAndGfpgan_PinnedToItsPublishedGraphs_EachPartUnder8Gb()
    {
        var tool = Tools(new FakeModelStore(), new FakeAiHost());

        Assert.IsTrue(tool.RunsHere);
        Assert.AreEqual("real-esrgan-gfpgan", tool.Catalog!.Id);
        CollectionAssert.AreEquivalent(
            new[] { "upscaler-x2", "upscaler-x4", "face-detector", "face-restorer", "face-parser" },
            Files.Select(f => f.RelativePath.Split('/')[2]).ToArray());
        Assert.IsTrue(Files.All(f => f.Source.AbsoluteUri.Contains("/smile-forge/real-esrgan-gfpgan-onnx/resolve/4311b906")),
                      "every file pinned to the published revision");
        Assert.IsFalse(tool.IsReady, "not until its files are here");
        Assert.IsTrue(Tools(Here(), new FakeAiHost()).IsReady);
    }

    [TestMethod]
    public void OnAMachineWithNoGpu_TheToolsRunOnItsCpu()
    {
        var tool = Tools(new FakeModelStore(Files), new FakeAiHost(), NoGpu());

        Assert.IsTrue(tool.RunsHere, "one set of fp32 graphs serves the CPU and CUDA alike");
        Assert.IsNull(tool.Runtime, "no CUDA pack to fetch");
        Assert.IsTrue(tool.IsReady);
    }

    [TestMethod]
    public async Task APictureEnhancedAtItsSize_IsAskedOfTheToolItsCatalogNames_AtTheJobPriority()
    {
        var host = Restoring(new Progress(0, RestoringStages.Upscaling, 1, 2), new Progress(0, RestoringStages.Upscaling, 2, 2),
                             new PictureRestored(0, 64, 48, 0, SharedBytes.Of(new byte[64 * 48 * 4])));
        var steps = new List<RestoringStep>();
        var picture = new byte[64 * 48 * 4];
        picture[7] = 9;

        using var restored = await Tools(Here(), host).RestoreAsync(picture, 64, 48, 1, faces: false, new Inline<RestoringStep>(steps.Add), CancellationToken.None);

        var request = (RestorePicture)host.Requests.Single();
        Assert.AreEqual((64, 48, 1, false, RequestPriority.Job), (request.Width, request.Height, request.Scale, request.Faces, request.Priority));
        CollectionAssert.AreEqual(picture, request.Pixels.ToArray(), "the picture goes to the host as it was given");
        Assert.AreEqual("real-esrgan-gfpgan", request.Tool, "the tool by the catalog's id: which upscaler the scale takes is the host's to choose");
        Assert.AreEqual((64, 48, 0, 64 * 48 * 4), (restored.Width, restored.Height, restored.FacesRestored, restored.Pixels.Length));
        CollectionAssert.AreEqual(new[] { 1, 2 }, steps.Select(s => s.Done).ToArray());
    }

    [TestMethod]
    public async Task APictureUpscaledWithItsFaces_AsksForFourTimesItsSize_AndItsFaces()
    {
        var host = Restoring(new PictureRestored(0, 256, 192, 2, SharedBytes.Of(new byte[256 * 192 * 4])));

        using var restored = await Tools(Here(), host).RestoreAsync(new byte[64 * 48 * 4], 64, 48, 4, faces: true, null, CancellationToken.None);

        var request = (RestorePicture)host.Requests.Single();
        Assert.AreEqual(("real-esrgan-gfpgan", 4, true), (request.Tool, request.Scale, request.Faces), "the parts that takes are the host's to choose");
        Assert.AreEqual(2, restored.FacesRestored);
    }

    [TestMethod]
    public async Task ToolsNotYetHere_AreNotDownloadedUnasked()
    {
        var host = Restoring(new PictureRestored(0, 64, 48, 0, SharedBytes.Of(new byte[64 * 48 * 4])));
        var store = new FakeModelStore();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            Tools(store, host).RestoreAsync(new byte[64 * 48 * 4], 64, 48, 2, false, null, CancellationToken.None));

        Assert.AreEqual(0, host.Requests.Count);
        Assert.AreEqual(0, store.Fetched.Count, "a download is the person's to start");
    }

    [TestMethod]
    public async Task AHostThatCannotRestore_FailsAsTheTool()
    {
        var host = new FakeAiHost { Reply = (r, _) => [new Failed(r.RequestId, HostErrorKind.Refused, "not enough room on the GPU")] };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            Tools(Here(), host).RestoreAsync(new byte[64 * 48 * 4], 64, 48, 2, false, null, CancellationToken.None));

        StringAssert.Contains(ex.Message, "not enough room on the GPU");
    }

    [TestMethod]
    public void ATool_LackingAPartItsKindNeeds_IsRefused()
    {
        var json = Encoding.UTF8.GetBytes("""
            { "tools": [ { "id": "t", "kind": "pictureRestoration",
                           "parts": [ { "role": "upscalerX2", "runner": "esrgan-upscaler", "folder": "tools/t/x2", "file": "x2.onnx", "memoryBytes": 1 } ],
                           "files": [ { "path": "tools/t/x2/x2.onnx", "url": "https://example.com/x2.onnx", "size": 1,
                                        "sha256": "0000000000000000000000000000000000000000000000000000000000000000" } ] } ] }
            """);

        var ex = Assert.ThrowsExactly<InvalidDataException>(() => HostCatalog.Read(json));

        StringAssert.Contains(ex.Message, "Tool 't' needs one part each of upscalerX2, upscalerX4, faceDetector, faceRestorer, faceParser");
    }

    /// <summary>Progress reported at once, on the reporting thread.</summary>
    private sealed class Inline<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
