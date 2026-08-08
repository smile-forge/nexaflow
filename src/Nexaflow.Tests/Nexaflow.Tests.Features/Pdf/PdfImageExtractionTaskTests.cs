using System.IO;
using System.Linq;
using Nexaflow.Features.Pdf.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// The shell background task behind "Extract images": where the files land, and what it reports back so the
/// completion toast can tell "this PDF has no images" from "this PDF couldn't be read".
/// </summary>
[TestClass]
[CoversNode("pdf-extract-images")]
public class PdfImageExtractionTaskTests
{
    private string _target = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _target = Path.Combine(Path.GetTempPath(), "nexapdfimg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_target);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_target)) Directory.Delete(_target, recursive: true);
    }

    private static string Sample(string name) => TestSampleData.Path("pdf", name);

    [TestMethod]
    public async Task WritesImagesIntoASubfolderNamedAfterTheDocument()
    {
        var task = new PdfImageExtractionTask([Sample("image-only.pdf")], _target);
        await task.RunAsync(default);

        // A subfolder rather than the target root, so extracting from several PDFs — or twice into the same
        // folder — never has two documents fighting over p001-01.png.
        var destination = Path.Combine(_target, "image-only");
        Assert.IsTrue(Directory.Exists(destination));
        CollectionAssert.AreEquivalent(
            new[] { "p001-01.png" },
            Directory.GetFiles(destination).Select(Path.GetFileName).ToArray());

        var result = task.Results.Single();
        Assert.AreEqual(1, result.Extracted);
        Assert.IsNull(result.Error);
        Assert.AreEqual(destination, result.Destination);
    }

    [TestMethod]
    public async Task LeavesNoPartFilesBehind()
    {
        var task = new PdfImageExtractionTask([Sample("image-only.pdf")], _target);
        await task.RunAsync(default);

        // Each image is written to a sidecar name and moved into place, so a failure part-way through can't
        // leave a truncated file masquerading as a successful extraction.
        Assert.AreEqual(0, Directory.GetFiles(_target, "*.part", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task RepeatedImage_WritesOneFileAndReportsTheSkip()
    {
        var task = new PdfImageExtractionTask([Sample("repeated-image.pdf")], _target);
        await task.RunAsync(default);

        Assert.AreEqual(1, Directory.GetFiles(Path.Combine(_target, "repeated-image")).Length);

        var result = task.Results.Single();
        Assert.AreEqual(1, result.Extracted);
        Assert.AreEqual(1, result.Duplicates,
            "reported rather than hidden — silence would make the count look like data loss");
    }

    [TestMethod]
    public async Task PdfWithNoImages_CreatesNoFolder()
    {
        var task = new PdfImageExtractionTask([Sample("text.pdf")], _target);
        await task.RunAsync(default);

        // An empty directory would leave the user wondering what went wrong; the toast says it plainly instead.
        Assert.AreEqual(0, Directory.GetDirectories(_target).Length);

        var result = task.Results.Single();
        Assert.AreEqual(0, result.Extracted);
        Assert.IsNull(result.Destination);
        Assert.IsNull(result.Error, "no images is not a failure");
    }

    [TestMethod]
    public async Task UnreadablePdf_IsReportedAsAnError_NotAsZeroImages()
    {
        var task = new PdfImageExtractionTask([Sample("corrupt.pdf")], _target);
        await task.RunAsync(default);

        var result = task.Results.Single();
        Assert.AreEqual(0, result.Extracted);
        Assert.IsNotNull(result.Error,
            "'0 images' would read as an empty document rather than one we failed to open");
    }

    [TestMethod]
    public async Task SeveralPdfs_EachGetTheirOwnFolder_AndOneFailureDoesNotStopTheRest()
    {
        var task = new PdfImageExtractionTask(
            [Sample("corrupt.pdf"), Sample("image-only.pdf")], _target);
        await task.RunAsync(default);

        Assert.AreEqual(2, task.Results.Count);
        Assert.IsTrue(Directory.Exists(Path.Combine(_target, "image-only")),
            "the readable document still produced its images");
        Assert.AreEqual(1, task.Results.Count(r => r.Error is not null));
    }

    [TestMethod]
    public async Task SecondRunIntoTheSameTarget_DoesNotOverwriteTheFirst()
    {
        await new PdfImageExtractionTask([Sample("image-only.pdf")], _target).RunAsync(default);
        await new PdfImageExtractionTask([Sample("image-only.pdf")], _target).RunAsync(default);

        Assert.IsTrue(Directory.Exists(Path.Combine(_target, "image-only")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_target, "image-only (1)")));
    }

    [TestMethod]
    public async Task Description_NamesWhatIsHappening()
    {
        // Shown in the activity ticker, which is the only place a user sees this task at all.
        Assert.AreEqual("Extracting images from image-only.pdf",
            new PdfImageExtractionTask([Sample("image-only.pdf")], _target).Description);
        Assert.AreEqual("Extracting images from 2 PDFs",
            new PdfImageExtractionTask([Sample("a.pdf"), Sample("b.pdf")], _target).Description);

        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task Cancellation_Propagates_SoTheShellEndsItQuietly()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var task = new PdfImageExtractionTask([Sample("image-only.pdf")], _target);

        // QueueBackgroundTask treats OperationCanceledException as "finished quietly, no failure reported";
        // swallowing it here would report a successful extraction of nothing.
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => task.RunAsync(cts.Token));
    }
}
