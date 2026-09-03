using System;
using System.IO;
using Nexaflow.Services.Initiatives.Hosting.Ipc;
using Nexaflow.Tests.Fixtures;
using System.Linq;
using System.Text;

namespace Nexaflow.Tests.Initiatives.Hosting;

/// <summary>
/// The wire between <c>nfi</c> and the process it starts for itself.
/// <para>
/// Two things are worth holding still here. A frame has to survive the round trip exactly, because what
/// crosses it is a command line and its console output and a caller cannot tell a mangled one from a wrong
/// answer. And the pipe name has to change when the binary changes: it is the only thing stopping a rebuilt
/// client from talking to a daemon running yesterday's code, which is a failure this repo has already spent
/// an afternoon on in another guise.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("Transport for the headless CLI — infrastructure, not a product-tree node.")]
public class DaemonProtocolTests
{
    [TestMethod]
    public void ARequest_SurvivesTheRoundTripExactly()
    {
        var sent = DaemonRequest.Command("tkt00000001",
                                         ["graph", "edit", "substitute", "--find", "a — b"],
                                         @"D:
    epo\.claude\worktrees\x", @"D:
    epo\src", "piped text")
            with { Stop = true };

        using var stream = new MemoryStream();
        DaemonProtocol.Write(stream, sent);
        stream.Position = 0;

        var back = DaemonProtocol.Read<DaemonRequest>(stream);

        Assert.IsNotNull(back);
        Assert.AreEqual(DaemonAsk.Command, back!.Ask);
        Assert.AreEqual(sent.Ticket, back.Ticket);
        CollectionAssert.AreEqual(sent.Args, back.Args);
        Assert.AreEqual(sent.CodeRoot, back.CodeRoot);
        Assert.AreEqual(sent.WorkingDirectory, back.WorkingDirectory);
        Assert.AreEqual(sent.Stdin, back.Stdin);
        Assert.IsTrue(back.Stop);
    }

    /// <summary>
    /// The shape of a command exchange: the request out, the acknowledgement straight back, and the answer when
    /// there is one. The client reads them in that order and nothing labels them, so the order is the contract —
    /// a daemon that skipped the middle frame would have its output read as an acknowledgement.
    /// </summary>
    [TestMethod]
    public void ACommandExchange_ReadsBackInTheOrderItWasWritten()
    {
        using var stream = new MemoryStream();

        DaemonProtocol.Write(stream, DaemonRequest.Command("tkt", ["graph", "build"], null, @"D:\repo", null));
        DaemonProtocol.Write(stream, new DaemonAck("tkt", true, null));
        DaemonProtocol.Write(stream, new DaemonResponse(0, "built", ""));
        stream.Position = 0;

        Assert.AreEqual("tkt", DaemonProtocol.Read<DaemonRequest>(stream)?.Ticket);
        Assert.IsTrue(DaemonProtocol.Read<DaemonAck>(stream)?.Accepted);
        Assert.AreEqual("built", DaemonProtocol.Read<DaemonResponse>(stream)?.Out);
    }

    /// <summary>A status query is a request like any other, distinguished only by what it asks for — so the
    /// daemon can answer it on any connection without a second listener or a second frame format.</summary>
    [TestMethod]
    public void AStatusQuery_IsARequest_CarryingOnlyTheTicket()
    {
        using var stream = new MemoryStream();
        DaemonProtocol.Write(stream, DaemonRequest.Status("tkt"));
        stream.Position = 0;

        var back = DaemonProtocol.Read<DaemonRequest>(stream);

        Assert.IsNotNull(back);
        Assert.AreEqual(DaemonAsk.Status, back!.Ask);
        Assert.AreEqual("tkt", back.Ticket);
        Assert.AreEqual(0, back.Args.Length);
    }

    /// <summary>The one time anyone reads these bytes is when something has gone wrong, and at that moment
    /// "Queued" answers the question that a 1 does not.</summary>
    [TestMethod]
    public void AWorkStatus_NamesItsStateInThePayload()
    {
        using var stream = new MemoryStream();
        DaemonProtocol.Write(stream, new DaemonWorkStatus("tkt", WorkState.Queued, "graph stats", 2, 0, "graph build (9s)"));

        StringAssert.Contains(Encoding.UTF8.GetString(stream.ToArray()), "Queued");

        stream.Position = 0;
        var back = DaemonProtocol.Read<DaemonWorkStatus>(stream);

        Assert.AreEqual(WorkState.Queued, back!.State);
        Assert.AreEqual("graph build (9s)", back.Behind);
    }

    /// <summary>Two commands must never share a name, or a status query answers about the wrong one.</summary>
    [TestMethod]
    public void Tickets_AreNotRepeated()
    {
        var tickets = Enumerable.Range(0, 500).Select(_ => DaemonRequest.NewTicket()).ToHashSet();

        Assert.AreEqual(500, tickets.Count);
    }

    /// <summary>The em dash is not decoration: the tool quotes source back at you, and a transport that
    /// mangles it produces error messages that misquote the line they are about.</summary>
    [TestMethod]
    public void AResponse_KeepsItsStreamsApart_AndItsCharactersIntact()
    {
        var sent = new DaemonResponse(3, "out — with a dash\r\nsecond line\r\n", "error — also dashed\r\n");

        using var stream = new MemoryStream();
        DaemonProtocol.Write(stream, sent);
        stream.Position = 0;

        var back = DaemonProtocol.Read<DaemonResponse>(stream);

        Assert.IsNotNull(back);
        Assert.AreEqual(3, back!.ExitCode);
        Assert.AreEqual(sent.Out, back.Out);
        Assert.AreEqual(sent.Error, back.Error);
    }

    /// <summary>A null request is the ordinary way a connection ends — the client hung up — so it must not
    /// be an exception the server has to guard every call with.</summary>
    [TestMethod]
    public void AClosedStream_ReadsAsNothing()
    {
        using var empty = new MemoryStream();
        Assert.IsNull(DaemonProtocol.Read<DaemonRequest>(empty));
    }

    [TestMethod]
    public void ATruncatedFrame_ReadsAsNothingRatherThanGuessing()
    {
        using var full = new MemoryStream();
        DaemonProtocol.Write(full, new DaemonResponse(0, "a reasonably long body to cut short", ""));

        var bytes = full.ToArray();
        using var cut = new MemoryStream(bytes, 0, bytes.Length - 10);

        Assert.IsNull(DaemonProtocol.Read<DaemonResponse>(cut));
    }

    /// <summary>A length header claiming hundreds of megabytes is a framing error or a peer that is not us.
    /// Allocating for it would be the bug, not reporting it.</summary>
    [TestMethod]
    public void AnAbsurdLengthHeader_IsRefusedRatherThanAllocated()
    {
        using var stream = new MemoryStream();
        stream.Write(BitConverter.GetBytes(int.MaxValue));
        stream.Position = 0;

        Assert.IsNull(DaemonProtocol.Read<DaemonResponse>(stream));
    }

    [TestMethod]
    public void ThePipeName_IsStableForTheSameRootAndBuild()
    {
        Assert.AreEqual(DaemonProtocol.PipeName(@"D:\repo", "stamp"),
                        DaemonProtocol.PipeName(@"D:\repo", "stamp"));
    }

    /// <summary>Two products on one machine are two daemons, or one answers for the other's tree.</summary>
    [TestMethod]
    public void ThePipeName_DiffersByProductRoot()
    {
        Assert.AreNotEqual(DaemonProtocol.PipeName(@"D:\repo", "stamp"),
                           DaemonProtocol.PipeName(@"D:\other", "stamp"));
    }

    /// <summary>The one that matters: rebuild the CLI and it must not reach the daemon running the old one.
    /// Without this a fresh client is told its own new option does not exist.</summary>
    [TestMethod]
    public void ThePipeName_DiffersByBuild_SoARebuiltClientCannotReachAStaleDaemon()
    {
        Assert.AreNotEqual(DaemonProtocol.PipeName(@"D:\repo", "before"),
                           DaemonProtocol.PipeName(@"D:\repo", "after"));
    }

    /// <summary>Same tree reached by a differently-spelled path is the same daemon — otherwise one command
    /// run from a trailing slash starts a second copy of everything.</summary>
    [TestMethod]
    public void ThePipeName_IgnoresHowTheRootWasSpelled()
    {
        var root = Path.GetTempPath();
        Assert.AreEqual(DaemonProtocol.PipeName(root.TrimEnd(Path.DirectorySeparatorChar), "s"),
                        DaemonProtocol.PipeName(root.ToUpperInvariant(), "s"));
    }
}
