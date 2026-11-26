using System.Collections.Concurrent;
using Nexaflow.Icons;
using Nexaflow.IO.Common.Shell;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Shell;

/// <summary>
/// Pictures in files' resources, as views ask for them: each is drawn once per size, off the asking thread, and then
/// answered at once; one the file does not hold never arrives, and is answered as none without asking the file again.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[CoversNode("vcommon-shell-resource-icons")]
public class ShellResourceIconsTests
{
    private static readonly ShellIconRef App = new(@"C:\Apps\app.exe", 0);

    private readonly HeldAnswers _thread = new();

    [TestMethod]
    public void AnIconIsDrawnOnce_AndThenAnsweredAtOnce()
    {
        var drawn = new ConcurrentQueue<string>();
        var icons = Icons(drawn);

        var first = Ask(icons, App, 16, out var arrived);
        Assert.IsNull(first.Icon, "nothing is in hand before it has been drawn");
        Assert.IsNotNull(first.Pending);
        _thread.PumpUntil(() => arrived.Count == 1);

        var second = Ask(icons, App, 16, out _);
        Assert.IsNotNull(second.Icon, "it is in hand now");
        Assert.IsNull(second.Pending, "and there is nothing better to wait for");
        CollectionAssert.AreEqual(new[] { @"C:\Apps\app.exe,0@16" }, drawn.ToArray());
    }

    [TestMethod]
    public void EachSize_IsADrawingOfItsOwn()
    {
        var drawn = new ConcurrentQueue<string>();
        var icons = Icons(drawn);

        Ask(icons, App, 16, out var small);
        Ask(icons, App, 32, out var large);
        _thread.PumpUntil(() => small.Count == 1 && large.Count == 1);

        CollectionAssert.AreEquivalent(new[] { @"C:\Apps\app.exe,0@16", @"C:\Apps\app.exe,0@32" }, drawn.ToArray(),
                                       "a picture drawn small is never stretched to stand for a large one");
    }

    [TestMethod]
    public void AnIconTheFileDoesNotHold_NeverArrives_AndIsNotAskedForAgain()
    {
        var drawn   = new ConcurrentQueue<string>();
        var missing = new ShellIconRef(@"C:\Apps\none.dll", 9);
        var icons   = Icons(drawn, holds: icon => icon != missing);

        Ask(icons, missing, 16, out var lost);
        Ask(icons, App, 16, out var found);
        _thread.PumpUntil(() => found.Count == 1);

        Assert.AreEqual(0, lost.Count, "the view keeps its glyph");
        var again = Ask(icons, missing, 16, out _);
        Assert.IsNull(again.Icon);
        Assert.IsNull(again.Pending, "the file is not asked about twice");
        Assert.AreEqual(2, drawn.Count);
    }

    [TestMethod]
    public void TheShell_DrawsAnIndexAndAResourceId_AtTheSizeAsked() => HeldAnswers.OnSta(() =>
    {
        var byIndex = ShellInterop.ResourceIcon(new ShellIconRef(@"%SystemRoot%\System32\shell32.dll", 3), 24);
        var byId    = ShellInterop.ResourceIcon(new ShellIconRef(@"%SystemRoot%\System32\shell32.dll", -4), 24);
        var none    = ShellInterop.ResourceIcon(new ShellIconRef(@"%SystemRoot%\System32\no-such-file.dll", 0), 24);

        Assert.IsNotNull(byIndex);
        Assert.AreEqual(24, byIndex.Width);
        Assert.IsNotNull(byId, "a negative number is a resource id, as a DefaultIcon value writes one");
        Assert.IsNull(none);
    });

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static ShellResourceIcons Icons(ConcurrentQueue<string> drawn, Func<ShellIconRef, bool>? holds = null) =>
        new((icon, pixels) =>
        {
            drawn.Enqueue($"{icon}@{pixels}");
            return holds is null || holds(icon) ? new ShellIconPixels(1, 1, new byte[4]) : null;
        }, quiet: TimeSpan.FromMilliseconds(30), patience: TimeSpan.FromMilliseconds(100));

    private ShellIconLookup Ask(ShellResourceIcons icons, ShellIconRef icon, int pixels, out List<ShellIconPixels> arrived)
    {
        var landed = new List<ShellIconPixels>();
        arrived = landed;
        return icons.Request(icon, pixels, _thread, landed.Add);
    }
}
