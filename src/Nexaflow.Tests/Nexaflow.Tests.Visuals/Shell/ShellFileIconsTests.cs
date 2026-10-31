using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Shell;

namespace Nexaflow.Tests.Visuals.Shell;

/// <summary>
/// Explorer's icons for a list, as its rows ask for them: a kind of file is drawn once and then answered at once, a burst
/// of requests is drawn newest first once it settles, a row that has moved on is never answered, and a file that draws its
/// own icon is drawn by its path and kept — up to a bound.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[CoversNode("vcommon-shell-icons")]
public class ShellFileIconsTests
{
    [TestMethod]
    public void AKindOfFileIsDrawnOnce_AndThenAnsweredAtOnce() => UiThread.Run(() =>
    {
        var shell = new FakeShell();
        var icons = Icons(shell);

        var first = Ask(icons, "a.txt", nameOnly: true, out var arrived);
        Assert.IsNull(first.Icon, "nothing is in hand before the kind has been drawn");
        Assert.IsNotNull(first.Pending);
        PumpUntil(() => arrived.Count == 1);

        var second = Ask(icons, "b.txt", nameOnly: true, out _);
        Assert.IsNotNull(second.Icon, "the kind is in hand now");
        Assert.IsNull(second.Pending, "and there is nothing better to wait for");
        CollectionAssert.AreEqual(new[] { "type:.txt" }, shell.Drawn.ToArray());
    });

    [TestMethod]
    public void ABurstOfRequests_IsDrawnNewestFirst_OnceItSettles() => UiThread.Run(() =>
    {
        var shell = new FakeShell();
        var icons = Icons(shell);

        Ask(icons, "a.one", nameOnly: true, out var one);
        Ask(icons, "b.two", nameOnly: true, out var two);
        Ask(icons, "c.three", nameOnly: true, out var three);
        PumpUntil(() => one.Count + two.Count + three.Count == 3);

        CollectionAssert.AreEqual(new[] { "type:.three", "type:.two", "type:.one" }, shell.Drawn.ToArray(),
                                  "the newest rows are the ones on screen");
    });

    [TestMethod]
    public void ARowThatMovesOn_IsNeverAnswered_AndItsIconNeverDrawn() => UiThread.Run(() =>
    {
        var shell = new FakeShell();
        var icons = Icons(shell);

        var left = Ask(icons, "x.txt", nameOnly: true, out var leftArrived);
        left.Pending!.Dispose();
        Ask(icons, "y.md", nameOnly: true, out var stayed);
        PumpUntil(() => stayed.Count == 1);

        Assert.AreEqual(0, leftArrived.Count);
        CollectionAssert.DoesNotContain(shell.Drawn.ToArray(), "type:.txt");
    });

    [TestMethod]
    public void RowsAskingForTheSameKind_ShareOneDrawing() => UiThread.Run(() =>
    {
        var shell = new FakeShell();
        var icons = Icons(shell);

        Ask(icons, "a.txt", nameOnly: true, out var a);
        Ask(icons, "b.txt", nameOnly: true, out var b);
        PumpUntil(() => a.Count == 1 && b.Count == 1);

        Assert.AreSame(a[0], b[0]);
        CollectionAssert.AreEqual(new[] { "type:.txt" }, shell.Drawn.ToArray());
    });

    [TestMethod]
    public void AProgramIsDrawnByItsPath_AndKept() => UiThread.Run(() =>
    {
        var shell = new FakeShell();
        var icons = Icons(shell);

        Ask(icons, @"C:\p\app.exe", nameOnly: false, out var arrived);
        PumpUntil(() => arrived.Count == 1);
        var again = Ask(icons, @"C:\p\app.exe", nameOnly: false, out _);

        CollectionAssert.Contains(shell.Drawn.ToArray(), @"path:C:\p\app.exe");
        Assert.IsNotNull(again.Icon);
        Assert.IsNull(again.Pending, "kept, so answered at once");
    });

    [TestMethod]
    public void AFileThatSharesItsKindsIcon_IsNeverDrawnByItsPath() => UiThread.Run(() =>
    {
        var shell = new FakeShell();
        var icons = Icons(shell);

        Ask(icons, @"C:\d\a.txt", nameOnly: false, out var arrived);
        PumpUntil(() => arrived.Count == 1);
        var next = Ask(icons, @"C:\d\b.txt", nameOnly: false, out _);

        Assert.IsNotNull(next.Icon);
        Assert.IsNull(next.Pending, "once its kind is known not to draw its own, a file is answered from its kind");
        Assert.IsFalse(shell.Drawn.Any(drawn => drawn.StartsWith("path:", StringComparison.Ordinal)));
    });

