using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Common;
using Nexaflow.Features.Pdf;
using Nexaflow.Features.Pdf.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// The reader's toolbar over a document being edited: what it offers once the document is open, and what it acts on —
/// the document as it now stands, edits not yet saved included.
/// </summary>
[TestClass]
public class PdfViewModelEditingTests
{
    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-save")]
    [CoversNode("pdf-extract-images")]
    public async Task SavingAsAndExtracting_AreOffered_AsSoonAsTheDocumentIsOpen_AndAfterAnEdit()
    {
        using var vm = new PdfViewModel(Copy("save-as.pdf"), Shell(), VirtualFileSystem.Instance, new PdfConfig());
        var told = new HashSet<string>();
        vm.SaveAsCommand.CanExecuteChanged += (_, _) => told.Add("save as");
        vm.ExtractImagesCommand.CanExecuteChanged += (_, _) => told.Add("extract");

        await vm.LoadAsync(Brushes.White);

        // A button asks again only when told to: asking the command alone would answer yes without it.
        CollectionAssert.AreEquivalent(new[] { "save as", "extract" }, told.ToArray(), "the buttons are told the document is open");
        Assert.IsTrue(vm.SaveAsCommand.CanExecute(null), "a document open is a document that can be saved as another");
        Assert.IsFalse(vm.SaveCommand.CanExecute(null), "nothing to save over the file yet");

        vm.PagesPanel.Pages[0].IsSelected = true;
        vm.PagesPanel.DeleteSelectedCommand.Execute(null);

        Assert.IsTrue(vm.SaveCommand.CanExecute(null));
        Assert.IsTrue(vm.SaveAsCommand.CanExecute(null), "an edited document can be saved as another file");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-extract-images")]
    public async Task ExtractingImages_ReadsTheDocumentAsItNowStands_AndLeavesNoCopyBehind()
    {
        var target = TestTemp.CreateSubdirectory("extracted_").FullName;
        var picture = TestTemp.CreateFile(".png");
        File.WriteAllBytes(picture, Png());
        var shell = Shell();
        shell.PickOpenFilesAsync(Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>()).Returns(Task.FromResult<IReadOnlyList<string>>([picture]));
        shell.PickFolderAsync(Arg.Any<string?>()).Returns(Task.FromResult<string?>(target));
        IBackgroundTask? queued = null;
        shell.When(s => s.QueueBackgroundTask(Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>?>(), Arg.Any<CancellationToken>()))
             .Do(call => queued = call.Arg<IBackgroundTask>());
        using var vm = await Opened(shell, Copy("edited-extract.pdf"));

        Assert.IsTrue(vm.ExtractImagesCommand.CanExecute(null));
        await vm.PagesPanel.InsertImagesAtCommand.ExecuteAsync(0);   // a picture the file itself does not carry
        await vm.ExtractImagesCommand.ExecuteAsync(null);

        Assert.IsNotNull(queued, "the extraction is the shell's job, so it outlives the tab");
        await queued.RunAsync(CancellationToken.None);

        var written = Directory.GetFiles(Path.Combine(target, "edited-extract"));
        Assert.AreEqual(1, written.Length, "the picture inserted but not yet saved is extracted, into a folder named after the document");
        Assert.IsFalse(Directory.EnumerateFiles(DraftLocation.Root, "edited-extract.pdf", SearchOption.AllDirectories).Any(),
            "the copy written for the extraction is gone once read");
    }

    private static async Task<PdfViewModel> Opened(IShellServices shell, string path)
    {
        var vm = new PdfViewModel(path, shell, VirtualFileSystem.Instance, new PdfConfig());
        await vm.LoadAsync(Brushes.White);
        return vm;
    }

    private static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call => { call.Arg<Action>()(); return Task.CompletedTask; });
        return shell;
    }

    /// <summary>A copy of the outlined sample, named <paramref name="name"/>, so what is made from it can be told apart.</summary>
    private static string Copy(string name)
    {
        var path = Path.Combine(TestTemp.CreateSubdirectory().FullName, name);
        File.Copy(TestSampleData.Path("pdf", "outline.pdf"), path);
        return path;
    }

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
