using System.Linq;
using Nexaflow.IO.Terminal;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// The VT screen buffer. The key case: ConPTY positions command output with cursor moves (CSI row;col H)
/// rather than line feeds, so short output never LF-commits and must be drained from the grid when the
/// next prompt appears — otherwise it's stranded until the screen scrolls (~one page).
/// </summary>
[TestClass]
public class TerminalScreenTests
{
    [TestMethod]
    public void TakeLines_CommitsOnLineFeed()
    {
        var screen = new TerminalScreen(80, 25);
        screen.Feed("LINE1\r\nLINE2\r\n");

        CollectionAssert.AreEqual(new[] { "LINE1", "LINE2" }, screen.TakeLines().ToList());
    }

    [TestMethod]
    public void CursorPositionedOutput_IsNotLineFeedCommitted()
    {
        var screen = new TerminalScreen(80, 25);
        // Write output, then jump the cursor down with CUP (no line feed) — how ConPTY emits short output.
        screen.Feed("HELLO\x1b[5;1H");

        Assert.AreEqual(0, screen.TakeLines().Count, "cursor-positioned output has no LF, so nothing commits");
    }

    [TestMethod]
    public void DrainAboveCursor_FlushesCursorPositionedOutput()
    {
        var screen = new TerminalScreen(80, 25);
        screen.Feed("HELLO\x1b[5;1H");   // "HELLO" at row 0, cursor moved to row 5

        var drained = screen.DrainAboveCursor();

        CollectionAssert.Contains(drained.ToList(), "HELLO");
    }

    [TestMethod]
    public void DrainAboveCursor_IsEmptyWhenNothingStranded()
    {
        var screen = new TerminalScreen(80, 25);
        screen.Feed("LINE1\r\n");        // committed via LF, cursor on a fresh empty row
        screen.TakeLines();

        Assert.AreEqual(0, screen.DrainAboveCursor().Count);
    }
}
