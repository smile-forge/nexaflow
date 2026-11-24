using System.Collections.Generic;
using System.Linq;
using Nexaflow.IO.Terminal;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Terminal;

/// <summary>
/// What a shell reports rather than draws: the folder it works in (OSC 7) and the marks of its prompt cycle (OSC 133) reach
/// the caller as they are read — ended by BEL or by ST, whole however the output is split — and draw nothing. A report the
/// screen cannot read, and every other OSC, is dropped.
/// </summary>
[TestClass]
[CoversNode("ioterm-screen")]
public sealed class TerminalScreenOscTests
{
    private const string Esc = "\x1b";
    private const string St  = "\x1b\\";
    private const string Bel = "\x07";

    private readonly TerminalScreen _screen = new(80, 5);
    private readonly List<TerminalWorkingFolder> _folders = [];
    private readonly List<ShellMark> _marks = [];

    [TestInitialize]
    public void Listen()
    {
        _screen.WorkingFolderReported += _folders.Add;
        _screen.ShellMarked           += _marks.Add;
    }

    [TestMethod]
    public void AFolderReportEndedByST_IsTheHostAndThePath()
    {
        _screen.Feed($"{Esc}]7;file://probe/home/adam{St}");

        Assert.AreEqual(new TerminalWorkingFolder("probe", "/home/adam"), _folders.Single());
    }

    [TestMethod]
    public void AFolderReportEndedByBEL_ReadsTheSame()
    {
        _screen.Feed($"{Esc}]7;file://probe/home/adam{Bel}");

        Assert.AreEqual(new TerminalWorkingFolder("probe", "/home/adam"), _folders.Single());
    }

    [TestMethod]
    public void AReportSplitAcrossReads_ArrivesOnceWhole()
    {
        _screen.Feed($"{Esc}]7;file://pro");
        _screen.Feed("be/home/adam");
        _screen.Feed(Esc);
        Assert.AreEqual(0, _folders.Count, "nothing is reported before the terminator");
        _screen.Feed("\\after");

        Assert.AreEqual("/home/adam", _folders.Single().Path);
        Assert.AreEqual("after", _screen.Snapshot()[0], "the text after the report draws as usual");
    }

    [TestMethod]
    public void APercentEncodedPath_IsDecodedAsUtf8_SpacesAndAll()
    {
        // "/srv/a b/café/日本" as the shims encode it: every byte outside [A-Za-z0-9/._~-] as %XX.
        _screen.Feed($"{Esc}]7;file://probe/srv/a%20b/caf%C3%A9/%E6%97%A5%E6%9C%AC{St}");

        Assert.AreEqual("/srv/a b/café/日本", _folders.Single().Path);
    }

    [TestMethod]
    public void AMalformedFolderReport_IsDropped_AndDrawsNothing()
    {
        _screen.Feed($"{Esc}]7;file://probe/srv/%ZZ{St}");          // not a percent escape
        _screen.Feed($"{Esc}]7;file://probe/srv/%C3%28{St}");       // not UTF-8
        _screen.Feed($"{Esc}]7;http://probe/srv{St}");              // not a file URI
        _screen.Feed($"{Esc}]7;file://probe{St}");                  // no path
        _screen.Feed("$ ");

        Assert.AreEqual(0, _folders.Count);
        Assert.AreEqual("$", _screen.Snapshot()[0]);
    }

    [TestMethod]
    public void PromptMarks_ArriveInOrder_WithTheExitCodeAndWhereTheCursorStood()
    {
        _screen.Feed($"{Esc}]133;D;2{St}{Esc}]133;A{St}adam$ {Esc}]133;B{Bel}");
        _screen.Feed($"ls\r\n{Esc}]133;C{St}");

        CollectionAssert.AreEqual(
            new[] { ShellMarkKind.CommandEnd, ShellMarkKind.PromptStart, ShellMarkKind.InputStart, ShellMarkKind.OutputStart },
            _marks.Select(m => m.Kind).ToArray());
        Assert.AreEqual(2, _marks[0].ExitCode);
        Assert.AreEqual(new ShellMark(ShellMarkKind.InputStart, null, 0, 6), _marks[2], "typing starts after \"adam$ \"");
        Assert.AreEqual(1, _marks[3].Line, "the output starts on the next line");
    }

    [TestMethod]
    public void AMarksLine_StaysTrueAsLinesScrollOff()
    {
        for (int i = 0; i < 7; i++) _screen.Feed($"line {i}\r\n");   // a 5-row screen: three lines have scrolled off
        _screen.Feed($"{Esc}]133;A{St}$ ");

        var mark = _marks.Single();
        Assert.AreEqual(3, _screen.TopLine);
        Assert.AreEqual(_screen.CursorRow, (int)(mark.Line - _screen.TopLine), "the mark's line is the cursor's row");
    }

    [TestMethod]
    public void AnUnknownMarkOrOsc_IsDropped()
    {
        _screen.Feed($"{Esc}]133;Z{St}{Esc}]133;AB{St}{Esc}]0;window title{Bel}{Esc}]8;;https://example.com{St}text");

        Assert.AreEqual(0, _marks.Count);
        Assert.AreEqual(0, _folders.Count);
        Assert.AreEqual("text", _screen.Snapshot()[0]);
    }

    [TestMethod]
    public void AnOscCutShortByAnotherEscape_GivesWayToIt()
    {
        _screen.Feed($"{Esc}]7;file://probe/x{Esc}[2Cok");

        Assert.AreEqual(0, _folders.Count, "an OSC with no terminator reports nothing");
        Assert.AreEqual("  ok", _screen.Snapshot()[0], "the escape that cut it in is applied");
    }
}
