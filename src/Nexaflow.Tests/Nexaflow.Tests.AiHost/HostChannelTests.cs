using System.IO.Pipes;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>The wire: every message survives the pipe as what it was sent as.</summary>
[TestClass]
[CoversNode("ai-host")]
public class HostChannelTests
{
    [TestMethod]
    public async Task EveryMessage_ArrivesAsItWasSent()
    {
        string name = "nexaflow-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(), client.ConnectAsync());

        await using var app  = new HostChannel(server);
        await using var host = new HostChannel(client);

        HostMessage[] sent =
        [
            new Hello("ab12", AiHostProtocol.Version, 7), new Welcome(), new Ping(3), new Pong(3), new CancelRequest(9),
            new DescribeHost(1), new Progress(1, "loading", 5, 10), new Completed(1),
            new Failed(2, HostErrorKind.Refused, "over budget"),
        ];
        foreach (var message in sent)
        {
            await host.SendAsync(message);
            Assert.AreEqual(message, await app.ReceiveAsync());
        }

        await host.SendAsync(new HostDescription(4, "1.30.0", ["CUDAExecutionProvider", "CPUExecutionProvider"], @"C:\ai\runtime\cuda",
                                                 [new HostGpu(0, "NVIDIA GeForce RTX 2080 Ti", "0000:01:00.0", 11L << 30, 9L << 30, 10L << 30)]));
        var description = (HostDescription)(await app.ReceiveAsync())!;
        Assert.AreEqual((4L, "1.30.0", @"C:\ai\runtime\cuda"), (description.RequestId, description.RuntimeVersion, description.RuntimePack));
        Assert.AreEqual(new HostGpu(0, "NVIDIA GeForce RTX 2080 Ti", "0000:01:00.0", 11L << 30, 9L << 30, 10L << 30), description.Gpus.Single(), "its GPUs, each with its budget");
        CollectionAssert.AreEqual(new[] { "CUDAExecutionProvider", "CPUExecutionProvider" }, description.ExecutionProviders.ToArray());
    }

    [TestMethod]
    public async Task ADrawing_ArrivesWithItsModel_AndThePictureWithItsBytes()
    {
        string name = "nexaflow-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(), client.ConnectAsync());

        await using var app  = new HostChannel(server);
        await using var host = new HostChannel(client);

        var model = new DrawingModel(DrawingMethod.HiDream,
                                     new Dictionary<string, ModelSpec> { [DrawingParts.Denoiser] = new("h/denoiser", "hidream-denoiser", @"C:\ai\h\denoiser", "denoiser.onnx", null, 2, 0) },
                                     50, 3f, 5f);
        await app.SendAsync(new Draw(5, model, "a fox", 1344, 768, 42, RequestPriority.Job));
        var draw = (Draw)(await host.ReceiveAsync())!;
        Assert.AreEqual((5L, "a fox", 1344, 768, 42L, RequestPriority.Job), (draw.RequestId, draw.Prompt, draw.Width, draw.Height, draw.Seed, draw.Priority));
        Assert.AreEqual((DrawingMethod.HiDream, 50, 3f, 5f), (draw.Model.Method, draw.Model.Steps, draw.Model.Shift, draw.Model.Guidance));
        Assert.AreEqual(@"C:\ai\h\denoiser", draw.Model.Parts[DrawingParts.Denoiser].Folder);

        byte[] rgb = [1, 2, 3, 4, 5, 6];
        await host.SendAsync(new Drawn(5, 2, 1, rgb));
        var drawn = (Drawn)(await app.ReceiveAsync())!;
        Assert.AreEqual((2, 1), (drawn.Width, drawn.Height));
        CollectionAssert.AreEqual(rgb, drawn.Rgb);
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task AMessagesByteArrays_CrossBesideItsJson_ManyMegabytesAndSeveralAtOnce()
    {
        await using var pair = await PipePair.OpenAsync();
        var model = new DrawingModel(DrawingMethod.HiDream, new Dictionary<string, ModelSpec>(), 4, 3f, 1f);
        byte[] first = [.. Enumerable.Range(0, 1024 * 1024 * 3 * 3).Select(i => (byte)i)];
        byte[] second = [7, 8, 9];
        byte[] mask = new byte[1024 * 1024];
        mask[12345] = 255;

    var receiving = pair.Host.ReceiveAsync();   // a pipe holds a little: the other end reads as it is written, as a host's loop does
        await pair.App.SendAsync(new Draw(5, model, "a fox", 1024, 1024, 42, RequestPriority.Job)
        {
            Sources = [new DrawingSource(3072, 1024, first), new DrawingSource(1, 1, second)],
            Mask = mask,
        });
        var draw = (Draw)(await receiving)!;

        CollectionAssert.AreEqual(first, draw.Sources[0].Rgb);
        CollectionAssert.AreEqual(second, draw.Sources[1].Rgb);
        CollectionAssert.AreEqual(mask, draw.Mask);
    }

    [TestMethod]
    public async Task AStreamEndingInsideAFrame_IsNoMessage()
    {
        var stream = new MemoryStream([10, 0, 0, 0, (byte)'{']);
        await using var channel = new HostChannel(stream);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => channel.ReceiveAsync());
    }

    /// <summary>The two ends of a byte-mode pipe, each with its channel.</summary>
    private sealed class PipePair : IAsyncDisposable
    {
        public required HostChannel App { get; init; }
        public required HostChannel Host { get; init; }

        public static async Task<PipePair> OpenAsync()
        {
            string name = "nexaflow-test-" + Guid.NewGuid().ToString("N");
            var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536);
            var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Task.WhenAll(server.WaitForConnectionAsync(), client.ConnectAsync());
            return new PipePair { App = new HostChannel(server), Host = new HostChannel(client) };
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await Host.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task AClosedPipe_ReadsAsTheEnd()
    {
        string name = "nexaflow-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(), client.ConnectAsync());
        await using var app = new HostChannel(server);

        await client.DisposeAsync();

        Assert.IsNull(await app.ReceiveAsync());
    }
}
