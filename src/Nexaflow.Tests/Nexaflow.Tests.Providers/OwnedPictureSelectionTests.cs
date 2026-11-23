using Nexaflow.AiHost.Contracts;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Providers.Support;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Provisioning;
using Nexaflow.AiHost.Catalog;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// Picture selection's front (<see cref="OwnedPictureSelection"/>): SAM 3.1 pinned to its published graphs, a session
/// that reads its picture once at interactive priority, named to the host by the tool's id, each ask answered by the reply of its own
/// turn, and a session the host ends telling whatever still waits why.
/// </summary>
[TestClass]
public class OwnedPictureSelectionTests
{
    private static MachineCards Nvidia() => new([new MachineCard("gpu", 8L << 30)], CudaRuntimePack.MinimumDriver);

    private static OwnedPictureSelection Selection(FakeModelStore store, FakeAiHost host)
        => new(new FakeActivityManager(), store, host, new HostHolds(host), Nvidia());

    private static IReadOnlyList<ModelAsset> Files => Selection(new FakeModelStore(), new FakeAiHost()).Catalog!.Files;

    private static FakeModelStore Here() => new([.. Files, .. CudaRuntimePack.Files]);

    private static readonly MaskTarget Mask = new(16, 12);

    private static readonly byte[] Picture = new byte[640 * 480 * 4];

    /// <summary>A host that reads a picture and answers each click with the box of its turn.</summary>
    private static FakeAiHost Reading() => new()
    {
        Reply = (r, _) => r is SelectInPicture s ? [new PictureEncoded(s.RequestId)] : [],
        Answer = i => i is SelectByPoints p ? [new PointsSelected(p.RequestId, p.Turn, new SelectionBox(p.Turn, 0, 10, 10), SharedBytes.Of(new byte[16 * 12]))] : [],
    };

    [TestMethod]
    public void TheShippedTool_IsSam31_PinnedToItsPublishedGraphs_EachPartUnder8Gb()
    {
        var selection = Selection(new FakeModelStore(), new FakeAiHost());

        Assert.IsTrue(selection.RunsHere);
        Assert.AreEqual("sam3.1", selection.Catalog!.Id);
        CollectionAssert.AreEquivalent(
            new[] { "image-encoder", "image-encoder", "image-encoder", "click-decoder", "text-encoder", "text-encoder", "exemplar-encoder", "finder" },
            Files.Select(f => f.RelativePath.Split('/')[2]).ToArray());
        Assert.IsTrue(Files.All(f => f.Source.AbsoluteUri.Contains("/smile-forge/sam3.1-onnx/resolve/df0b5cbe")), "every file pinned to the published revision");
        Assert.IsTrue(Files.Any(f => f.RelativePath == "tools/sam3.1/text-encoder/bpe_simple_vocab_16e6.txt.gz"),
                      "the vocabulary lies beside the text encoder, where its runner reads it");
        Assert.IsTrue(Files.Any(f => f.RelativePath == "tools/sam3.1/image-encoder/LICENSE"), "the SAM License comes with the weights, as it asks");
        Assert.IsTrue(OwnedModels.Tools.Single(t => t.Kind == HostedToolKind.PictureSelection).Parts.All(p => p.MemoryBytes < 8L << 30));
        Assert.IsFalse(selection.IsReady, "not until its files are here");
        Assert.IsTrue(Selection(Here(), new FakeAiHost()).IsReady);
    }

    [TestMethod]
    public async Task ASession_ReadsItsPictureOnce_Interactively_ByTheToolItsCatalogNames_AndEndsItsRequestWhenDisposed()
    {
        var host = Reading();
        var selection = Selection(Here(), host);

        await using (var session = await selection.OpenAsync(Picture, 640, 480, CancellationToken.None))
        {
            var first = await session.SelectAsync([new SelectionPoint(10, 20, SelectionPointKind.Include)], Mask, CancellationToken.None);
            var second = await session.SelectAsync([new SelectionPoint(10, 20, SelectionPointKind.Include), new SelectionPoint(30, 40, SelectionPointKind.Exclude)],
                                                   Mask, CancellationToken.None);
            Assert.AreEqual((1, 2), (first.Turn, second.Turn));
        }

        var request = (SelectInPicture)host.Requests.Single();
        Assert.AreEqual(RequestPriority.Interactive, request.Priority);
        Assert.AreEqual("sam3.1", request.Tool, "the tool by the catalog's id: its parts, and where each runs, are the host's to take from it");
        Assert.AreEqual((640, 480, 640 * 480 * 4), (request.Width, request.Height, request.Pixels.Length));
        Assert.IsInstanceOfType<InputEnd>(host.Inputs.Last(), "disposing the session ends its request, and the host lets the picture go");
    }

    [TestMethod]
    public async Task EachAsk_HearsTheAnswerOfItsOwnTurn_WhicheverComesFirst()
    {
        var host = Reading();
        host.Answer = i => i is SelectByPoints { Turn: 2 } p
            ? [new PointsSelected(p.RequestId, 2, new SelectionBox(2, 0, 10, 10), SharedBytes.Of(new byte[16 * 12])),
               new PointsSelected(p.RequestId, 1, new SelectionBox(1, 0, 10, 10), SharedBytes.Of(new byte[16 * 12]))]
            : [];
        await using var session = await Selection(Here(), host).OpenAsync(Picture, 640, 480, CancellationToken.None);

        var first = session.SelectAsync([new SelectionPoint(1, 1, SelectionPointKind.Include)], Mask, CancellationToken.None);
        var second = session.SelectAsync([new SelectionPoint(2, 2, SelectionPointKind.Include)], Mask, CancellationToken.None);

        Assert.AreEqual(1f, (await first).Bounds!.Left);
        Assert.AreEqual(2f, (await second).Bounds!.Left);
    }

    [TestMethod]
    public async Task ASessionTheHostEnds_TellsTheAskWaitingWhy_AndRefusesMore()
    {
        var host = Reading();
        host.Answer = i => i is SelectByPhrase f ? [new Failed(f.RequestId, HostErrorKind.Invalid, "A search needs a phrase or an example.")] : [];
        await using var session = await Selection(Here(), host).OpenAsync(Picture, 640, 480, CancellationToken.None);

        var failed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => session.FindAsync("", [], Mask, CancellationToken.None));
        StringAssert.Contains(failed.Message, "needs a phrase");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => session.SelectAsync([new SelectionPoint(1, 1, SelectionPointKind.Include)], Mask, CancellationToken.None));
    }

    [TestMethod]
    public async Task ToolsNotYetHere_AreNotOpened_NorDownloaded()
    {
        var store = new FakeModelStore();
        var host = Reading();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Selection(store, host).OpenAsync(Picture, 640, 480, CancellationToken.None));
        Assert.AreEqual(0, store.Fetched.Count);
        Assert.AreEqual(0, host.Requests.Count);
    }
}
