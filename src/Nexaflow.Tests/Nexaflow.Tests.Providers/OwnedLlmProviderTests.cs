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
    private static readonly CatalogModel Gemma = Provider(new FakeModelStore(), new FakeAiHost()).Catalog.Single(m => m.Id == "gemma-4-e4b-it");

    private static ComputeHardwareReport Hardware(ComputeVendor vendor = ComputeVendor.Amd, Version? cuda = null)
        => new([new ComputeAdapter("gpu", vendor, 24L << 30, IsIntegrated: false, IsNpu: false)], cuda);

    private static ComputeHardwareReport Nvidia() => Hardware(ComputeVendor.Nvidia, CudaRuntimePack.MinimumDriver);

    private static OwnedLlmProvider Provider(FakeModelStore store, FakeAiHost host, ComputeHardwareReport? hardware = null)
        => new(new FakeActivityManager(), store, host, new HostModelSetup(host), hardware ?? Hardware(), new ProviderModel("gemma-4-e4b-it"));

    private static async Task<string> ReadAsync(IAsyncEnumerable<LlmStreamEvent> events)
    {
        var text = new StringBuilder();
        await foreach (var e in events) if (e is LlmTextDelta d) text.Append(d.Text);
        return text.ToString();
    }

    [TestMethod]
    public void ItIsNexaflowsOwnHarness_AndNothingLeavesTheMachine()
    {
        var provider = Provider(new FakeModelStore(), new FakeAiHost());

        Assert.AreEqual(("Nexaflow", ModelHarness.Owned, DataResidency.StaysOnMachine), (provider.Name, provider.Harness, provider.Residency));
        Assert.IsTrue(provider.SupportsImages);
    }

    [TestMethod]
    public void TheModel_IsAvailableOnceItsFilesAreHere()
    {
        var store = new FakeModelStore();
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
        var provider = Provider(new FakeModelStore(Gemma.Files), host);
        var image = new LlmAttachment(@"C:\shot.png", "image/png");
        var notes = new LlmAttachment(@"C:\notes.txt", "text/plain");

        var reply = await ReadAsync(provider.StreamAsync(
        [
            new(LlmRole.System, "Be brief."),
            new(LlmRole.User, "What failed?") { Attachments = [image, notes] },
        ]));

        Assert.AreEqual("Hello there.", reply);
        var generate = host.Requests.OfType<Generate>().Single();
        Assert.AreEqual(RequestPriority.Interactive, generate.Priority);
        Assert.AreEqual(@"C:\ai\models\gemma-4-e4b-it-qat", generate.Model.Folder, "the folder is where the store keeps its files");
        Assert.AreEqual(("gemma-4-E4B_q4_0-it.gguf", "gemma-4-E4B-it-mmproj.gguf"), (generate.Model.File, generate.Model.Projector),
                        "the GGUF, and the projector it sees and hears through");
        Assert.AreEqual(TurnRole.System, generate.Turns[0].Role);
        Assert.AreEqual(@"C:\shot.png", generate.Turns[1].Media!.Single().Path, "an image the model reads goes as an image");
        StringAssert.Contains(generate.Turns[1].Text, @"[attached: C:\notes.txt]", "anything else is named");
    }

    [TestMethod]
    public async Task BackgroundWork_AsksForTheBackgroundPriority()
    {
        var host = new FakeAiHost();
        var provider = Provider(new FakeModelStore(Gemma.Files), host);

        await provider.CompleteAsync([new(LlmRole.User, "Analyse this page.")], LlmUrgency.Background);

        Assert.AreEqual(RequestPriority.Background, host.Requests.OfType<Generate>().Single().Priority);
    }

    [TestMethod]
    public async Task ARequestTheHostRefuses_IsAProviderFailure()
    {
        var host = new FakeAiHost { Reply = (r, _) => throw new AiHostException(AiHostFailure.Request, "over budget", HostErrorKind.Refused) };
        var provider = Provider(new FakeModelStore(Gemma.Files), host);

        var ex = await Assert.ThrowsExactlyAsync<LlmProviderException>(() => ReadAsync(provider.StreamAsync([new(LlmRole.User, "Hi")])));
        StringAssert.Contains(ex.Message, "over budget");
    }

    [TestMethod]
    public async Task AHostLostBeforeAWord_IsAskedOnceMore()
    {
        var host = new FakeAiHost();
        var answer = host.Reply;
        host.Reply = (r, attempt) => r is Generate && attempt == 0 ? throw new AiHostException(AiHostFailure.Lost, "crashed") : answer(r, attempt);
        var provider = Provider(new FakeModelStore(Gemma.Files), host);

        Assert.AreEqual("Hello there.", await ReadAsync(provider.StreamAsync([new(LlmRole.User, "Hi")])));
        Assert.AreEqual(2, host.Requests.OfType<Generate>().Count());
    }

    [TestMethod]
    public async Task AHostLostPartWay_IsNotAskedAgain_TheReaderHasSeenPartOfTheReply()
    {
        var host = new FakeAiHost();
        host.Reply = (r, _) => r is Generate ? LostAfterAWord(r.RequestId) : [];
        var provider = Provider(new FakeModelStore(Gemma.Files), host);

        await Assert.ThrowsExactlyAsync<LlmProviderException>(() => ReadAsync(provider.StreamAsync([new(LlmRole.User, "Hi")])));
        Assert.AreEqual(1, host.Requests.OfType<Generate>().Count());

        static IEnumerable<HostReply> LostAfterAWord(long id)
        {
            yield return new TextDelta(id, "Hel");
            throw new AiHostException(AiHostFailure.Lost, "crashed");
        }
    }

    [TestMethod]
    public async Task WarmingUp_LoadsAModelThatIsHere_OnTheGpu_RunningFromThePack_AndDownloadsNothing()
    {
        var store = new FakeModelStore([.. Gemma.Files, .. CudaRuntimePack.Files]);
        var host = new FakeAiHost();
        var provider = Provider(store, host, Nvidia());

        await provider.WarmupAsync();

        Assert.AreEqual(0, store.Fetched.Count, "a download is the user's to start");
        Assert.IsNotNull(host.Environment.RuntimePack, "the host runs from the pack");
        var load = (LoadModel)host.Requests.Single();
        Assert.AreEqual((0, 32768), (load.Model.Device, load.Model.ContextTokens), "on the GPU, with the context the catalog gives it where there is room");
        Assert.AreEqual(5905580032L + 17920L * 32768, load.Model.MemoryBytes, "what it holds there, context and all");
    }

    [TestMethod]
    public async Task WarmingUp_AModelNotHere_DownloadsNothing_AndLoadsNothing()
    {
        var store = new FakeModelStore([.. Gemma.Files]);   // the pack it needs on this machine is not
        var host = new FakeAiHost();

        await Provider(store, host, Nvidia()).WarmupAsync();

        Assert.AreEqual(0, store.Fetched.Count);
        Assert.AreEqual(0, host.Requests.Count);
    }

    [TestMethod]
    public async Task OnAMachineThePackDoesNotSuit_TheModelAloneIsAllItNeeds_AndItRunsOnTheCpu()
    {
        var store = new FakeModelStore([.. Gemma.Files]);
        var host = new FakeAiHost();
        var provider = Provider(store, host, Hardware(ComputeVendor.Amd));

        await provider.WarmupAsync();

        Assert.AreEqual(AiHostEnvironment.Cpu, host.Environment);
        Assert.AreEqual((null, 0L), (((LoadModel)host.Requests.Single()).Model.Device, ((LoadModel)host.Requests.Single()).Model.MemoryBytes));
    }

    [TestMethod]
    public async Task OnACardWithLittleRoom_TheModelIsGivenAShorterContext_AndSaysSo()
    {
        var host = new FakeAiHost { Gpus = [new HostGpu(0, "NVIDIA GeForce RTX 2080 Ti", null, 11L << 30, 5905580032L + (1L << 28), 10L << 30)] };
        var provider = Provider(new FakeModelStore([.. Gemma.Files, .. CudaRuntimePack.Files]), host, Nvidia());

        await provider.WarmupAsync();

        int context = ((LoadModel)host.Requests.Single()).Model.ContextTokens;
        Assert.AreEqual(14336, context, "a quarter of a gigabyte past its weights is fourteen thousand-token steps of context");
        Assert.AreEqual(context, (await provider.GetModelInfoAsync())!.ContextWindowTokens, "and the app is told the context it has");
    }

    [TestMethod]
    [DataRow(11, new[] { "gemma-4-e4b-it", "qwen-3.5-9b", "mimo-v2.6-9b" })]
    [DataRow(16, new[] { "gemma-4-e4b-it", "qwen-3.5-9b", "mimo-v2.6-9b", "phi-4" })]
    [DataRow(24, new[] { "gemma-4-e4b-it", "qwen-3.5-9b", "mimo-v2.6-9b", "phi-4", "gemma-4-26b-a4b-it", "qwen-3.8-27b" })]
    [DataRow(32, new[] { "gemma-4-e4b-it", "qwen-3.5-9b", "mimo-v2.6-9b", "phi-4", "gemma-4-26b-a4b-it", "qwen-3.6-35b-a3b", "qwen-3.8-27b", "gemma-4-31b-it" })]
    public void EachCard_ListsTheLanguageModelsItFits(int gigabytes, string[] listed)
    {
        var card = new ComputeHardwareReport([new ComputeAdapter("gpu", ComputeVendor.Nvidia, (long)gigabytes << 30, false, false)], CudaRuntimePack.MinimumDriver);

        CollectionAssert.AreEqual(listed, Provider(new FakeModelStore(), new FakeAiHost(), card).Catalog.Select(m => m.Id).ToArray());
    }

    [TestMethod]
    public void WithoutAGpuItCanRunOn_OnlyTheDefaultModelIsListed()
        => CollectionAssert.AreEqual(new[] { "gemma-4-e4b-it" }, Provider(new FakeModelStore(), new FakeAiHost(), Hardware(ComputeVendor.Amd)).Catalog.Select(m => m.Id).ToArray(),
                                     "it runs on the CPU, as every model could, but the others too slowly to offer");

    [TestMethod]
    public async Task ARecording_GoesToTheModelAsAudio()
    {
        var host = new FakeAiHost();
        var provider = Provider(new FakeModelStore(Gemma.Files), host);

        await ReadAsync(provider.StreamAsync([new(LlmRole.User, "What is said?") { Attachments = [new LlmAttachment("clip.wav", "audio/wav", [1, 2])] }]));

        var media = host.Requests.OfType<Generate>().Single().Turns[0].Media!.Single();
        Assert.AreEqual(MediaKind.Audio, media.Kind);
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, media.Bytes);
        Assert.IsTrue(provider.SupportsAudio);
    }
}
