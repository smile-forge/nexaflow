using System.Runtime.CompilerServices;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>The machine's setup: every holder's models planned together, and each holder whose models moved told so.</summary>
[TestClass]
[CoversNode("local-ai-setup")]
public class HostModelSetupTests
{
    private const long GB = 1L << 30;

    /// <summary>A host with one GPU of a 10 GB budget, recording what it is asked.</summary>
    private sealed class OneGpuHost : IAiHost
    {
        public readonly List<HostRequest> Requests = [];

        public AiHostEnvironment Environment { get; set; } = AiHostEnvironment.Cpu;

        public async IAsyncEnumerable<HostReply> SendAsync(Func<long, HostRequest> create, TimeSpan stallLimit, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            var request = create(Requests.Count + 1);
            if (request is DescribeHost)
            {
                yield return new HostDescription(request.RequestId, "1.30.0", [], @"C:\pack", [new HostGpu(0, "GPU", null, 12 * GB, 10 * GB, 11 * GB)]);
                yield break;
            }
            Requests.Add(request);
        }

        public Task<IHostStream> OpenStreamAsync(Func<long, HostRequest> create, TimeSpan stallLimit, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    /// <summary>A holder that keeps every placement it is told of.</summary>
    private sealed class Holder : IModelHolder
    {
        public readonly List<Placement> Told = [];

        public Task ApplyAsync(Placement placement, CancellationToken ct)
        {
            Told.Add(placement);
            return Task.CompletedTask;
        }
    }

    /// <summary>A language model of 4 GB and 1 MB a token, given 4k where there is room and no less than 1k.</summary>
    private static readonly PlacementNeed Chat = new(new ModelSpec("chat", "llama-chat", "chat", "m.gguf", null, 4 * GB, 4096), BytesPerToken: 1L << 20, ShortestContext: 1024);

    private static readonly PlacementNeed Voice = new(new ModelSpec("voice", "kokoro", "voice", "v.onnx", null, 3 * GB, 0));

    [TestMethod]
    public async Task AHolderWhoseModelsMove_IsToldWhereTheyNowRun()
    {
        var setup = new HostModelSetup(new OneGpuHost());
        Holder chat = new(), voice = new();

        await setup.HoldAsync(chat, [Chat], default);
        await setup.HoldAsync(voice, [Voice], default);

        Assert.AreEqual(2, chat.Told.Count, "the chat model gave up context for the voice, and was told");
        Assert.AreEqual((4096, 3072), (chat.Told[0].Of("chat")!.ContextTokens, chat.Told[1].Of("chat")!.ContextTokens));
        Assert.AreEqual(0, voice.Told.Single().Of("voice")!.Device);
    }

    [TestMethod]
    public async Task AHolderWhoseModelsStay_IsNotToldAgain()
    {
        var setup = new HostModelSetup(new OneGpuHost());
        Holder chat = new(), small = new();

        await setup.HoldAsync(chat, [Chat], default);
        await setup.HoldAsync(small, [Voice with { OnGpu = Voice.OnGpu with { Id = "small", MemoryBytes = 1 * GB } }], default);

        Assert.AreEqual(1, chat.Told.Count, "there was room beside it");
    }

    [TestMethod]
    public async Task LettingGo_UnloadsThatHoldersModels_AndMovesNoOther()
    {
        var host = new OneGpuHost();
        var setup = new HostModelSetup(host);
        Holder chat = new(), voice = new();
        await setup.HoldAsync(chat, [Chat], default);
        await setup.HoldAsync(voice, [Voice], default);

        await setup.LetGoAsync(voice, default);

        Assert.AreEqual("voice", ((UnloadModel)host.Requests.Single()).ModelId);
        Assert.AreEqual(2, chat.Told.Count, "the chat model keeps the context it was given");
        Assert.IsNull(setup.Current!.Of("voice"));
    }
}
