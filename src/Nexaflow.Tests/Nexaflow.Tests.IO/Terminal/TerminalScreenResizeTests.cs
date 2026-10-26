using Nexaflow.IO.Terminal;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Terminal;

/// <summary>
/// The screen taking the size the shell is told: rows that no longer fit leave from the top into the scrollback so the
/// cursor's row stays on screen, each row keeps the columns that still fit, and the cursor stays inside the new grid.
/// </summary>
[TestClass]
[CoversNode("ioterm-screen")]
public sealed class TerminalScreenResizeTests
{
    private static TerminalScreen Filled(int cols, int rows, string text)
    {
        var screen = new TerminalScreen(cols, rows);
        screen.Feed(text);
        return screen;
    }

    [TestMethod]
    public void FewerRowsSendTheTopLinesToTheScrollback_AndKeepTheCursorsRow()
    {
        var screen = Filled(20, 5, "a\r\nb\r\nc\r\nd\r\ne");

        screen.Resize(20, 3);

        CollectionAssert.AreEqual(new[] { "a", "b" }, screen.TakeScrollback().ToArray());
        CollectionAssert.AreEqual(new[] { "c", "d", "e" }, screen.Snapshot().ToArray());
        Assert.AreEqual(2, screen.CursorRow);
    }

    [TestMethod]
    public void MoreRowsKeepEverythingWhereItWas()
    {
        var screen = Filled(20, 3, "a\r\nb");

        screen.Resize(20, 10);

        Assert.AreEqual(0, screen.TakeScrollback().Count);
        CollectionAssert.AreEqual(new[] { "a", "b" }, screen.Snapshot().ToArray());
    }

    [TestMethod]
    public void FewerColumnsCutEachRow_AndTheCursorStaysInside()
    {
        var screen = Filled(20, 3, "hello world");

        screen.Resize(5, 3);

        Assert.AreEqual("hello", screen.Snapshot()[0]);
        Assert.AreEqual(4, screen.CursorCol);
    }

    [TestMethod]
    public void TextAfterAResizeLandsOnTheNewGrid()
    {
        var screen = Filled(20, 5, "x");

        screen.Resize(30, 2);
        screen.Feed("\r\nnext");

        CollectionAssert.AreEqual(new[] { "x", "next" }, screen.Snapshot().ToArray());
    }

    [TestMethod]
    public void ASizeOfNothingIsRefused()
        => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TerminalScreen(20, 5).Resize(0, 5));
}
