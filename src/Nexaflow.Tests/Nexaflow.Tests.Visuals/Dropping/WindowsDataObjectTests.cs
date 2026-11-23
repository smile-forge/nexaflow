using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Interaction;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Dropping;

namespace Nexaflow.Tests.Visuals.Dropping;

/// <summary>
/// What the app puts on the clipboard, written in the formats other applications read and read back as the same kinds —
/// the clipboard's two halves meeting in one vocabulary.
/// </summary>
[TestClass]
[CoversNode("shell-clipboard")]
public class WindowsDataObjectTests
{
    private static IDragPayload RoundTrip(IDragPayload payload) => new WindowsDragPayload(WindowsDataObject.From(payload));

    private static byte[] OnePixelPng()
    {
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 128 }, 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);
        return png.ToArray();
    }

    [TestMethod]
    public void Words_AreReadBackAsTheyWereWritten() => UiThread.Run(() =>
    {
        var data = WindowsDataObject.From(ComposedPayload.OfText("naïve — café"));

        Assert.AreEqual("naïve — café", data.GetData(DataFormats.UnicodeText));
        Assert.AreEqual("naïve — café", RoundTrip(ComposedPayload.OfText("naïve — café")).Text());
    });

    [TestMethod]
    public void APicture_KeepsItsTransparency_AndIsOfferedAsABitmapToo() => UiThread.Run(() =>
    {
        var png = OnePixelPng();
        var data = WindowsDataObject.From(ComposedPayload.OfImage(png));

        Assert.IsTrue(data.GetDataPresent(DataFormats.Bitmap), "for what reads only a bitmap");
        CollectionAssert.AreEqual(png, RoundTrip(ComposedPayload.OfImage(png)).Image(), "the PNG as written, alpha and all");
    });

    [TestMethod]
    public void FilesCopied_AreNotCut_AndFilesCut_SaySo() => UiThread.Run(() =>
    {
        string[] paths = [@"C:\a.txt", @"C:\b folder"];

        var copied = RoundTrip(ComposedPayload.OfFiles(paths, cut: false));
        var cut    = RoundTrip(ComposedPayload.OfFiles(paths, cut: true));

        CollectionAssert.AreEqual(paths, copied.Files().ToArray());
        Assert.IsFalse(copied.Contains(DragKinds.FilesCut));
        CollectionAssert.AreEqual(paths, cut.Files().ToArray());
        Assert.IsTrue(cut.Contains(DragKinds.FilesCut), "a paste moves what was cut, here or in Explorer");
    });

    [TestMethod]
    public void HtmlAndWords_TravelTogether() => UiThread.Run(() =>
    {
        var payload = ComposedPayload.OfText("bold").With(DragKinds.Html, "<b>bold</b>");

        var read = RoundTrip(payload);

        Assert.AreEqual("bold", read.Text());
        Assert.AreEqual("<b>bold</b>", read.Html());
    });

    [TestMethod]
    public void NothingComposed_PutsNothingThere() => UiThread.Run(() =>
    {
        Assert.AreEqual(0, WindowsDataObject.From(ComposedPayload.Empty).GetFormats().Length);
    });
}
