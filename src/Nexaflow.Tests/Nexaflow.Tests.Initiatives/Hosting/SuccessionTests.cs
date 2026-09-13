using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Services.Initiatives.Cli.Daemon;
using Nexaflow.Services.Initiatives.Hosting.Ipc;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Hosting;

/// <summary>
/// One resident process per tree. A rebuilt nfi talks on a new pipe, so the process its previous build started used
/// to run beside the new one — each with its own copy of the graph — until it idled out. Now the newer asks the older
/// to stop; the older never asks the newer.
/// </summary>
[TestClass]
[CoversNode("initiatives-daemon")]
[DoNotParallelize]
public class SuccessionTests
{
    private string _registry = "";

    [TestInitialize]
    public void Setup() => _registry = Directory.CreateTempSubdirectory("nfi-residents-").FullName;

    [TestCleanup]
    public void Teardown() { try { Directory.Delete(_registry, recursive: true); } catch { } }

    private void Record(string pipe, int processId, long builtTicks) =>
        File.WriteAllText(Path.Combine(_registry, pipe + ".json"),
                          JsonSerializer.Serialize(new Succession.Resident(pipe, processId, builtTicks)));

    /// <summary>A stand-in resident: answers on its pipe and notes whether it was asked to stop.</summary>
    private static Task Listen(string pipe, ConcurrentBag<string> stopped, CancellationToken cancel) => Task.Run(async () =>
    {
        while (!cancel.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try { await server.WaitForConnectionAsync(cancel); }
            catch (OperationCanceledException) { return; }

            if (DaemonProtocol.Read<DaemonRequest>(server) is { Stop: true } request)
            {
                stopped.Add(pipe);
                DaemonProtocol.Write(server, new DaemonAck(request.Ticket, true, null));
            }
            server.Disconnect();
        }
    });

    [TestMethod]
    public void AStartingProcess_AsksAnOlderBuildToStop_AndLeavesANewerOneAlone()
    {
        var older   = "nfi-test-old-" + Guid.NewGuid().ToString("N")[..8];
        var newer   = "nfi-test-new-" + Guid.NewGuid().ToString("N")[..8];
        var stopped = new ConcurrentBag<string>();

        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var listening = new[] { Listen(older, stopped, cancel.Token), Listen(newer, stopped, cancel.Token) };

        Record(older, Environment.ProcessId, builtTicks: 100);
        Record(newer, Environment.ProcessId, builtTicks: 300);

        var asked = Succession.Claim("D:/repo", "nfi-test-mine", _registry, builtTicks: 200);

        CollectionAssert.AreEqual(new[] { older }, asked.ToArray());
        CollectionAssert.AreEquivalent(new[] { older }, stopped.ToArray(), "a newer build is not this one's to end");
        Assert.IsTrue(File.Exists(Path.Combine(_registry, "nfi-test-mine.json")), "and it records itself for the next one");

        cancel.Cancel();
        Task.WaitAll(listening, TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public void AResidentThatIsGone_IsForgotten()
    {
        Record("nfi-test-dead", processId: int.MaxValue, builtTicks: 1);           // no such process
        Record("nfi-test-silent", Environment.ProcessId, builtTicks: 1);           // alive, but nothing on its pipe

        Succession.Claim("D:/repo", "nfi-test-mine", _registry, builtTicks: 200);

        Assert.IsFalse(File.Exists(Path.Combine(_registry, "nfi-test-dead.json")));
        Assert.IsFalse(File.Exists(Path.Combine(_registry, "nfi-test-silent.json")),
                       "a process id nothing answers for has been reused by something else");

        Succession.Leave("D:/repo", "nfi-test-mine", _registry);
        Assert.IsFalse(File.Exists(Path.Combine(_registry, "nfi-test-mine.json")), "a process leaving takes its record");
    }
}
