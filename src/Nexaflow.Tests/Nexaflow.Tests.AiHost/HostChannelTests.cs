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

        await host.SendAsync(new HostDescription(4, "1.30.0", ["CUDAExecutionProvider", "CPUExecutionProvider"], @"C:\ai\runtime\cuda"));
        var description = (HostDescription)(await app.ReceiveAsync())!;
        Assert.AreEqual((4L, "1.30.0", @"C:\ai\runtime\cuda"), (description.RequestId, description.RuntimeVersion, description.RuntimePack));
        CollectionAssert.AreEqual(new[] { "CUDAExecutionProvider", "CPUExecutionProvider" }, description.ExecutionProviders.ToArray());
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
