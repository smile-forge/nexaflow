using System.Runtime.CompilerServices;
using System.Text;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// "Nexaflow" — a model run in the AI host — as the app sees it: the conversations it sends, the replies it streams,
/// how it fails, and fetching what it needs.
/// </summary>
[TestClass]
public class OwnedLlmProviderTests
{
    private const string Root = @"C:\ai\";

    /// <summary>Files are here once fetched; a store set up with <c>present</c> has them already.</summary>
    private sealed class Store(params IEnumerable<ModelAsset> present) : IModelAssetStore
    {
        public readonly HashSet<ModelAsset> Here = [.. present];
        public readonly List<IReadOnlyList<ModelAsset>> Fetched = [];
        public event EventHandler<ModelAsset>? Changed;
        public string? Locate(ModelAsset asset) => Here.Contains(asset) ? Root + asset.RelativePath.Replace('/', '\\') : null;
        public Task<string> EnsureAsync(ModelAsset asset, IProgress<AssetProgress>? progress = null, CancellationToken ct = default)
            => Task.FromResult(EnsureAllAsync([asset], progress, ct).Result[0]);
        public Task<IReadOnlyList<string>> EnsureAllAsync(IReadOnlyList<ModelAsset> assets, IProgress<AssetProgress>? progress = null, CancellationToken ct = default)
        {
            Fetched.Add(assets);
            foreach (var asset in assets) { Here.Add(asset); Changed?.Invoke(this, asset); }
            return Task.FromResult<IReadOnlyList<string>>([.. assets.Select(a => Locate(a)!)]);
        }
    }

    private static readonly CatalogModel Gemma = new OwnedLlmProvider(new FakeActivityManager(), new Store(), new FakeAiHost(), Hardware(), new ProviderModel("x"))
        .Catalog.Single(m => m.Id == "gemma-4-e4b-it");

    private static ComputeHardwareReport Hardware(ComputeVendor vendor = ComputeVendor.Amd, Version? cuda = null)
        => new([new ComputeAdapter("gpu", vendor, 24L << 30, IsIntegrated: false, IsNpu: false)], cuda);

    private static OwnedLlmProvider Provider(Store store, FakeAiHost host, ComputeHardwareReport? hardware = null)
        => new(new FakeActivityManager(), store, host, hardware ?? Hardware(), new ProviderModel("gemma-4-e4b-it"));

    private static async Task<string> ReadAsync(IAsyncEnumerable<LlmStreamEvent> events)
    {
        var text = new StringBuilder();
        await foreach (var e in events) if (e is LlmTextDelta d) text.Append(d.Text);
        return text.ToString();
    }

    [TestMethod]
    public void ItIsNexaflowsOwnHarness_AndNothingLeavesTheMachine()
    {
        var provider = Provider(new Store(), new FakeAiHost());

        Assert.AreEqual(("Nexaflow", ModelHarness.Owned, DataResidency.StaysOnMachine), (provider.Name, provider.Harness, provider.Residency));
        Assert.IsTrue(provider.SupportsImages);
    }

    [TestMethod]
    public void TheModel_IsAvailableOnceItsFilesAreHere()
    {
        var store = new Store();
        var provider = Provider(store, new FakeAiHost());
        int changes = 0;
        provider.AvailabilityChanged += (_, _) => changes++;
        Assert.IsFalse(provider.IsAvailable);

        store.EnsureAllAsync(Gemma.Files).Wait();

        Assert.IsTrue(provider.IsAvailable);
        Assert.AreEqual(Gemma.Files.Count, changes);
    }

    [TestMethod]
    public async Task AConversation_GoesToTheHostWhole_AndItsReplyStreamsBack()
    {
        var host = new FakeAiHost();
        var provider = Provider(new Store(Gemma.Files), host);
        var image = new LlmAttachment(@"C:\shot.png", "image/png");
        var notes = new LlmAttachment(@"C:\notes.txt", "text/plain");

        var reply = await ReadAsync(provider.StreamAsync(
        [
            new(LlmRole.System, "Be brief."),
            new(LlmRole.User, "What failed?") { Attachments = [image, notes] },
        ]));

        Assert.AreEqual("Hello there.", reply);
        var generate = (Generate)host.Requests.Single();
        Assert.AreEqual(RequestPriority.Interactive, generate.Priority);
        Assert.AreEqual(@"C:\ai\models\gemma-4-e4b-it", generate.Model.Folder, "the folder is where the store keeps its files");
        Assert.AreEqual(TurnRole.System, generate.Turns[0].Role);
        Assert.AreEqual(@"C:\shot.png", generate.Turns[1].Media!.Single().Path, "an image the model reads goes as an image");
        StringAssert.Contains(generate.Turns[1].Text, @"[attached: C:\notes.txt]", "anything else is named");
    }

