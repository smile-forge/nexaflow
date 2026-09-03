using System;
using System.IO;
using Nexaflow.Services.Initiatives.Hosting.Ipc;
using Nexaflow.Tests.Fixtures;

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
        var sent = new DaemonRequest(["graph", "edit", "substitute", "--find", "a — b"],
                                     @"D:\repo\.claude\worktrees\x", @"D:\repo\src", "piped text")
        { Stop = true };

        using var stream = new MemoryStream();
        DaemonProtocol.Write(stream, sent);
        stream.Position = 0;

        var back = DaemonProtocol.Read<DaemonRequest>(stream);

        Assert.IsNotNull(back);
        CollectionAssert.AreEqual(sent.Args, back!.Args);
        Assert.AreEqual(sent.CodeRoot, back.CodeRoot);
        Assert.AreEqual(sent.WorkingDirectory, back.WorkingDirectory);
        Assert.AreEqual(sent.Stdin, back.Stdin);
        Assert.IsTrue(back.Stop);
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
