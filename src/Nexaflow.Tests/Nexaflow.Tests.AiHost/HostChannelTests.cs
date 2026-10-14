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
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous, 65536, 65536);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(), client.ConnectAsync());
        client.ReadMode = PipeTransmissionMode.Message;
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
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous, 65536, 65536);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(), client.ConnectAsync());
        client.ReadMode = PipeTransmissionMode.Message;
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

        byte[] png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];
        await host.SendAsync(new Drawn(5, png));
        CollectionAssert.AreEqual(png, ((Drawn)(await app.ReceiveAsync())!).Png);
    }

    [TestMethod]
    public async Task AClosedPipe_ReadsAsTheEnd()
    {
        string name = "nexaflow-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(), client.ConnectAsync());
        await using var app = new HostChannel(server);

        await client.DisposeAsync();

        Assert.IsNull(await app.ReceiveAsync());
    }
}
