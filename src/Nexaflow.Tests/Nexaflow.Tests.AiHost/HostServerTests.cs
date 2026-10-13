using System.IO.Pipes;
using System.Threading.Channels;
using Nexaflow.AiHost;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>The host's dispatch: input and cancellations reach a request however soon after it they are sent.</summary>
[TestClass]
[CoversNode("ai-host")]
public class HostServerTests
{
    [TestMethod]
    public async Task InputSentStraightAfterARequest_ReachesIt_InOrder_AndInputEndEndsIt()
    {
        await using var pipes = await Pipes.OpenAsync(new CountingStream());

        await pipes.App.SendAsync(new DescribeHost(1));
        await pipes.App.SendAsync(new AudioInput(1, [1]));
        await pipes.App.SendAsync(new AudioInput(1, [1, 2]));
        await pipes.App.SendAsync(new InputEnd(1));

        Assert.AreEqual(new Progress(1, "heard", 1, 0), await pipes.App.ReceiveAsync());
        Assert.AreEqual(new Progress(1, "heard", 2, 0), await pipes.App.ReceiveAsync());
        Assert.AreEqual(new Completed(1), await pipes.App.ReceiveAsync());
    }

    [TestMethod]
    public async Task ACancellationSentStraightAfterARequest_CancelsIt()
    {
        await using var pipes = await Pipes.OpenAsync(new CountingStream());

        await pipes.App.SendAsync(new DescribeHost(1));
        await pipes.App.SendAsync(new CancelRequest(1));

        var end = (Failed)(await pipes.App.ReceiveAsync())!;
        Assert.AreEqual(HostErrorKind.Cancelled, end.Kind);
    }

    [TestMethod]
    public async Task InputForARequestThatHasEnded_IsDropped()
    {
        await using var pipes = await Pipes.OpenAsync(new CountingStream());

        await pipes.App.SendAsync(new DescribeHost(1));
        await pipes.App.SendAsync(new InputEnd(1));
        Assert.AreEqual(new Completed(1), await pipes.App.ReceiveAsync());

        await pipes.App.SendAsync(new AudioInput(1, [1]));
        await pipes.App.SendAsync(new Ping(5));
        Assert.AreEqual(new Pong(5), await pipes.App.ReceiveAsync(), "nothing answered the stray input");
    }

    /// <summary>A stream that says how many bytes each input carried, until its input ends.</summary>
    private sealed class CountingStream : HostStreamHandler<DescribeHost>
    {
        protected override async Task HandleAsync(DescribeHost request, Func<HostReply, Task> reply, ChannelReader<RequestInput> input,
                                                  CancellationToken ct)
        {
            await foreach (var item in input.ReadAllAsync(ct))
                if (item is AudioInput audio) await reply(new Progress(request.RequestId, "heard", audio.Pcm.Length, 0));
        }
    }

    /// <summary>A pipe with the app's end to talk on and a <see cref="HostServer"/> serving the other.</summary>
    private sealed class Pipes : IAsyncDisposable
    {
        private readonly NamedPipeServerStream _app;
        private readonly HostChannel _host;
        private readonly HostLog _log;
        private readonly Task _serving;

        private Pipes(NamedPipeServerStream app, HostChannel host, HostLog log, Task serving)
        {
            _app     = app;
            App      = new HostChannel(app);
            _host    = host;
            _log     = log;
            _serving = serving;
        }

        public HostChannel App { get; }

        public static async Task<Pipes> OpenAsync(params IHostRequestHandler[] handlers)
        {
            string name = "nexaflow-test-" + Guid.NewGuid().ToString("N");
            var app = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous, 65536, 65536);
            var hostEnd = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Task.WhenAll(app.WaitForConnectionAsync(), hostEnd.ConnectAsync());
            hostEnd.ReadMode = PipeTransmissionMode.Message;

            var host = new HostChannel(hostEnd);
            var log  = new HostLog(Path.Combine(Path.GetTempPath(), "nexaflow-tests", "aihost-logs"));
            var server = new HostServer(host, log, handlers);
            return new Pipes(app, host, log, Task.Run(server.RunAsync));
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await _serving.WaitAsync(TimeSpan.FromSeconds(10));
            await _host.DisposeAsync();
            _log.Dispose();
        }
    }
}
