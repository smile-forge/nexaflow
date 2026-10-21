using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Pdf.Editing;
using Nexaflow.Tests.Fixtures;
using UglyToad.PdfPig;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// The document a tab edits: edits staged on an arrangement of its pages, undone and redone; the whole-document view
/// following them through the working copy; the files they depend on held against writers; and a save writing them,
/// after which the document is the file written. Every test works on its own copy of a sample.
/// </summary>
[TestClass]
public class PdfDocumentSessionTests
{
    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-edit-history")]
    public async Task Edits_AreUndoneAndRedone_AndTheDocumentIsCleanOnceBackAsSaved()
    {
        using var session = await Opened();
        var dirty = new List<bool>();
        session.DirtyChanged += dirty.Add;

        session.Move([2], 0);
        session.Delete([1]);
        Assert.AreEqual("3 2", Numbers(session));
        Assert.IsTrue(session.IsDirty);

        session.Undo();
        session.Undo();
        Assert.AreEqual("1 2 3", Numbers(session));
        Assert.IsFalse(session.IsDirty);
        Assert.IsTrue(session.CanRedo);

        session.Redo();
        Assert.AreEqual("3 1 2", Numbers(session));
        CollectionAssert.AreEqual(new[] { true, false, true }, dirty);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-document-assembly")]
    public async Task TheWholeDocument_IsReadAsEdited()
    {
        using var session = await Opened();

        session.Delete([0]);
        var texts = await session.Document.ReadAsync(scope => scope.Document.GetPages().Select(page => page.Text).ToList(), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "second page", "third page" }, texts);

        session.Undo();
        var count = await session.Document.ReadAsync(scope => scope.Document.NumberOfPages, CancellationToken.None);
        Assert.AreEqual(3, count, "back as saved, the file itself is read");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-edits")]
    public async Task WhileEdited_TheFileIsHeldAgainstWriters_AndLetGoOnceSaved()
    {
        var path = Copy();
        using var session = await Opened(path);

        session.Move([0], 3);
        Assert.ThrowsExactly<IOException>(() => new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose());

        await session.SaveAsync(null, CancellationToken.None);
        new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose();
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-save")]
    public async Task Saving_WritesTheEdits_AndTheDocumentIsThatFileFromThen()
    {
        var path = Copy();
        using var session = await Opened(path);
        session.Move([2], 0);
        var ids = session.Arrangement.Pages.Select(page => page.Id).ToList();

        await session.SaveAsync(null, CancellationToken.None);

        Assert.IsFalse(session.IsDirty);
        Assert.IsFalse(session.CanUndo, "the history starts again from the file saved");
        CollectionAssert.AreEqual(ids, session.Arrangement.Pages.Select(page => page.Id).ToList(), "every page keeps its id");
        using var saved = PdfDocument.Open(path);
        Assert.AreEqual("third page", saved.GetPage(1).Text);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-save")]
    public async Task SavingAs_LeavesTheFileAsItWas_AndMovesTheDocumentToTheNewOne()
    {
        var path = Copy();
        var target = Path.Combine(TestTemp.CreateSubdirectory().FullName, "edited.pdf");
        using var session = await Opened(path);
        string? moved = null;
        session.Moved += to => moved = to;
        session.Delete([0]);

        await session.SaveAsync(target, CancellationToken.None);

        Assert.AreEqual(target, moved);
        Assert.AreEqual(target, session.Base.Path);
        using (var original = PdfDocument.Open(path)) Assert.AreEqual(3, original.NumberOfPages);
        using (var written = PdfDocument.Open(target)) Assert.AreEqual(2, written.NumberOfPages);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-insert-image")]
    public async Task APicture_IsInsertedAsAPageTheSizeOfItsNeighbour()
    {
        using var session = await Opened();
        var picture = TestTemp.CreateFile(".png");
        File.WriteAllBytes(picture, Png());

        await session.InsertImagesAsync(1, [picture], CancellationToken.None);

        Assert.AreEqual(4, session.Arrangement.Count);
        Assert.AreEqual(session.PageSize(0), session.PageSize(1));
        Assert.AreNotEqual(session.Arrangement[0].Source, session.Arrangement[1].Source);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-insert-pdf")]
    public async Task AnotherPdf_IsInsertedWhole_AndOneThatCannotBeReadIsRefused()
    {
        using var session = await Opened();

        await session.InsertPdfsAsync(3, [Copy("text.pdf")], CancellationToken.None);
        Assert.AreEqual(4, session.Arrangement.Count);

        var refused = await Assert.ThrowsExactlyAsync<PdfEditRefusedException>(
            () => session.InsertPdfsAsync(0, [Copy("corrupt.pdf")], CancellationToken.None));
        Assert.AreEqual(PdfEditRefusal.Unreadable, refused.Reason);
        Assert.AreEqual(4, session.Arrangement.Count, "a refused insert changes nothing");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-insert-image")]
    public async Task AFileThatIsNoPicture_IsRefused()
    {
        using var session = await Opened();
        var notAPicture = TestTemp.CreateFile(".jpg");
        File.WriteAllText(notAPicture, "not a picture");

        var refused = await Assert.ThrowsExactlyAsync<PdfEditRefusedException>(
            () => session.InsertImagesAsync(0, [notAPicture], CancellationToken.None));

        Assert.AreEqual(PdfEditRefusal.NotAPicture, refused.Reason);
        Assert.IsFalse(session.IsDirty);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-edits")]
    public async Task TheDraftsOfAnEdit_AreGoneOnceSavedOrClosed()
    {
        var path = Copy();
        var session = await Opened(path);
        var picture = TestTemp.CreateFile(".png");
        File.WriteAllBytes(picture, Png());
        await session.InsertImagesAsync(0, [picture], CancellationToken.None);
        var draft = session.Source(session.Arrangement[0].Source).Path;
        Assert.IsTrue(File.Exists(draft));

        await session.SaveAsync(null, CancellationToken.None);
        Assert.IsFalse(File.Exists(draft), "saved: the picture's page is in the file, its draft gone");

        await session.InsertImagesAsync(0, [picture], CancellationToken.None);
        draft = session.Source(session.Arrangement[0].Source).Path;
        session.Dispose();
        Assert.IsFalse(File.Exists(draft), "closed: the draft goes with the tab");
    }

    private static async Task<PdfDocumentSession> Opened(string? path = null)
    {
        var session = new PdfDocumentSession(path ?? Copy());
        Assert.IsTrue(await session.OpenAsync(CancellationToken.None));
        return session;
    }

    private static string Copy(string sample = "outline.pdf")
    {
        var path = Path.Combine(TestTemp.CreateSubdirectory().FullName, sample);
        File.Copy(TestSampleData.Path("pdf", sample), path);
        return path;
    }

    private static string Numbers(PdfDocumentSession session) => string.Join(" ", session.Arrangement.Pages.Select(page => page.Number));

    private static byte[] Png()
    {
        var pixels = new byte[30 * 20 * 4];
        Array.Fill(pixels, (byte)180);
        var picture = BitmapSource.Create(30, 20, 96, 96, PixelFormats.Bgra32, null, pixels, 30 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
