using System.Text;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// The picture tools Nexaflow runs in its AI host: Real-ESRGAN and GFPGAN, catalogued as a tool rather than a model, run
/// for a restoration with the parts its scale and faces need, never downloaded unasked.
/// </summary>
[TestClass]
public class OwnedPictureToolsTests
{
    private static ComputeHardwareReport Nvidia() => new([new ComputeAdapter("gpu", ComputeVendor.Nvidia, 8L << 30, IsIntegrated: false, IsNpu: false)], CudaRuntimePack.MinimumDriver);

    private static ComputeHardwareReport NoGpu() => new([], null);

    private static OwnedPictureTools Tools(FakeModelStore store, FakeAiHost host, ComputeHardwareReport? hardware = null)
        => new(new FakeActivityManager(), store, host, new HostModelSetup(host), hardware ?? Nvidia());

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
    public async Task APictureEnhancedAtItsSize_UsesThe2xUpscalerAlone_AtTheJobPriority()
    {
        var host = Restoring(new Progress(0, RestoringStages.Upscaling, 1, 2), new Progress(0, RestoringStages.Upscaling, 2, 2), new PictureRestored(0, 64, 48, 0));
        var steps = new List<RestoringStep>();

        var restored = await Tools(Here(), host).RestoreAsync(@"C:\in.rgba", 64, 48, 1, faces: false, @"C:\out.rgba", new Inline<RestoringStep>(steps.Add), CancellationToken.None);

        var request = (RestorePicture)host.Requests.Single();
        Assert.AreEqual((@"C:\in.rgba", 64, 48, 1, false, @"C:\out.rgba", RequestPriority.Job),
                        (request.Pixels, request.Width, request.Height, request.Scale, request.Faces, request.Into, request.Priority));
        CollectionAssert.AreEqual(new[] { RestoringParts.UpscalerX2 }, request.Parts.Keys.ToArray());
        Assert.AreEqual("esrgan-upscaler", request.Parts[RestoringParts.UpscalerX2].Runner);
        Assert.AreEqual(new RestoredPicture(64, 48, 0), restored);
        CollectionAssert.AreEqual(new[] { 1, 2 }, steps.Select(s => s.Done).ToArray());
    }

    [TestMethod]
    public async Task APictureUpscaledWithItsFaces_UsesThe4xUpscaler_AndEveryFacePart()
    {
        var host = Restoring(new PictureRestored(0, 256, 192, 2));

        var restored = await Tools(Here(), host).RestoreAsync(@"C:\in.rgba", 64, 48, 4, faces: true, @"C:\out.rgba", null, CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { RestoringParts.UpscalerX4, RestoringParts.FaceDetector, RestoringParts.FaceRestorer, RestoringParts.FaceParser },
                                       ((RestorePicture)host.Requests.Single()).Parts.Keys.ToArray());
        Assert.AreEqual(2, restored.FacesRestored);
    }

    [TestMethod]
    public async Task ToolsNotYetHere_AreNotDownloadedUnasked()
    {
        var host = Restoring(new PictureRestored(0, 64, 48, 0));
        var store = new FakeModelStore();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            Tools(store, host).RestoreAsync(@"C:\in.rgba", 64, 48, 2, false, @"C:\out.rgba", null, CancellationToken.None));

        Assert.AreEqual(0, host.Requests.Count);
        Assert.AreEqual(0, store.Fetched.Count, "a download is the person's to start");
    }

    [TestMethod]
    public async Task AHostThatCannotRestore_FailsAsTheTool()
    {
        var host = new FakeAiHost { Reply = (r, _) => [new Failed(r.RequestId, HostErrorKind.Refused, "not enough room on the GPU")] };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            Tools(Here(), host).RestoreAsync(@"C:\in.rgba", 64, 48, 2, false, @"C:\out.rgba", null, CancellationToken.None));

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

        var ex = Assert.ThrowsExactly<InvalidDataException>(() => OwnedModels.ReadTools(json));

        StringAssert.Contains(ex.Message, "Owned tool 't' needs one part each of upscalerX2, upscalerX4, faceDetector, faceRestorer, faceParser");
    }

    /// <summary>Progress reported at once, on the reporting thread.</summary>
    private sealed class Inline<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
