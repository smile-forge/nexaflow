using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Features.Common.Viewlets;
using System.Windows.Controls.Primitives;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// Reloading the open folder — what a delete, a paste or a refresh ends in — while the list is scrolled down a folder
/// big enough to stream. The rows are read again into the same virtualizing list the page shows, and the list has to
/// come back where the reader left it, with every row in its place.
/// <para>
/// A reload that emptied the list and then streamed the folder back one row per notification left the list's panel
/// working from the scroll position it had before the rows went: it kept hundreds of rows realized and arranged them
/// with blank bands between, until the tab was closed.
/// </para>
/// <para>UI category: the list is laid out on an STA thread and the dispatcher pumped, so the load's continuations
/// reach the list the way the page's do. No window opens.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("winfs-reload-in-place")]
public class FileListReloadTests
{
    // Over the loader's streaming threshold (1000), so the folder arrives in batches rather than one sorted set.
    private const int Photos     = 1100;
    private const int ScrolledTo = 110;
    private static readonly Size Viewport = new(700, 500);

    private string _scratch = string.Empty;

    [TestInitialize]
    public void CreateScratch()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "nexafs_reload_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
        File.WriteAllText(Path.Combine(_scratch, "album.zip"), "zip");
        for (var i = 0; i < Photos; i++)
            File.WriteAllText(Path.Combine(_scratch, $"photo_{i:D4}.jpg"), "jpg");
    }

    [TestCleanup]
    public void RemoveScratch() { try { Directory.Delete(_scratch, recursive: true); } catch { } }

    [TestMethod]
    public void DeletingAFileWhileScrolledDown_ReloadsInPlace_WithEveryRowWhereItBelongs() => UiThread.Run(() =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

        var vm   = AtPath(_scratch);
        var list = FileList(vm.Entries);
        PumpUntil(() => !vm.IsLoadingEntries && vm.Entries.Count == Photos + 1, "the folder to load");

        var scroll = ScrollerOf(list);
        scroll.ScrollToVerticalOffset(ScrolledTo);
        Lay(list);
        Assert.AreEqual(ScrolledTo, scroll.VerticalOffset, "the list scrolled before the reload");

        // The zip sorts above the rows on screen, as the one in the report did.
        File.Delete(Path.Combine(_scratch, "album.zip"));
        vm.Refresh();
        PumpUntil(() => !vm.IsLoadingEntries && vm.Entries.Count == Photos, "the reload to finish");
        Lay(list);

        Assert.AreEqual(ScrolledTo, scroll.VerticalOffset, "a reload of the open folder keeps the reader's place");

        var rows = RowsOnScreen(list, scroll);
        Assert.IsTrue(rows.Count > 0, "rows are on screen after the reload");
        Assert.AreSame(vm.Entries[ScrolledTo], rows[0].Content, "the top row is the entry the scroll position names");

        for (var i = 1; i < rows.Count; i++)
        {
            Assert.AreSame(vm.Entries[ScrolledTo + i], rows[i].Content, $"row {i} on screen is the next entry, none skipped");
            Assert.AreEqual(LayoutInformation.GetLayoutSlot(rows[i - 1]).Bottom, LayoutInformation.GetLayoutSlot(rows[i]).Top, 0.5,
                $"row {i} starts where the one above it ends — no blank band between them");
        }

        // A healthy panel realizes what is on screen plus a page of cache either side; the broken one held hundreds.
        var realized = RealizedRows(list).Count;
        Assert.IsTrue(realized <= 3 * scroll.ViewportHeight + 2,
            $"{realized} rows realized for a viewport of {scroll.ViewportHeight}");
    });

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FileSystemViewModel AtPath(string path)
    {
        var shell = Substitute.For<IShellServices>();
        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());
        return new FileSystemViewModel(path, shell, Substitute.For<IAIService>(), new Dictionary<Type, IFeatureConfig>());
    }

    /// <summary>The page's file list as far as this concerns it: a recycling, virtualizing grid over the entries.</summary>
    private static ListView FileList(System.Collections.IEnumerable entries)
    {
        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "Name", Width = 300, DisplayMemberBinding = new Binding(nameof(FileSystemEntry.Name)) });
        view.Columns.Add(new GridViewColumn { Header = "Type", Width = 90,  DisplayMemberBinding = new Binding(nameof(FileSystemEntry.TypeLabel)) });
        view.Columns.Add(new GridViewColumn { Header = "Size", Width = 80,  DisplayMemberBinding = new Binding(nameof(FileSystemEntry.SizeLabel)) });

        var list = new ListView { View = view, ItemsSource = entries, SelectionMode = SelectionMode.Extended };
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        Lay(list);
        return list;
    }

    private static void Lay(ListView list)
    {
        list.Measure(Viewport);
        list.Arrange(new Rect(Viewport));
        list.UpdateLayout();
    }

    /// <summary>Runs the dispatcher until <paramref name="done"/>, so the loader's continuations and the layout they
    /// cause are processed in the order the app's dispatcher would process them.</summary>
    private static void PumpUntil(Func<bool> done, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!done())
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, $"timed out waiting for {what}");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            if (!done()) Thread.Sleep(5);
        }
    }

    private static ScrollViewer ScrollerOf(DependencyObject root)
        => Descendants(root).OfType<ScrollViewer>().First();

    private static List<ListViewItem> RealizedRows(ListView list)
        => Descendants(list).OfType<ListViewItem>().ToList();

    /// <summary>The realized rows whose top lies inside the viewport, top to bottom.</summary>
    private static List<ListViewItem> RowsOnScreen(ListView list, ScrollViewer scroll)
    {
        var bottom = scroll.ActualHeight;
        return RealizedRows(list)
            .Where(row => row.Visibility == Visibility.Visible)
            .Select(row => (Row: row, Top: Top(row, scroll)))
            .Where(r => r.Top >= -0.5 && r.Top < bottom)
            .OrderBy(r => r.Top)
            .Select(r => r.Row)
            .ToList();
    }

    private static double Top(UIElement row, ScrollViewer scroll) => row.TranslatePoint(new Point(0, 0), scroll).Y;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var below in Descendants(child)) yield return below;
        }
    }
}
