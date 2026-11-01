using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nexaflow.Icons;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Shell;

namespace Nexaflow.Tests.Visuals.Shell;

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

    [TestMethod]
    public void AnIconIsDrawnOnce_AndThenAnsweredAtOnce() => UiThread.Run(() =>
    {
        var drawn = new ConcurrentQueue<string>();
        var icons = Icons(drawn);

        var first = Ask(icons, App, 16, out var arrived);
        Assert.IsNull(first.Icon, "nothing is in hand before it has been drawn");
        Assert.IsNotNull(first.Pending);
        PumpUntil(() => arrived.Count == 1);

        var second = Ask(icons, App, 16, out _);
        Assert.IsNotNull(second.Icon, "it is in hand now");
        Assert.IsNull(second.Pending, "and there is nothing better to wait for");
        CollectionAssert.AreEqual(new[] { @"C:\Apps\app.exe,0@16" }, drawn.ToArray());
    });

    [TestMethod]
    public void EachSize_IsADrawingOfItsOwn() => UiThread.Run(() =>
    {
        var drawn = new ConcurrentQueue<string>();
        var icons = Icons(drawn);

        Ask(icons, App, 16, out var small);
        Ask(icons, App, 32, out var large);
        PumpUntil(() => small.Count == 1 && large.Count == 1);

        CollectionAssert.AreEquivalent(new[] { @"C:\Apps\app.exe,0@16", @"C:\Apps\app.exe,0@32" }, drawn.ToArray(),
                                       "a picture drawn small is never stretched to stand for a large one");
    });

    [TestMethod]
    public void AnIconTheFileDoesNotHold_NeverArrives_AndIsNotAskedForAgain() => UiThread.Run(() =>
    {
        var drawn   = new ConcurrentQueue<string>();
        var missing = new ShellIconRef(@"C:\Apps\none.dll", 9);
        var icons   = Icons(drawn, holds: icon => icon != missing);

        Ask(icons, missing, 16, out var lost);
        Ask(icons, App, 16, out var found);
        PumpUntil(() => found.Count == 1);

        Assert.AreEqual(0, lost.Count, "the view keeps its glyph");
        var again = Ask(icons, missing, 16, out _);
        Assert.IsNull(again.Icon);
        Assert.IsNull(again.Pending, "the file is not asked about twice");
        Assert.AreEqual(2, drawn.Count);
    });

    [TestMethod]
    public void TheShell_DrawsAnIndexAndAResourceId_AtTheSizeAsked() => UiThread.Run(() =>
    {
        var byIndex = ShellInterop.ResourceIcon(new ShellIconRef(@"%SystemRoot%\System32\shell32.dll", 3), 24);
        var byId    = ShellInterop.ResourceIcon(new ShellIconRef(@"%SystemRoot%\System32\shell32.dll", -4), 24);
        var none    = ShellInterop.ResourceIcon(new ShellIconRef(@"%SystemRoot%\System32\no-such-file.dll", 0), 24);

        Assert.IsNotNull(byIndex);
        Assert.IsTrue(byIndex.IsFrozen, "so it can be shown on another thread than the one that drew it");
        Assert.AreEqual(24, byIndex.PixelWidth);
        Assert.IsNotNull(byId, "a negative number is a resource id, as a DefaultIcon value writes one");
        Assert.IsNull(none);
    });

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static ShellResourceIcons Icons(ConcurrentQueue<string> drawn, Func<ShellIconRef, bool>? holds = null) =>
        new((icon, pixels) =>
        {
            drawn.Enqueue($"{icon}@{pixels}");
            return holds is null || holds(icon) ? Picture() : null;
        }, quiet: TimeSpan.FromMilliseconds(30), patience: TimeSpan.FromMilliseconds(100));

    private static ShellIconLookup Ask(ShellResourceIcons icons, ShellIconRef icon, int pixels, out List<ImageSource> arrived)
    {
        var landed = new List<ImageSource>();
        arrived = landed;
        return icons.Request(icon, pixels, Dispatcher.CurrentDispatcher, landed.Add);
    }

    /// <summary>Runs this thread's dispatcher until <paramref name="done"/>, as the window's thread would while it waits.</summary>
    private static void PumpUntil(Func<bool> done)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var frame = new DispatcherFrame();
        var check = new DispatcherTimer(TimeSpan.FromMilliseconds(5), DispatcherPriority.Background,
                                        (_, _) => { if (done() || DateTime.UtcNow > deadline) frame.Continue = false; },
                                        Dispatcher.CurrentDispatcher);
        try { if (!done()) Dispatcher.PushFrame(frame); }
        finally { check.Stop(); }
        Assert.IsTrue(done(), "the icons did not arrive");
    }

    private static ImageSource Picture()
    {
        var picture = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Pbgra32, null, new byte[4], 4);
        picture.Freeze();
        return picture;
    }
}
