using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Services.Initiatives.Cli;
using Nexaflow.Services.Initiatives.Cli.Daemon;
using Nexaflow.Services.Initiatives.Hosting;
using Nexaflow.Services.Initiatives.Hosting.Ipc;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;
using System.Diagnostics;

namespace Nexaflow.Tests.Initiatives.Hosting;

/// <summary>
/// The resident process serving real connections, in this process, on a pipe of its own.
/// <para>
/// Each test here is one of the ways a call to <c>nfi</c> once hung, or would have: work queued by a caller that
/// had already gone, a stop that crashed the process mid-flight, input read from a console the process does not
/// have, and a command sent a second time after it had been taken. None of them shows up in a test of the parts
/// on their own — they live in how the parts meet a pipe.
/// </para>
/// <para>
/// Not parallel: the server's ledger and locks are the process's, shared by every test that serves.
/// </para>
/// </summary>
[TestClass]
[CoversNode("initiatives-daemon")]
[DoNotParallelize]
public class DaemonServingTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Product();
    }

    [TestCleanup]
    public void Teardown() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private static string Product()
    {
        var root = Directory.CreateTempSubdirectory("nexa-daemon-").FullName;
        new ProductStore(root).Initialize("P");
        return root;
    }

    private (Task Serving, string Pipe, InitiativesHost Host) Serve()
    {
        var pipe = "nfi-test-" + Guid.NewGuid().ToString("N")[..12];
        var host = new InitiativesHost(_root);
        return (Task.Run(() => DaemonServer.Serve(pipe, host)), pipe, host);
    }

    private static NamedPipeClientStream Connect(string pipe)
    {
        var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.None);
        client.Connect(5_000);
        return client;
    }

    private static void Stop(string pipe)
    {
        using var client = Connect(pipe);
        DaemonProtocol.Write(client, new DaemonRequest { Stop = true, Ticket = DaemonRequest.NewTicket() });
        DaemonProtocol.Read<DaemonAck>(client);
        DaemonProtocol.Read<DaemonResponse>(client);
    }

    private static void Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail($"timed out waiting for {what}");
            Thread.Sleep(20);
        }
    }

    /// <summary>
    /// <c>nfi daemon stop</c> cancelled the accept loop's wait, which threw out of a method with no handler for
    /// it: the process died instead of stopping — no drain, no flush, and every other tree's command in flight
    /// went with it.
    /// </summary>
    [TestMethod]
    public void DaemonStop_ShutsDownCleanly()
    {
        var (serving, pipe, host) = Serve();
        try
        {
            Stop(pipe);

            Assert.IsTrue(serving.Wait(TimeSpan.FromSeconds(45)), "the accept loop should return once told to stop");
            Assert.IsFalse(serving.IsFaulted, serving.Exception?.ToString());
        }
        finally { host.Dispose(); }
    }

    /// <summary>
    /// A caller killed by a harness timeout left its command queued, the retry queued behind it, and every
    /// command on the tree looked hung until all of them had run for nobody. A command whose caller has gone is
    /// dropped before it takes its turn.
    /// </summary>
    [TestMethod]
    public void QueuedRequestIsDroppedWhenItsClientDisconnects()
    {
        var (serving, pipe, host) = Serve();
        var tree = Path.Combine(_root, "a-tree-somebody-is-busy-in");
        var turn = DaemonServer.Turns.GetOrAdd(tree, _ => new DaemonServer.Turn());

        turn.Semaphore.Wait();                               // another command holds the tree
        try
        {
            var ticket = DaemonRequest.NewTicket();
            using (var client = Connect(pipe))
            {
                DaemonProtocol.Write(client, DaemonRequest.Command(ticket, ["validate", _root], tree, _root, null));
                Assert.IsNotNull(DaemonProtocol.Read<DaemonAck>(client), "taken, and said so");
                Until(() => DaemonServer.Ledger.StatusOf(ticket).State == WorkState.Queued, "the command to queue");
            }                                      // …and its caller goes

            Until(() => DaemonServer.Ledger.StatusOf(ticket).State == WorkState.Finished,
                  "the queued command to be dropped while the tree is still held");
            Assert.AreEqual(0, DaemonServer.Ledger.StatusOf(ticket).RanSeconds, "it never got its turn");
        }
        finally
        {
            turn.Semaphore.Release();
            Stop(pipe);
            serving.Wait(TimeSpan.FromSeconds(45));
            host.Dispose();
        }
    }

    [TestMethod]
    public void AStuckCommand_IsLeftBehind_AndWhatWaitsOnItsTreeRuns()
    {
        var (serving, pipe, host) = Serve();
        var tree  = Path.Combine(_root, "a-tree-with-a-stuck-command");
        var stuck = DaemonServer.Turns.GetOrAdd(tree, _ => new DaemonServer.Turn());

        stuck.Semaphore.Wait();                        // a command that will never let go
        try
        {
            var before = host.Workspace(tree);
            var ticket = DaemonRequest.NewTicket();
            using var client = Connect(pipe);
            DaemonProtocol.Write(client, DaemonRequest.Command(ticket, ["validate", _root], tree, _root, null));
            Assert.IsNotNull(DaemonProtocol.Read<DaemonAck>(client), "taken, and said so");
            Until(() => DaemonServer.Ledger.StatusOf(ticket).State == WorkState.Queued, "the command to queue behind it");

            DaemonServer.Abandon(tree, host);

            var answer = Task.Run(() => DaemonProtocol.Read<DaemonResponse>(client));
            Assert.IsTrue(answer.Wait(TimeSpan.FromSeconds(30)), "the waiting command takes the tree's new turn and answers");
            Assert.IsNotNull(answer.Result);
            Assert.AreNotSame(before, host.Workspace(tree), "the graph is loaded afresh rather than shared with the stuck command");
        }
        finally
        {
            stuck.Semaphore.Release();
            Stop(pipe);
            serving.Wait(TimeSpan.FromSeconds(45));
            host.Dispose();
        }
    }

    [TestMethod]
    public void Stop_GivesUpThePipeAtOnce_AndStillAnswersWhatItTook()
    {
        var pipe    = "nfi-test-" + Guid.NewGuid().ToString("N")[..12];
        var host    = new InitiativesHost(_root);
        var givenUp = new ManualResetEventSlim();
        var serving = Task.Run(() => DaemonServer.Serve(pipe, host, givenUp.Set));

        var tree = Path.Combine(_root, "a-tree-somebody-is-busy-in");
        var turn = DaemonServer.Turns.GetOrAdd(tree, _ => new DaemonServer.Turn());
        var held = true;

        turn.Semaphore.Wait();                         // the command ahead, still running when the stop comes
        try
        {
            var ticket = DaemonRequest.NewTicket();
            using var client = Connect(pipe);
            DaemonProtocol.Write(client, DaemonRequest.Command(ticket, ["validate", _root], tree, _root, null));
            Assert.IsNotNull(DaemonProtocol.Read<DaemonAck>(client), "taken, and said so");
            Until(() => DaemonServer.Ledger.StatusOf(ticket).State == WorkState.Queued, "the command to queue");

            Stop(pipe);
            Assert.IsTrue(givenUp.Wait(TimeSpan.FromSeconds(15)), "the pipe is given up while work is still in hand");
            Assert.IsFalse(serving.IsCompleted, "and the process stays to finish that work");

            turn.Semaphore.Release();
            held = false;
            Assert.IsNotNull(DaemonProtocol.Read<DaemonResponse>(client), "what was taken before the stop is answered");
            Assert.IsTrue(serving.Wait(TimeSpan.FromSeconds(45)), "and then the process goes");
        }
        finally
        {
            if (held) turn.Semaphore.Release();
            host.Dispose();
        }
    }

    /// <summary>
    /// Standard input belongs to the request. It was a static set per command, so two trees served at once
    /// could clear each other's — and a command left with none read the daemon's own standard input, a handle
    /// nobody inherited, and blocked there holding its tree's lock.
    /// </summary>
    [TestMethod]
    public void StdinDoesNotCrossBetweenConcurrentTrees()
    {
        var other = Product();
        try
        {
            Task<int> Create(string root, string? stdin) => Task.Run(() =>
            {
                using var scope = RequestScope.Begin(new StringWriter(), new StringWriter(),
                                                     new RequestContext(root) { Stdin = stdin });
                return Program.Execute(["graph", "edit", "create", "piped.md", "--stdin", root]);
            });

            var mine   = Create(_root, "written from the first tree");
            var theirs = Create(other, "written from the second tree");
            Assert.IsTrue(Task.WaitAll([mine, theirs], TimeSpan.FromSeconds(30)), "neither may block on a console");

            StringAssert.Contains(File.ReadAllText(Path.Combine(_root, "piped.md")), "first tree");
            StringAssert.Contains(File.ReadAllText(Path.Combine(other, "piped.md")), "second tree");

            var nothingPiped = Create(Product(), stdin: null);
            Assert.IsTrue(nothingPiped.Wait(TimeSpan.FromSeconds(30)),
                          "a request with nothing piped reads nothing - it must not wait on this process's input");
        }
        finally { try { Directory.Delete(other, recursive: true); } catch { } }
    }

    /// <summary>
    /// Once a command has been acknowledged it may have run, so a connection lost after that is reported, never
    /// retried: the retry path started a daemon and sent it again, and an edit applied twice is not a hang but is
    /// worse.
    /// </summary>
    [TestMethod]
    public void ErrorAfterAck_IsNotResent()
    {
        var pipe     = DaemonProtocol.PipeName(_root, DaemonProtocol.BuildStamp());
        var received = 0;

        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var fake = Task.Run(async () =>
        {
            while (!cancel.IsCancellationRequested)
            {
                using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                try { await server.WaitForConnectionAsync(cancel.Token); }
                catch (OperationCanceledException) { return; }

                if (DaemonProtocol.Read<DaemonRequest>(server) is not { Ask: DaemonAsk.Command } request) continue;
                Interlocked.Increment(ref received);
                DaemonProtocol.Write(server, new DaemonAck(request.Ticket, true, null));
                server.Disconnect();                // taken, then gone without an answer
            }
        });

        Assert.ThrowsExactly<DaemonUnavailableException>(() => DaemonClient.Run(["validate"], _root, null));
        Thread.Sleep(500);                          // room for a retry to arrive, if one were coming
        Assert.AreEqual(1, Volatile.Read(ref received), "sent once, and not again after it was taken");

        cancel.Cancel();
        fake.Wait(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// A command's flush wrote every tree's graph, which meant taking every tree's lock: an answer on one worktree
    /// waited behind whatever another worktree's command was in the middle of. A tree's flush is that tree's alone.
    /// </summary>
    [TestMethod]
    public void FlushOfOneTreeDoesNotWaitOnAnother()
    {
        using var host = new InitiativesHost(_root);
        var busy = Directory.CreateDirectory(Path.Combine(_root, "busy-tree")).FullName;
        var idle = Directory.CreateDirectory(Path.Combine(_root, "idle-tree")).FullName;

        host.Workspace(idle).Mutate(_ => 0);          // something to write, so the flush has real work to do

        using var inside  = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holding = Task.Run(() => host.Workspace(busy).Mutate(_ =>
        {
            inside.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            return 0;
        }));

        try
        {
            Assert.IsTrue(inside.Wait(TimeSpan.FromSeconds(15)), "the busy tree should be mid-command");

            var flushed = Task.Run(() => host.Flush(idle));
            Assert.IsTrue(flushed.Wait(TimeSpan.FromSeconds(5)),
                          "the idle tree's flush waited on the busy tree's command");
        }
        finally
        {
            release.Set();
            holding.Wait(TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>
    /// Two callers racing to start the process produce one that serves and one that finds the pipe's lock held and
    /// exits 0. The loser's caller read that exit as a death and failed, while the winner was a moment from answering.
    /// </summary>
    [TestMethod]
    public void LosingTheStartupRace_StillConnects()
    {
        var pipe = "nfi-test-" + Guid.NewGuid().ToString("N")[..12];
        var host = new InitiativesHost(_root);

        // What a start that lost the race looks like from outside: a process that leaves at once, cleanly.
        static Process? Loser() => Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0")
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        });

        // …and the winner, which answers a little later.
        var serving = Task.Run(() =>
        {
            Thread.Sleep(1500);
            DaemonServer.Serve(pipe, host);
        });

        try
        {
            var request = DaemonRequest.Command(DaemonRequest.NewTicket(), [], _root, _root, null);
            var reply   = DaemonClient.StartThenSend(pipe, request, Loser);

            Assert.AreEqual(0, reply.ExitCode, reply.Error);
        }
        finally
        {
            Stop(pipe);
            serving.Wait(TimeSpan.FromSeconds(45));
            host.Dispose();
        }
    }
}
