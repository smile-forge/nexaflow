using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Pdf.Editing;
using Nexaflow.Features.Pdf.Images;
using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Features.Pdf.Rendering;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Fixtures;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// A document's images edited as files: published as a location of the runtime's file system while the document is
/// open, found under the pointer on the page that draws them, read as picture files, and replaced through the file system
/// — the picture drawn in the image's place on every page that draws it, one edit to undo, and written when saved.
/// </summary>
[TestClass]
[CoversNode("pdf-image-edit")]
public class PdfImageEditTests
{
    /// <summary>The pages' height, in points: what turns a place PDF measures up from the bottom into one down from the top.</summary>
    private const double PageHeight = 400;

    private string _dir = string.Empty;
    private readonly List<IDisposable> _open = [];

    /// <summary>The document's images as published, and their publication: withdrawn, as a save or a close withdraws them.</summary>
    private PdfImageLocation _location = null!;
    private IDisposable _published = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(TestTemp.Root, "nexapdfimg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var open in _open) open.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task AnImage_IsFoundWhereItIsDrawn_AndListedOnce_ThoughTwoPagesDrawIt()
    {
        var (path, _, _) = WriteDocument();
        var (_, pages, files, root, _) = await Opened(path);

        await pages.LayAsync(0, CancellationToken.None);

        Assert.IsNotNull(pages.ImageAt(0, new Point(80, PageHeight - 80)), "the JPEG is drawn at 20–140 up the page");
        Assert.IsNull(pages.ImageAt(0, new Point(300, 50)), "nothing is drawn there");

        var listed = new List<string>();
        await foreach (var entry in files.EnumerateAsync(root)) listed.Add(entry.Name);
        Assert.AreEqual(2, listed.Count, "the JPEG both pages draw, once, and the PNG: " + string.Join(", ", listed));
        Assert.AreEqual(1, listed.Count(name => name.EndsWith(".jpg", StringComparison.Ordinal)), "a JPEG the document holds is a JPEG file");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task APictureSavedThroughTheFileSystem_IsDrawnInTheImagesPlace_OnEveryPage_AsOneEdit_AndIsWritten()
    {
        var (path, jpeg, png) = WriteDocument();
        var (session, pages, files, root, images) = await Opened(path);
        await pages.LayAsync(0, CancellationToken.None);
        var image = pages.ImageAt(0, new Point(80, PageHeight - 80))!.Key;
        var file = root + "\\" + images.NameOf(image, ".jpg");

        CollectionAssert.AreEqual(jpeg, await files.ReadAllBytesAsync(file), "the JPEG the document holds, as it is");
        Assert.IsTrue(files.GetBacking(file).WritesInPlace(), "what the image editor saves in place");

        await files.ReplaceAsync(file, png);

        Assert.IsTrue(session.IsDirty);
        var (pngWidth, pngHeight) = SizeOf(png);
        var (jpegWidth, _) = SizeOf(jpeg);
        Assert.AreEqual(pngWidth, (await pages.ImagePictureAsync(1, image, CancellationToken.None))!.PixelWidth,
            "the second page draws the picture in the image's place too");
        CollectionAssert.AreEqual(png, await files.ReadAllBytesAsync(Path.ChangeExtension(file, ".png")), "the file is now the picture");

        session.Undo();
        Assert.IsFalse(session.IsDirty, "one edit, undone");
        Assert.AreEqual(jpegWidth, (await pages.ImagePictureAsync(1, image, CancellationToken.None))!.PixelWidth);

        session.Redo();
        await session.SaveAsync(null, CancellationToken.None);

        using var saved = PdfDocumentScope.TryOpen(File.OpenRead(path), CancellationToken.None)!;
        foreach (var number in new[] { 1, 2 })
        {
            var drawn = saved.Document.GetPage(number).GetImages().Single(found => found.BoundingBox.Left < 100);
            Assert.AreEqual(pngWidth, drawn.WidthInSamples, $"page {number} draws the picture, saved");
            Assert.AreEqual(pngHeight, drawn.HeightInSamples);
            Assert.AreEqual(new PdfRectangle(20, 20, 140, 140), drawn.BoundingBox, "in the place the image had");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task AFileThatIsNoPicture_IsRefused_AndTheDocumentLeftAsItWas()
    {
        var (path, _, _) = WriteDocument();
        var (session, pages, files, root, images) = await Opened(path);
        await pages.LayAsync(0, CancellationToken.None);
        var image = pages.ImageAt(0, new Point(80, PageHeight - 80))!.Key;

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => files.ReplaceAsync(root + "\\" + images.NameOf(image, ".jpg"), [1, 2, 3]).AsTask());
        await Assert.ThrowsExactlyAsync<LocationException>(() => files.WriteAllBytesAsync(root + "\\image-999999.png", [1]).AsTask(),
            "no page draws an image of that name");
        Assert.IsFalse(session.IsDirty);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("images-recipe")]
    public async Task APictureOfADocument_IsSavedInPlaceByTheImageEditor_ItsEditsLastingTheSession()
    {
        var (path, _, _) = WriteDocument();
        var (_, pages, files, root, images) = await Opened(path);
        await pages.LayAsync(0, CancellationToken.None);
        var image = pages.ImageAt(0, new Point(80, PageHeight - 80))!.Key;

        using var services = new ImageEditingServices(files);
        using var edit = new ImageEditSession(root + "\\" + images.NameOf(image, ".jpg"), services);

        Assert.IsTrue(edit.CanReplace, "saved where it is");
        Assert.IsFalse(edit.IsFileBacked, "with no sidecar beside it");
        Assert.IsFalse(edit.CanPersist);
        Assert.IsTrue(edit.SaveIsUndoable, "a save is an edit of the document, so it is not confirmed as an overwrite");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task AnImageOpenElsewhere_HearsItsDocumentsImagesWithdrawn_AndIsGone()
    {
        var (path, _, _) = WriteDocument();
        var (_, pages, files, root, images) = await Opened(path);
        await pages.LayAsync(0, CancellationToken.None);
        var file = root + "\\" + images.NameOf(pages.ImageAt(0, new Point(80, PageHeight - 80))!.Key, ".jpg");

        using var watch = await files.WatchChangesAsync(file, recursive: false);
        Assert.IsNotNull(watch, "a file of a document's images can be watched");
        var told = new TaskCompletionSource();
        watch.Changed += _ => told.TrySetResult();

        _published.Dispose();
        _location.Withdrawn();

        await told.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(await files.ExistsAsync(file), "withdrawn, the file is no longer there");
    }

    /// <summary>Two pages, each drawing the same JPEG at 20–140; the second a PNG too.</summary>
    private (string Path, byte[] Jpeg, byte[] Png) WriteDocument()
    {
        var jpeg = File.ReadAllBytes(FirstImage(".jpg"));
        var png  = File.ReadAllBytes(FirstImage(".png"));

        using var builder = new PdfDocumentBuilder();
        var first = builder.AddPage(PageHeight, PageHeight);
        var shared = first.AddJpeg(jpeg, new PdfRectangle(20, 20, 140, 140));
        var second = builder.AddPage(PageHeight, PageHeight);
        second.AddJpeg(shared, new PdfRectangle(20, 20, 140, 140));
        second.AddPng(png, new PdfRectangle(160, 160, 280, 280));

        var path = Path.Combine(_dir, "written.pdf");
        File.WriteAllBytes(path, builder.Build());
        return (path, jpeg, png);
    }

    /// <summary>The document open as its tab opens it, its images published on a runtime's file system.</summary>
    private async Task<(PdfDocumentSession Session, PdfPageSource Pages, LocationFileSystem Files, string Root, PdfImageFiles Images)> Opened(string path)
    {
        var session = new PdfDocumentSession(path, VirtualFileSystem.Instance);
        _open.Add(session);
        Assert.IsTrue(await session.OpenAsync(CancellationToken.None));

        var pages = new PdfPageSource(session.Arrangement, session.Opened(), Brushes.White);
        session.Changed += (_, next) => pages.Rearrange(next, session.Opened());

        var images = new PdfImageFiles(session, () => pages, action => { action(); return Task.CompletedTask; });
        var files = new LocationFileSystem(VirtualFileSystem.Instance);
        _open.Add(images);
        _location = new PdfImageLocation(images);
        var mount = _location.Mount("pdf-test-" + Guid.NewGuid().ToString("N"), "written.pdf");
        _published = files.Publish(mount);
        _open.Add(_published);
        return (session, pages, files, mount.Root, images);
    }

    private static (int Width, int Height) SizeOf(byte[] picture)
    {
        using var stream = new MemoryStream(picture);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    private static string FirstImage(string extension) =>
        TestSampleData.Files("images").First(p => p.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
}
