using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The host image hook, <see cref="MarkdownSurface.ImageResolver"/>. A document that does not live on disk — the help
/// pane's showcases, read out of a language pack — brings its pictures through it. What is asked in what order, and what a
/// resolver that throws costs, is <c>MarkdownImageTests</c>; this is that the surface hands the hook on. UI category: text
/// is set on an STA thread; no window opens.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("md-image-resolver")]
public class MarkdownImageResolverTests
{
    private const string Doc = "![pic alt](pic.png)\n";

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

    private static BitmapSource Pixel()
    {
        var bmp = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        bmp.Freeze();
        return bmp;
    }
}
