using Nexaflow.IO.Common;
using Nexaflow.Features.Common;
using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using System.IO;
using System.Linq;

namespace Nexaflow.Tests.Features.Markdown;

/// <summary>
/// Headless command / view-state tests for <see cref="MarkdownViewModel"/> — the pure-state
/// behaviour behind the toolbar's source/preview toggle and Save button, plus the AI-context
/// surface. Load, dirty-tracking and save round-tripping are covered in
/// <see cref="MarkdownViewModelEditingTests"/>; this file adds the toggle round-trip, the
/// Save-when-clean no-op, and the context string/object reflecting dirty + file state.
///
/// The view-model takes an <c>IShellServices</c>, to marshal AI-tool edits to the UI thread and to
/// route a link out of the document; a bare substitute suffices for everything else here.
///
/// Coverage is declared per method: each toolbar control is its own product-tree leaf, so the test that
/// drives that control's command/state names it rather than the panel that hosts them.
/// </summary>
[TestClass]
public class MarkdownViewModelTests
{
    private readonly List<string> _tempFiles = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var path in _tempFiles)
            try { File.Delete(path); } catch { }
    }

    private async Task<MarkdownViewModel> MakeAsync(string content = "# Title\n\nBody.\n") => (await MakeWithShellAsync(content)).Vm;

    /// <summary>The same document, with the shell it talks to in hand — for what the view-model asks of the host.</summary>
    private async Task<(MarkdownViewModel Vm, IShellServices Shell)> MakeWithShellAsync(string content = "# Title\n\nBody.\n")
    {
        var path = TestTemp.CreateFile();
        File.WriteAllText(path, content);
        _tempFiles.Add(path);

        var shell = Substitute.For<IShellServices>();
        var vm = new MarkdownViewModel(path, shell, VirtualFileSystem.Instance);
        await vm.Loaded;
        return (vm, shell);
    }

    // ── File name label ───────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("markdown-filename")]
    public async Task FileName_IsTheOpenDocumentsName_NotItsPath()
    {
        var vm = await MakeAsync();

        Assert.AreEqual(Path.GetFileName(vm.FilePath), vm.FileName);
        Assert.IsFalse(vm.FileName.Contains(Path.DirectorySeparatorChar),
                       "The toolbar label shows the file name only.");
    }

    [TestMethod]
    [CoversNode("markdown-links")]
    public async Task FollowLink_IsTheShellsToRoute_AndSaysWhetherAnythingTookIt()
    {
        var (vm, shell) = await MakeWithShellAsync();

        shell.HandleObject("https://example.com/x").Returns(true);
        Assert.IsTrue(vm.FollowLink("https://example.com/x"), "a feature claimed it, so the document's link handling ends there");

        shell.HandleObject("https://example.com/y").Returns(false);
        Assert.IsFalse(vm.FollowLink("https://example.com/y"), "nothing claimed it, so it is left to open the way links do");

        Assert.IsFalse(vm.FollowLink("   "), "and a link to nowhere is never offered to anybody");
        shell.DidNotReceive().HandleObject("   ");
    }

    // ── A rendered block's picture ────────────────────────────────────────────

    [TestMethod]
    [CoversNode("markdown-block-picture")]
    public async Task SavePicture_WritesThePngWhereTheReaderPicks_NamedForTheDocumentAndTheBlock()
    {
        var path = TestTemp.CreateFile();
        _tempFiles.Add(path);
        File.WriteAllText(path, "# Pets\n");

        var shell = Substitute.For<IShellServices>();
        var chosen = Path.Combine(TestTemp.Root, Guid.NewGuid().ToString("N"));
        _tempFiles.Add(chosen + ".png");
        shell.PickSaveFileAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>(), Arg.Any<long>())
             .Returns(chosen);

        var vm = new MarkdownViewModel(path, shell, VirtualFileSystem.Instance);
        await vm.Loaded;
        byte[] png = [0x89, 0x50, 0x4E, 0x47];

        Assert.IsTrue(await vm.SavePictureAsync(png, "mermaid"));

        // The size goes with the ask, so the dialog can say what the write costs and whether the volume has room.
        await shell.Received(1).PickSaveFileAsync($"{Path.GetFileNameWithoutExtension(path)}-mermaid.png",
                                                  Arg.Is<IReadOnlyList<string>?>(extensions => extensions!.SequenceEqual(new[] { ".png" })),
                                                  Path.GetDirectoryName(path),
                                                  png.Length);
        CollectionAssert.AreEqual(png, File.ReadAllBytes(chosen + ".png"), "written as a .png, whatever the name picked ended with");
    }

    [TestMethod]
    [CoversNode("markdown-block-picture")]
    public async Task SavePicture_WritesNothingWhenThePickIsCancelled()
    {
        var shell = Substitute.For<IShellServices>();
        shell.PickSaveFileAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>(), Arg.Any<long>())
             .Returns((string?)null);

        var path = TestTemp.CreateFile();
        _tempFiles.Add(path);

        Assert.IsFalse(await new MarkdownViewModel(path, shell, VirtualFileSystem.Instance).SavePictureAsync([1, 2, 3], null));
    }

    // ── Source / preview toggle (pure state) ──────────────────────────────────

    [TestMethod]
    [CoversNode("markdown-view-mode")]
    public async Task ViewMode_WalksTheSlidersThreeStops()
    {
        var vm = await MakeAsync();

        Assert.AreEqual(MarkdownViewMode.Rendered, vm.ViewMode);   // default: rendered + inline editing
        Assert.IsTrue(vm.ShowRendered);
        Assert.IsFalse(vm.ShowSource);

        vm.ViewMode = MarkdownViewMode.Split;
        Assert.IsTrue(vm.IsSplit);
        Assert.IsTrue(vm.ShowRendered, "the split keeps the rendered half");
        Assert.IsTrue(vm.ShowSource,   "and puts the source beside it");

        vm.ViewMode = MarkdownViewMode.Source;
        Assert.IsTrue(vm.ShowSource);
        Assert.IsFalse(vm.ShowRendered);
        Assert.IsFalse(vm.IsSplit);
    }

    /// <summary>The slider binds a number, not the enum, so the number has to be the honest inverse of it.</summary>
    [TestMethod]
    [CoversNode("markdown-view-mode")]
    public async Task ViewModeIndex_IsTheSlidersPosition_AndReadsAsTheNearestStop()
    {
        var vm = await MakeAsync();

        Assert.AreEqual(0d, vm.ViewModeIndex);

        vm.ViewModeIndex = 1;
        Assert.AreEqual(MarkdownViewMode.Split, vm.ViewMode);

        // Mid-drag the slider hands over a value between two stops; it reads as the one it is nearest,
        // and the number reads back as that stop rather than where the thumb happened to be.
        vm.ViewModeIndex = 1.6;
        Assert.AreEqual(MarkdownViewMode.Source, vm.ViewMode);
        Assert.AreEqual(2d, vm.ViewModeIndex);

        // Off either end it is clamped, rather than cast to a mode that does not exist.
        vm.ViewModeIndex = -3;
        Assert.AreEqual(MarkdownViewMode.Rendered, vm.ViewMode);
        vm.ViewModeIndex = 9;
        Assert.AreEqual(MarkdownViewMode.Source, vm.ViewMode);
    }

    [TestMethod]
    [CoversNode("markdown-view-mode")]
    [CoversNode("markdown-source-box")]
    public async Task ChangingTheViewMode_PreservesMarkdownText()
    {
        var vm = await MakeAsync("# Heading\n\nA paragraph.\n");
        var before = vm.Markdown;

        vm.ViewMode = MarkdownViewMode.Source;
        Assert.AreEqual(before, vm.Markdown);     // same backing text across both surfaces
        vm.ViewMode = MarkdownViewMode.Split;
        Assert.AreEqual(before, vm.Markdown);
        vm.ViewMode = MarkdownViewMode.Rendered;
        Assert.AreEqual(before, vm.Markdown);
    }

    // ── Save command (pure state) ─────────────────────────────────────────────

    [TestMethod]
    [CoversNode("markdown-save")]
    public async Task SaveCommand_CannotExecute_WhenClean()
    {
        var vm = await MakeAsync();

        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
    }

    [TestMethod]
    [CoversNode("markdown-save")]
    public async Task SaveCommand_ExecuteWhenClean_IsNoOp_AndStaysClean()
    {
        var vm = await MakeAsync("# Clean\n");

        await vm.SaveCommand.ExecuteAsync(null);             // disabled-but-invoked: must not throw or dirty

        Assert.IsFalse(vm.IsDirty);
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
    }

    [TestMethod]
    [CoversNode("markdown-save")]
    public async Task EditThenSave_FlipsCanExecute_ThenBackToFalse()
    {
        var vm = await MakeAsync("# Original\n");
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));

        vm.Markdown = "# Edited\n";
        Assert.IsTrue(vm.SaveCommand.CanExecute(null));

        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
    }

    // ── AI-context surface ────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("markdown-ai-context")]
    public async Task GetContext_Clean_OmitsUnsavedMarker_AndNamesFile()
    {
        var vm = await MakeAsync();

        var context = vm.GetContext();

        StringAssert.Contains(context, vm.FileName);
        Assert.IsFalse(context.Contains("unsaved"), "Clean document should not advertise unsaved changes.");
    }

    [TestMethod]
    [CoversNode("markdown-ai-context")]
    public async Task GetContext_Dirty_AdvertisesUnsavedChanges()
    {
        var vm = await MakeAsync();

        vm.Markdown = "# Dirty now\n";
        var context = vm.GetContext();

        StringAssert.Contains(context, "unsaved");
    }

    [TestMethod]
    [CoversNode("markdown-ai-context")]
    public async Task GetContextObject_SelectsTheFile_UnderItsFolder()
    {
        var vm = await MakeAsync();

        var ctx = vm.GetContextObject();

        Assert.IsNotNull(ctx);
        var fileCtx = (Nexaflow.Features.Common.FileSystemContext)ctx!;
        Assert.AreEqual(Path.GetDirectoryName(vm.FilePath), fileCtx.RootPath);
        CollectionAssert.Contains(fileCtx.SelectedItems.ToList(), vm.FilePath);
    }

    // ── Zoom ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The viewer's own zoom is the shared <c>TextZoom</c>, so its stepping and clamping are pinned once in
    /// <c>TextZoomTests</c>. What is markdown's alone — and what a shared model cannot prove — is that the
    /// tab actually carries one and hands out a usable body size.
    /// </summary>
    [TestMethod]
    [CoversNode("markdown-zoom")]
    public async Task Zoom_ScalesTheDocumentBodySize()
    {
        var vm = await MakeAsync();
        Assert.AreEqual(100, vm.Zoom.Percent, "a freshly opened document is unzoomed");

        var unzoomed = vm.Zoom.FontSize;
        vm.Zoom.Percent = 150;
        Assert.AreEqual(unzoomed * 1.5, vm.Zoom.FontSize, 1e-9,
            "the rendered surface and the source box both bind this");
    }

    /// <summary>
    /// The footer's counts. A word is a run of non-whitespace, so punctuation and markdown syntax
    /// (<c>**bold**</c>, a list bullet) count as part of the word they are attached to — which is what a
    /// count in a markdown editor should say, and is the behaviour a naive Split would quietly change.
    /// </summary>
    [TestMethod]
    [CoversNode("markdown-doc-stats")]
    public async Task DocumentStats_CountWordsAndLines()
    {
        var vm = await MakeAsync("# Title\n\nBody text here.\n");

        Assert.AreEqual(3, vm.LineCount, "the trailing newline ends the third line rather than starting a fourth");
        Assert.AreEqual(5, vm.WordCount, "'#', 'Title', 'Body', 'text', 'here.'");

        vm.Markdown = "one  two\tthree\nfour";
        Assert.AreEqual(4, vm.WordCount, "runs of mixed whitespace separate one word, not several");
        Assert.AreEqual(2, vm.LineCount);
    }

    [TestMethod]
    [CoversNode("markdown-doc-stats")]
    public async Task DocumentStats_AnEmptyDocumentHasNoLines()
    {
        var vm = await MakeAsync(string.Empty);
        Assert.AreEqual(0, vm.WordCount);
        Assert.AreEqual(0, vm.LineCount, "an empty file is no lines, not one blank one");
    }
}
