using System.Linq;
using Nexaflow.IO.Terminal;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Terminal;

/// <summary>
/// The VT screen buffer behaves like a real terminal screen: on-screen lines stay in the grid (rendered
/// via <see cref="TerminalScreen.Snapshot"/>) and only lines that scroll off the top become scrollback.
/// This is what lets short, cursor-positioned ConPTY output (which never line-feed-commits) render at all.
/// </summary>
[TestClass]
[CoversNode("ioterm-screen")]
public class TerminalScreenTests
{
    [TestMethod]
    public void OnScreenLines_StayInSnapshot_NotScrollback()
    {
        var screen = new TerminalScreen(80, 25);
        screen.Feed("LINE1\r\nLINE2\r\n");

        Assert.AreEqual(0, screen.TakeScrollback().Count, "nothing scrolled off a 25-row screen");
        var snap = screen.Snapshot().ToList();
        CollectionAssert.Contains(snap, "LINE1");
        CollectionAssert.Contains(snap, "LINE2");
    }

    [TestMethod]
    public void CursorPositionedOutput_AppearsInSnapshot()
    {
        var screen = new TerminalScreen(80, 25);
        // Output positioned with CUP rather than a line feed — how ConPTY emits short output.
        screen.Feed("HELLO\x1b[5;1H");

        CollectionAssert.Contains(screen.Snapshot().ToList(), "HELLO");
    }

    [TestMethod]
    public void Scrolling_EvictsTopLinesToScrollback()
    {
        var screen = new TerminalScreen(80, 5);   // small screen forces a scroll
        for (int i = 1; i <= 7; i++) screen.Feed($"L{i}\r\n");

        var scrollback = screen.TakeScrollback().ToList();
        CollectionAssert.Contains(scrollback, "L1");
        CollectionAssert.Contains(scrollback, "L3");

        CollectionAssert.Contains(screen.Snapshot().ToList(), "L7");
    }

    [TestMethod]
    public void PeekPromptLine_DetectsPromptOnCursorRow()
    {
        var screen = new TerminalScreen(80, 25);
        screen.Feed("C:\\Windows>");

        Assert.AreEqual("C:\\Windows>", screen.PeekPromptLine());
    }
}
