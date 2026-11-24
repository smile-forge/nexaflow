using System.Collections.Concurrent;
using System.IO.Pipes;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// A host that runs in the test process: it opens the pipe it is started on, takes the client's hello and speaks the protocol, and
/// can be told to misbehave — miss heartbeats, ignore cancellations, crash after the handshake, turn the client away, or answer as
/// a process the client did not start.
/// </summary>
internal sealed class FakeHost : IAiHostLauncher
{
    private int _starts;

    public int Starts => _starts;

    public bool AnswerPings { get; set; } = true;
    public bool HonourCancel { get; set; } = true;
    public bool CrashAfterHandshake { get; set; }
    /// <summary>The process its welcome names, in place of the one started — as something else answering on the pipe would.</summary>
    public int? WelcomesAs { get; set; }

    /// <summary>Turns the client away, saying this.</summary>
    public string? RefusesWith { get; set; }

    /// <summary>The last client's hello, and the token the host was started with.</summary>
    public (Hello Hello, string Token)? Greeted { get; private set; }

    /// <summary>Dies as a native fault kills a host once a request arrives: the pipe goes with the process, and the exit —
    /// this code, and these last lines of error output — is seen <see cref="ExitSeenAfter"/> later, as a real exit waits
    /// for its error output to drain.</summary>
    public (int Code, string[] Errors)? FaultOnRequest { get; set; }

    public TimeSpan ExitSeenAfter { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>What the host does with a request; by default it describes itself and completes.</summary>
    public Func<HostRequest, HostChannel, Task> Serve { get; set; } = Describe;

    /// <summary>What the host does with input sent into a request; by default nothing.</summary>
    public Func<RequestInput, HostChannel, Task> Receive { get; set; } = (_, _) => Task.CompletedTask;

    private readonly TaskCompletionSource _requestArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once a request is in the host's hands — when a cancel is sure to reach a request it is serving,
    /// however long starting the host took.</summary>
    public Task RequestArrived => _requestArrived.Task;

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
        _ = RunAsync(pipeName, token, process);
        return process;
    }

    public static async Task Describe(HostRequest request, HostChannel channel)
    {
        await channel.SendAsync(new HostDescription(request.RequestId, "fake", ["CPUExecutionProvider"], RuntimePack: null, Gpus: []));
        await channel.SendAsync(new Completed(request.RequestId));
    }

    public static Task Silent(HostRequest request, HostChannel channel) => Task.CompletedTask;

    private async Task RunAsync(string pipeName, string token, FakeProcess process)
    {
        try
        {
            var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(process.Killed);
            var channel = new HostChannel(pipe);
            process.Channel = channel;

            if (await channel.ReceiveAsync(process.Killed) is not Hello hello) { process.Exit(3); return; }
            Greeted = (hello, token);
            string? refusal = RefusesWith ?? (hello.Token == token && hello.ProtocolVersion == AiHostProtocol.Version ? null : "not the token or protocol it was started with");
            if (refusal is not null)
            {
                await channel.SendAsync(new HelloRefused(refusal));
                process.Exit(3);
                return;
            }
            await channel.SendAsync(new Welcome(WelcomesAs ?? process.Id));
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
                    case HostRequest when FaultOnRequest is { } fault:
                        process.Channel = null;
                        await channel.DisposeAsync();
                        await Task.Delay(ExitSeenAfter);
                        process.LastErrors = fault.Errors;
                        process.Exit(fault.Code);
                        return;
                    case HostRequest request:
                        _requestArrived.TrySetResult();
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
        public IReadOnlyList<string> LastErrors { get; set; } = [];
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

    public HostMemory Memory => HostMemory.Unbounded;
}
