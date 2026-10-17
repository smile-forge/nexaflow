using System.Text;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// Page reading in the AI host: Unlimited-OCR read as its makers decode it, and PP-OCRv5's text finder on the CPU however
/// large the GPU — catalogued as a tool, never downloaded unasked.
/// </summary>
[TestClass]
public class OwnedPageReadingTests
{
    private static ComputeHardwareReport Nvidia() => new([new ComputeAdapter("gpu", ComputeVendor.Nvidia, 16L << 30, IsIntegrated: false, IsNpu: false)], CudaRuntimePack.MinimumDriver);

    private static OwnedPageReading Reading(FakeModelStore store, FakeAiHost host)
        => new(new FakeActivityManager(), store, host, new HostModelSetup(host), Nvidia());

    private static IReadOnlyList<ModelAsset> Files => Reading(new FakeModelStore(), new FakeAiHost()).Catalog!.Files;

    private static FakeModelStore Here() => new([.. Files, .. CudaRuntimePack.Files]);

    private static FakeAiHost Answering(Func<HostRequest, HostReply[]> replies) => new()
    {
        Reply = (r, _) => [.. replies(r), new Completed(r.RequestId)],
    };

    [TestMethod]
    public void TheShippedTool_IsUnlimitedOcrAndItsTextFinder_TheReaderUnder8Gb_TheFinderOnTheCpu()
    {
        var reading = Reading(new FakeModelStore(), new FakeAiHost());
        var tool = OwnedModels.Tools.Single(t => t.Kind == OwnedToolKind.PageReading);

        Assert.IsTrue(reading.RunsHere);
        Assert.AreEqual("unlimited-ocr", reading.Catalog!.Id);
        var reader = tool.Part(ReadingParts.Reader, OwnedModels.Cuda);
        Assert.AreEqual(("llama-chat", "unlimited-ocr-mmproj-f16.gguf"), (reader.Runner, reader.Projector));
        Assert.IsTrue(reader.MemoryBytes is > 0 and < 8L << 30, "the reader runs on a card with 8 GB free");
        Assert.AreEqual(0, tool.Part(ReadingParts.TextFinder, OwnedModels.Cuda).MemoryBytes, "the finder holds no GPU memory");
        Assert.IsTrue(Files.All(f => f.Source.AbsoluteUri.Contains("/smile-forge/unlimited-ocr/resolve/81bd5e5e")), "every file pinned to the published revision");
        Assert.IsFalse(reading.IsReady, "not until its files are here");
        Assert.IsTrue(Reading(Here(), new FakeAiHost()).IsReady);
    }

    [TestMethod]
    public async Task TextIsFound_OnTheCpu_EvenWhereTheGpuHasRoom()
    {
        var host = Answering(r => r is FindTextInPicture find ? [new TextFoundInPicture(find.RequestId, [new TextRegion(0.1f, 0.2f, 0.3f, 0.02f, 0.9f)])] : []);

        var regions = await Reading(Here(), host).FindTextAsync(@"C:\page.rgba", 640, 480, CancellationToken.None);

        var request = (FindTextInPicture)host.Requests.Single();
        Assert.AreEqual((@"C:\page.rgba", 640, 480), (request.Pixels, request.Width, request.Height));
        Assert.AreEqual("text-finder", request.Finder.Runner);
        Assert.IsNull(request.Finder.Device, "the CPU, not the GPU");
        Assert.AreEqual(0.9f, regions.Single().Score);
    }

    [TestMethod]
    public async Task APage_IsReadAsItsMakersDecodeIt_TheBlockMarksKept_ItsTextStreamed()
    {
        var host = Answering(r => r is Generate g
            ? [new TextDelta(g.RequestId, "<|det|>title [1, 2, 3, 4]<|/det|>"), new TextDelta(g.RequestId, "Introduction"),
               new GenerationEnded(g.RequestId, StopReason.EndOfTurn, 900, 900, 3)]
            : []);
        var pieces = new List<string>();

        string read = await Reading(Here(), host).ReadPageAsync(@"C:\page.png", waiting: true, new Inline<string>(pieces.Add), CancellationToken.None);

        var request = (Generate)host.Requests.Single();
        var turn = request.Turns.Single();
        Assert.AreEqual((TurnRole.User, OwnedPageReading.Instruction, @"C:\page.png"), (turn.Role, turn.Text, turn.Media!.Single().Path));
        Assert.AreEqual(new ReplyDecoding(128, new RepeatBan(35, 128), WritesControlTokens: true), request.Decoding);
        Assert.IsNull(request.Sampling, "greedy, as the makers decode");
        Assert.IsFalse(request.Thinks);
        Assert.AreEqual(RequestPriority.Interactive, request.Priority, "a person is waiting for it");
        Assert.AreEqual("unlimited-ocr-q8_0.gguf", request.Model.File);
        Assert.AreEqual("<|det|>title [1, 2, 3, 4]<|/det|>Introduction", read);
        CollectionAssert.AreEqual(new[] { "<|det|>title [1, 2, 3, 4]<|/det|>", "Introduction" }, pieces);
    }

    [TestMethod]
    public async Task APageReadForNobodyWaiting_GoesAtTheJobPriority()
    {
        var host = Answering(r => r is Generate g ? [new GenerationEnded(g.RequestId, StopReason.EndOfTurn, 1, 1, 1)] : []);

        await Reading(Here(), host).ReadPageAsync(@"C:\page.png", waiting: false, null, CancellationToken.None);

        Assert.AreEqual(RequestPriority.Job, ((Generate)host.Requests.Single()).Priority);
    }

    [TestMethod]
    public async Task PageReadingNotYetHere_IsNotDownloadedUnasked()
    {
        var host = new FakeAiHost();
        var store = new FakeModelStore();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Reading(store, host).ReadPageAsync(@"C:\page.png", true, null, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Reading(store, host).FindTextAsync(@"C:\page.rgba", 64, 64, CancellationToken.None));

        Assert.AreEqual(0, host.Requests.Count);
        Assert.AreEqual(0, store.Fetched.Count, "a download is the person's to start");
    }

    [TestMethod]
    public void APartThatRunsOnTheCpuEverywhere_HoldsNoGpuMemory()
    {
        var json = Encoding.UTF8.GetBytes("""
            { "tools": [ { "id": "t", "kind": "pageReading",
                           "parts": [ { "role": "reader", "runner": "llama-chat", "folder": "tools/t/reader", "file": "r.gguf", "projector": "p.gguf", "memoryBytes": 1 },
                                      { "role": "textFinder", "runner": "text-finder", "folder": "tools/t/finder", "file": "f.onnx", "runsOnCpu": true, "memoryBytes": 5 } ],
                           "files": [ { "path": "tools/t/reader/r.gguf", "url": "https://example.com/r.gguf", "size": 1, "sha256": "0000000000000000000000000000000000000000000000000000000000000000" },
                                      { "path": "tools/t/finder/f.onnx", "url": "https://example.com/f.onnx", "size": 1, "sha256": "0000000000000000000000000000000000000000000000000000000000000000" } ] } ] }
            """);

        var ex = Assert.ThrowsExactly<InvalidDataException>(() => OwnedModels.ReadTools(json));

        StringAssert.Contains(ex.Message, "runs its textFinder on the CPU everywhere, so it holds no GPU memory");
    }

    private sealed class Inline<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
