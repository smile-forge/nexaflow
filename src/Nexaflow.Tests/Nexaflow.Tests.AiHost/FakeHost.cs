using System.Collections.Concurrent;
using System.IO.Pipes;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// A host that runs in the test process: it connects to the client's pipe and speaks the protocol, and can be told to
/// misbehave — miss heartbeats, ignore cancellations, crash after the handshake, give the wrong token.
/// </summary>
internal sealed class FakeHost : IAiHostLauncher
{
    private int _starts;

    public int Starts => _starts;

    public bool AnswerPings { get; set; } = true;
    public bool HonourCancel { get; set; } = true;
    public bool CrashAfterHandshake { get; set; }
    public string? WrongToken { get; set; }

    /// <summary>What the host does with a request; by default it describes itself and completes.</summary>
    public Func<HostRequest, HostChannel, Task> Serve { get; set; } = Describe;

    /// <summary>What the host does with input sent into a request; by default nothing.</summary>
    public Func<RequestInput, HostChannel, Task> Receive { get; set; } = (_, _) => Task.CompletedTask;

    public ConcurrentBag<long> CancelsReceived { get; } = [];

    public ConcurrentQueue<RequestInput> InputsReceived { get; } = [];

    /// <summary>What each host was started with, in order.</summary>
    public ConcurrentQueue<AiHostEnvironment> Environments { get; } = [];

    public FakeProcess? Last { get; private set; }

    public IAiHostProcess Start(string pipeName, string token, AiHostEnvironment environment)
    {
        Interlocked.Increment(ref _starts);
        Environments.Enqueue(environment);
        var process = new FakeProcess();
        Last = process;
        _ = RunAsync(pipeName, WrongToken ?? token, process);
        return process;
    }

    public static async Task Describe(HostRequest request, HostChannel channel)
    {
        await channel.SendAsync(new HostDescription(request.RequestId, "fake", ["CPUExecutionProvider"], RuntimePack: null));
        await channel.SendAsync(new Completed(request.RequestId));
    }

    public static Task Silent(HostRequest request, HostChannel channel) => Task.CompletedTask;

    private async Task RunAsync(string pipeName, string token, FakeProcess process)
    {
        try
        {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(5000);
            pipe.ReadMode = PipeTransmissionMode.Message;
            var channel = new HostChannel(pipe);
            process.Channel = channel;

            await channel.SendAsync(new Hello(token, AiHostProtocol.Version, 4242));
            if (await channel.ReceiveAsync(process.Killed) is not Welcome) { process.Exit(3); return; }
            if (CrashAfterHandshake) { process.Exit(1); return; }

            while (await channel.ReceiveAsync(process.Killed) is { } message)
            {
                switch (message)
                {
                    case Ping ping when AnswerPings:
                        await channel.SendAsync(new Pong(ping.Sequence));
                        break;
                    case CancelRequest cancel:
                        CancelsReceived.Add(cancel.RequestId);
                        if (HonourCancel) await channel.SendAsync(new Failed(cancel.RequestId, HostErrorKind.Cancelled, "cancelled"));
                        break;
                    case RequestInput input:
                        InputsReceived.Enqueue(input);
                        await Receive(input, channel);
                        break;
                    case HostRequest request:
                        _ = Task.Run(() => Serve(request, channel));
                        break;
                }
            }
            process.Exit(0);
        }
        catch (Exception)
        {
            process.Exit(1);
        }
    }

    internal sealed class FakeProcess : IAiHostProcess
    {
        private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _kill = new();

        public HostChannel? Channel { get; set; }
        public CancellationToken Killed => _kill.Token;
        public bool WasKilled { get; private set; }

        public int Id => 4242;
        public string LastErrors => "";
        public Task<int> Exited => _exited.Task;

        public void Exit(int code)
        {
            if (!_exited.TrySetResult(code)) return;
            _ = Channel?.DisposeAsync().AsTask();
        }

        public void Kill()
        {
            WasKilled = true;
            _kill.Cancel();
            Exit(-1);
        }

        public void Dispose() => _kill.Dispose();
    }
}