    [TestMethod]
    public void ACustomisedFolder_WearsItsOwnIcon_AndAPlainOneItsKinds() => UiThread.Run(() =>
    {
        var shell = new FakeShell { CustomizedFolders = { @"C:\Users\me\Documents" } };
        var icons = Icons(shell);

        Ask(icons, @"C:\Users\me\Documents", nameOnly: false, out var custom, isFolder: true);
        Ask(icons, @"C:\Users\me\work", nameOnly: false, out var plain, isFolder: true);
        PumpUntil(() => custom.Count == 1 && plain.Count == 1);

        CollectionAssert.Contains(shell.Drawn.ToArray(), @"path:C:\Users\me\Documents");
        CollectionAssert.DoesNotContain(shell.Drawn.ToArray(), @"path:C:\Users\me\work");
        CollectionAssert.Contains(shell.Drawn.ToArray(), "type:folder");
    });

    [TestMethod]
    public void AFileThatChanged_IsAskedAboutAgain() => UiThread.Run(() =>
    {
        var shell = new FakeShell();
        var icons = Icons(shell);

        Ask(icons, @"C:\p\app.exe", nameOnly: false, out var arrived, stamp: 1);
        PumpUntil(() => arrived.Count == 1);

        Assert.IsNotNull(Ask(icons, @"C:\p\app.exe", nameOnly: false, out _, stamp: 2).Pending, "a rebuilt program may wear a new icon");
    });

    [TestMethod]
    public void OneOffIcons_AreKeptUpToTheirBound_LeastRecentGoingFirst() => UiThread.Run(() =>
    {
        var shell = new FakeShell();
        var icons = Icons(shell, pathCapacity: 2);

        foreach (var program in new[] { @"C:\p\a.exe", @"C:\p\b.exe", @"C:\p\c.exe" })
        {
            Ask(icons, program, nameOnly: false, out var arrived);
            PumpUntil(() => arrived.Count == 1);
        }

        Assert.IsNotNull(Ask(icons, @"C:\p\a.exe", nameOnly: false, out _).Pending, "the least recent was let go");
        Assert.IsNull(Ask(icons, @"C:\p\c.exe", nameOnly: false, out _).Pending, "the most recent is kept");
    });

    // ── Against the shell itself ─────────────────────────────────────────────────

    [TestMethod]
    public void TheShellNamesAKindOfFile_AsExplorersTypeColumnDoes()
    {
        string text = ShellFileTypes.NameFor(".txt", isFolder: false);

        Assert.IsFalse(string.IsNullOrWhiteSpace(text));
        Assert.AreNotEqual("TXT", text, "the shell's name, not the extension");
        Assert.IsFalse(string.IsNullOrWhiteSpace(ShellFileTypes.NameFor(string.Empty, isFolder: true)), "a folder has one too");
    }

    [TestMethod]
    public void ProgramsAndShortcutsDrawTheirOwnIcons_ADocumentDoesNot()
    {
        var shell = new SystemShellIconSource();

        Assert.IsTrue(shell.DrawsItsOwnIcon(".exe"));
        Assert.IsTrue(shell.DrawsItsOwnIcon(".lnk"));
        Assert.IsFalse(shell.DrawsItsOwnIcon(".txt"));
        Assert.IsFalse(shell.DrawsItsOwnIcon(string.Empty));
    }

    [TestMethod]
    public void TheShellDrawsAKindsIcon_AsAFrozenPictureAtItsSmallSize() => UiThread.Run(() =>
    {
        var icon = new SystemShellIconSource().TypeIcon(".txt", isFolder: false, ShellIconSize.Small) as BitmapSource;

        Assert.IsNotNull(icon);
        Assert.IsTrue(icon.IsFrozen, "so it can be shown on another thread than the one that drew it");
        Assert.AreEqual(ShellInterop.SmallIconPixels, icon.PixelWidth);
    });

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static ShellFileIcons Icons(FakeShell shell, int pathCapacity = 64) =>
        new(shell, quiet: TimeSpan.FromMilliseconds(30), patience: TimeSpan.FromMilliseconds(100), pathCapacity);

    private static ShellIconLookup Ask(ShellFileIcons icons, string path, bool nameOnly, out List<ImageSource> arrived,
                                      bool isFolder = false, long stamp = 0)
    {
        var landed = new List<ImageSource>();
        arrived = landed;
        return icons.Request(new FileIconQuery(path, isFolder, nameOnly, stamp, ShellIconSize.Small), Dispatcher.CurrentDispatcher, landed.Add);
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

    /// <summary>A shell that records what it was asked to draw, in order.</summary>
    private sealed class FakeShell : IShellIconSource
    {
        public ConcurrentQueue<string> Drawn { get; } = new();
        public HashSet<string> CustomizedFolders { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ImageSource? TypeIcon(string extension, bool isFolder, ShellIconSize size)
        {
            Drawn.Enqueue(isFolder ? "type:folder" : "type:" + extension);
            return Picture();
        }

        public ImageSource? PathIcon(string path, ShellIconSize size)
        {
            Drawn.Enqueue("path:" + path);
            return Picture();
        }

        public bool DrawsItsOwnIcon(string extension) => extension == ".exe";

        public bool IsCustomizedFolder(string path) => CustomizedFolders.Contains(path);

        private static ImageSource Picture()
        {
            var picture = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Pbgra32, null, new byte[4], 4);
            picture.Freeze();
            return picture;
        }
    }
}
