using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The host image hook, <see cref="MarkdownRenderContext.ImageResolver"/>. A document that does not live on disk —
/// the help pane's showcases, read out of a language pack — brings its pictures through it. It is asked first, a
/// null answer falls through to <see cref="MarkdownRenderContext.BaseDirectory"/>, and a resolver that throws costs
/// the picture (its alt text shows instead), never the document. UI category: FlowDocuments are built on an STA
/// thread; no window opens.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("md-image-resolver")]
public class MarkdownImageResolverTests
{
    private const string Doc = "![pic alt](pic.png)\n";

    [TestMethod]
    public void Resolver_SuppliesThePicture() => UiThread.Run(() =>
    {
        var pixel = Pixel();
        var doc = Build(src => src == "pic.png" ? pixel : null);

        Assert.AreSame(pixel, Images(doc).Single().Source, "the resolver's own instance should be what renders");
    });

    [TestMethod]
    public void Resolver_WinsOverBaseDirectory() => UiThread.Run(() => WithPngOnDisk(dir =>
    {
        var pixel = Pixel();
        var doc = Build(_ => pixel, dir);

        Assert.AreSame(pixel, Images(doc).Single().Source, "a resolver answer must beat the file on disk");
    }));

    [TestMethod]
    public void NullFromResolver_FallsBackToTheFile() => UiThread.Run(() => WithPngOnDisk(dir =>
    {
        var doc = Build(_ => null, dir);

        var source = Images(doc).Single().Source;
        Assert.IsInstanceOfType(source, typeof(BitmapImage), "null should fall through to BaseDirectory");
        Assert.IsTrue(source.IsFrozen, "a file-loaded picture is frozen so any UI thread can share it");
    }));

    [TestMethod]
    public void ThrowingResolver_ShowsTheAltText() => UiThread.Run(() =>
    {
        var doc = Build(_ => throw new InvalidOperationException("host bug"));

        Assert.AreEqual(0, Images(doc).Count, "a throwing resolver must not render a picture");
        StringAssert.Contains(new TextRange(doc.ContentStart, doc.ContentEnd).Text, "pic alt");
    });

    [TestMethod]
    public void TheSurfaceForwardsTheResolver() => UiThread.Run(() =>
    {
        // The resolver is the host's and the asking is a stage's, so the one thing the view has to do is
        // carry it between them.
        var pixel = Pixel();
        var view = new MarkdownSurface { ImageResolver = _ => pixel };

        view.Markdown = Doc;

        view.Measure(new Size(640, 2000));
        view.Arrange(new Rect(0, 0, 640, 2000));
        view.UpdateLayout();

        var shown = view.Shown;
        var drawn = new List<System.Windows.Media.ImageSource>();

        foreach (var piece in shown.Laid.Root.SelfAndDescendants())
            foreach (var mark in piece.Marks)
                if (mark is Nexaflow.Visuals.Text.Editing.PictureMark picture) drawn.Add(picture.Picture);

        Assert.AreSame(pixel, drawn.Single());
    });

    private static FlowDocument Build(Func<string, ImageSource?> resolver, string? baseDirectory = null)
        => MarkdownFlowDocument.Build(Doc, new MarkdownRenderContext
        {
            Palette       = StyleFormat.Dark,
            ImageResolver = resolver,
            BaseDirectory = baseDirectory,
        });

    private static BitmapSource Pixel()
    {
        var bmp = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        bmp.Freeze();
        return bmp;
    }

    // A real pic.png in a throwaway folder, so the file route is live and a resolver answer has something to beat.
    private static void WithPngOnDisk(Action<string> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "nf-md-img-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Pixel()));
            using (var fs = File.Create(Path.Combine(dir, "pic.png"))) encoder.Save(fs);
            body(dir);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static List<Image> Images(DependencyObject root)
    {
        var found = new List<Image>();
        void Walk(DependencyObject node)
        {
            if (node is Image img) found.Add(img);
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Walk(child);
        }
        Walk(root);
        return found;
    }
}
