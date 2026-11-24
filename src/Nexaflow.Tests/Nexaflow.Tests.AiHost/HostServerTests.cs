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

    [TestMethod]
    public async Task ARequestWaitingBehindOtherWork_IsSaidToBeQueued_WhenItHasBeenQuiet()
    {
        var clock = new ManualClock();
        var gate  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pipes = await Pipes.OpenAsync(clock, new Doing(() => RequestActivity.WaitAsync(gate.Task)));

        await pipes.App.SendAsync(new DescribeHost(1));
        await Task.Delay(Soon);
        clock.Advance(HostServer.Quiet);

        Assert.AreEqual(new Progress(1, Progress.Queued, 0, 0), await pipes.App.ReceiveAsync());
        gate.SetResult();
        Assert.AreEqual(new Completed(1), await pipes.App.ReceiveAsync());
    }

    [TestMethod]
    public async Task ARequestGoneOnFromAWait_IsSaidToBeWorking_WhenItHasBeenQuiet()
    {
        var clock = new ManualClock();
        var waited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var step   = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pipes = await Pipes.OpenAsync(clock, new Doing(async () =>
        {
            await RequestActivity.WaitAsync(waited.Task);
            await step.Task;   // a step of its own, nothing to show yet
        }));

        await pipes.App.SendAsync(new DescribeHost(1));
        await Task.Delay(Soon);
        waited.SetResult();
        await Task.Delay(Soon);
        clock.Advance(HostServer.Quiet);

        Assert.AreEqual(new Progress(1, Progress.Working, 0, 0), await pipes.App.ReceiveAsync());
        step.SetResult();
        Assert.AreEqual(new Completed(1), await pipes.App.ReceiveAsync());
    }

    [TestMethod]
    public async Task ARequestStuckInOneStep_IsLeftSilent_ForTheClientsWatchdogToHear()
    {
        var clock = new ManualClock();
        var step  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pipes = await Pipes.OpenAsync(clock, new Doing(() => step.Task));

        await pipes.App.SendAsync(new DescribeHost(1));
        await Task.Delay(Soon);
        clock.Advance(HostServer.Quiet * 3);
        await Task.Delay(Soon);
        await pipes.App.SendAsync(new Ping(7));

        Assert.AreEqual(new Pong(7), await pipes.App.ReceiveAsync(), "nothing was said for it");
        step.SetResult();
        Assert.AreEqual(new Completed(1), await pipes.App.ReceiveAsync());
    }

    private static readonly TimeSpan Soon = TimeSpan.FromMilliseconds(200);

    /// <summary>A request that does what it is given, then completes.</summary>
    private sealed class Doing(Func<Task> work) : HostRequestHandler<DescribeHost>
    {
        protected override Task HandleAsync(DescribeHost request, Func<HostReply, Task> reply, CancellationToken ct) => work();
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

        public static Task<Pipes> OpenAsync(params IHostRequestHandler[] handlers) => OpenAsync(TimeProvider.System, handlers);

        public static async Task<Pipes> OpenAsync(TimeProvider clock, params IHostRequestHandler[] handlers)
        {
            string name = "nexaflow-test-" + Guid.NewGuid().ToString("N");
            var app = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536);
            var hostEnd = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Task.WhenAll(app.WaitForConnectionAsync(), hostEnd.ConnectAsync());


            var host = new HostChannel(hostEnd);
            var log  = new HostLog(Path.Combine(TestTemp.Root, "nexaflow-tests", "aihost-logs"), modelCalls: true);
            var server = new HostServer(host, log, handlers, clock);
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
