using System.Text;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location;

/// <summary>
/// What a program run on a location's host wrote, as its backend gathers it: chunks cut anywhere become whole lines,
/// reported as each ends, and the program's two streams stay apart.
/// </summary>
[TestClass]
[CoversNode("io-locations-routing")]
public sealed class ProgramOutputTests
{
    private sealed class Lines : IProgress<string>
    {
        public List<string> Seen { get; } = [];
        public void Report(string value) => Seen.Add(value);
    }

    [TestMethod]
    public void ALineCutAcrossChunks_IsReportedOnceWhole_WhenItEnds()
    {
        var lines  = new Lines();
        var output = new ProgramOutput(lines);

        output.Add(false, "Build"u8);
        Assert.AreEqual(0, lines.Seen.Count, "a line not yet ended is not reported");
        output.Add(false, "ing…\r\nDone\n"u8);

        CollectionAssert.AreEqual(new[] { "Building…", "Done" }, lines.Seen, "the line's \\r is left off");
    }

    [TestMethod]
    public void ACharacterCutAcrossChunks_IsDecodedWhole()
    {
        var bytes  = Encoding.UTF8.GetBytes("café\n");
        var output = new ProgramOutput(null);

        output.Add(false, bytes.AsSpan(0, 4));   // inside the é
        output.Add(false, bytes.AsSpan(4));

        Assert.AreEqual("café\n", output.Finish(0).Output);
    }

    [TestMethod]
    public void TheStreamsStayApart_AndAnUnendedLastLineIsReportedAtTheEnd()
    {
        var lines  = new Lines();
        var output = new ProgramOutput(lines);

        output.Add(false, "out 1\n"u8);
        output.Add(true, "err "u8);
        output.Add(false, "out 2"u8);
        output.Add(true, "1\n"u8);
        var exit = output.Finish(3);

        Assert.AreEqual(3, exit.ExitCode);
        Assert.IsFalse(exit.Succeeded);
        Assert.AreEqual("out 1\nout 2", exit.Output);
        Assert.AreEqual("err 1\n", exit.Errors);
        CollectionAssert.AreEqual(new[] { "out 1", "err 1", "out 2" }, lines.Seen);
    }
}
