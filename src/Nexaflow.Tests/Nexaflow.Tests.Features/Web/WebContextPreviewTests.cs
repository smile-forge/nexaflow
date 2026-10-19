using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Web.ViewModels;
using Nexaflow.Features.Web.Views;
using Nexaflow.Tests.Features.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Web;

/// <summary>
/// The Web page's context preview: a snapshot of the page as it was last loaded, its title and its address. The
/// live browser cannot be shown twice, so the page keeps a small picture of itself; the view takes it on each
/// load through <see cref="HtmlViewModel.CaptureScreenshotAsync"/>, which here stands in for the browser.
/// </summary>
[TestClass]
[TestCategory("Unit")]
[CoversNode("web-viewer-ai-preview")]
public class WebContextPreviewTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup() => _tempDir = Directory.CreateDirectory(
        Path.Combine(TestTemp.Root, "nexaflow-webpreview-" + Guid.NewGuid().ToString("N"))).FullName;

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private HtmlViewModel Page()
    {
        var path = Path.Combine(_tempDir, "page.html");
        File.WriteAllText(path, "<html><body>hi</body></html>");
        return new HtmlViewModel(path);
    }

    /// <summary>A browser that answers each capture with the next picture in <paramref name="pictures"/>,
    /// noting the longest edge each one asked for.</summary>
    private static List<double> Browser(HtmlViewModel vm, params byte[]?[] pictures)
    {
        var asked = new List<double>();
        var next  = 0;
        vm.CaptureScreenshotAsync = (edge, _) =>
        {
            asked.Add(edge);
            return Task.FromResult(pictures[Math.Min(next++, pictures.Length - 1)]);
        };
        return asked;
    }

    private static byte[] Png(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    [TestMethod]
    public async Task ALoadedPage_IsPictured_SmallerThanTheAiIsShown()
    {
        var vm    = Page();
        var asked = Browser(vm, Png(48, 30));

        await vm.TakeSnapshotAsync(CancellationToken.None);
        var tool = vm.GetClientTools().Single(t => t.Name == "capture_web_page");
        await tool.InvokeAsync(new JsonObject(), CancellationToken.None);

        Assert.IsInstanceOfType(vm.Snapshot, typeof(BitmapSource));
        Assert.AreEqual(48, ((BitmapSource)vm.Snapshot!).PixelWidth);
        Assert.IsTrue(vm.Snapshot!.IsFrozen, "frozen, so the preview can bind it from wherever it is built");
        Assert.AreEqual(2, asked.Count);
        Assert.IsTrue(asked[0] < asked[1], "the snapshot is only to recognise the page; the AI's capture is to read it");
    }

    [TestMethod]
    public async Task ACaptureThatComesBackEmpty_OrIsOvertaken_LeavesTheLastSnapshot()
    {
        var vm = Page();
        Browser(vm, Png(16, 10), null, Png(32, 20));

        await vm.TakeSnapshotAsync(CancellationToken.None);
        var first = vm.Snapshot;
        await vm.TakeSnapshotAsync(CancellationToken.None);
        Assert.AreSame(first, vm.Snapshot, "nothing came back");

        await vm.TakeSnapshotAsync(new CancellationToken(canceled: true));
        Assert.AreSame(first, vm.Snapshot, "the next navigation cancelled it");
    }

    [TestMethod]
    public async Task WithoutABrowser_NothingIsCaptured()
    {
        var vm    = Page();
        var asked = Browser(vm, Png(16, 10));
        vm.WebViewAvailable = false;

        await vm.TakeSnapshotAsync(CancellationToken.None);

        Assert.AreEqual(0, asked.Count);
        Assert.IsNull(vm.Snapshot);
    }

    [TestMethod]
    public void ThePreview_RemindsOfThePage_ByItsTitleAndAddress_AndIsFreshEachTime()
    {
        UiThread.Run(() =>
        {
            var vm = Page();

            var untitled = ContextPreviewProbe.VisibleTexts(vm.CreateContextPreview());
            CollectionAssert.Contains(untitled.ToList(), "page.html", "before its title is known, the page is called by its file");

            vm.PageTitle = "Release notes";
            var first  = vm.CreateContextPreview();
            var second = vm.CreateContextPreview();

            Assert.IsInstanceOfType(first, typeof(WebContextPreview));
            Assert.AreSame(vm, first.DataContext, "the preview binds to the page's own snapshot and title");
            Assert.AreNotSame(first, second, "a fresh control per selection; the last one was discarded");
            CollectionAssert.IsSubsetOf(new[] { "Release notes", vm.CurrentUrl },
                ContextPreviewProbe.VisibleTexts(first).ToList());
        });
    }
}