    [TestMethod]
    public async Task BackgroundWork_AsksForTheBackgroundPriority()
    {
        var host = new FakeAiHost();
        var provider = Provider(new Store(Gemma.Files), host);

        await provider.CompleteAsync([new(LlmRole.User, "Analyse this page.")], LlmUrgency.Background);

        Assert.AreEqual(RequestPriority.Background, ((Generate)host.Requests.Single()).Priority);
    }

    [TestMethod]
    public async Task ARequestTheHostRefuses_IsAProviderFailure()
    {
        var host = new FakeAiHost { Reply = (r, _) => throw new AiHostException(AiHostFailure.Request, "over budget", HostErrorKind.Refused) };
        var provider = Provider(new Store(Gemma.Files), host);

        var ex = await Assert.ThrowsExactlyAsync<LlmProviderException>(() => ReadAsync(provider.StreamAsync([new(LlmRole.User, "Hi")])));
        StringAssert.Contains(ex.Message, "over budget");
    }

    [TestMethod]
    public async Task AHostLostBeforeAWord_IsAskedOnceMore()
    {
        var host = new FakeAiHost();
        var answer = host.Reply;
        host.Reply = (r, attempt) => attempt == 0 ? throw new AiHostException(AiHostFailure.Lost, "crashed") : answer(r, attempt);
        var provider = Provider(new Store(Gemma.Files), host);

        Assert.AreEqual("Hello there.", await ReadAsync(provider.StreamAsync([new(LlmRole.User, "Hi")])));
        Assert.AreEqual(2, host.Requests.Count);
    }

    [TestMethod]
    public async Task AHostLostPartWay_IsNotAskedAgain_TheReaderHasSeenPartOfTheReply()
    {
        var host = new FakeAiHost();
        host.Reply = (r, _) => LostAfterAWord(r.RequestId);
        var provider = Provider(new Store(Gemma.Files), host);

        await Assert.ThrowsExactlyAsync<LlmProviderException>(() => ReadAsync(provider.StreamAsync([new(LlmRole.User, "Hi")])));
        Assert.AreEqual(1, host.Requests.Count);

        static IEnumerable<HostReply> LostAfterAWord(long id)
        {
            yield return new TextDelta(id, "Hel");
            throw new AiHostException(AiHostFailure.Lost, "crashed");
        }
    }

    [TestMethod]
    public async Task WarmingUp_FetchesTheModelAndTheCudaPack_WhereThePackSuitsTheMachine_ThenLoadsIt()
    {
        var store = new Store();
        var host = new FakeAiHost();
        var provider = Provider(store, host, Hardware(ComputeVendor.Nvidia, new Version(13, 0)));

        await provider.WarmupAsync();

        CollectionAssert.IsSubsetOf(CudaRuntimePack.Files.ToArray(), store.Fetched.Single().ToArray());
        Assert.IsNotNull(host.Environment.RuntimePack, "the host runs from the pack");
        Assert.IsInstanceOfType<LoadModel>(host.Requests.Single());
    }

    [TestMethod]
    public async Task OnAMachineThePackDoesNotSuit_OnlyTheModelIsFetched_AndItRunsOnTheCpu()
    {
        var store = new Store();
        var host = new FakeAiHost();
        var provider = Provider(store, host, Hardware(ComputeVendor.Amd));

        await provider.WarmupAsync();

        CollectionAssert.AreEquivalent(Gemma.Files.ToArray(), store.Fetched.Single().ToArray());
        Assert.AreEqual(AiHostEnvironment.Cpu, host.Environment);
    }

    [TestMethod]
    public async Task ARecording_GoesToTheModelAsAudio()
    {
        var host = new FakeAiHost();
        var provider = Provider(new Store(Gemma.Files), host);

        await ReadAsync(provider.StreamAsync([new(LlmRole.User, "What is said?") { Attachments = [new LlmAttachment("clip.wav", "audio/wav", [1, 2])] }]));

        var media = ((Generate)host.Requests.Single()).Turns[0].Media!.Single();
        Assert.AreEqual(MediaKind.Audio, media.Kind);
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, media.Bytes);
        Assert.IsTrue(provider.SupportsAudio);
    }
}
