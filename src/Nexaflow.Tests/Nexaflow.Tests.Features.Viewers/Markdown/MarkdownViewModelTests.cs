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
/// The view-model takes an <c>IShellServices</c> (used only to marshal AI-tool edits to the UI
/// thread); none of these pure-state tests invoke a tool, so a bare substitute suffices.
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

    private MarkdownViewModel Make(string content = "# Title\n\nBody.\n")
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return new MarkdownViewModel(path, Substitute.For<IShellServices>());
    }

    // ── File name label ───────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("markdown-filename")]
    public void FileName_IsTheOpenDocumentsName_NotItsPath()
    {
        var vm = Make();

        Assert.AreEqual(Path.GetFileName(vm.FilePath), vm.FileName);
        Assert.IsFalse(vm.FileName.Contains(Path.DirectorySeparatorChar),
                       "The toolbar label shows the file name only.");
    }

    // ── Source / preview toggle (pure state) ──────────────────────────────────

    [TestMethod]
    [CoversNode("markdown-source-toggle")]
    public void SourceOnly_RoundTrips_OffOnOff()
    {
        var vm = Make();

        Assert.IsFalse(vm.SourceOnly);            // default: rendered + inline editing
        vm.SourceOnly = true;
        Assert.IsTrue(vm.SourceOnly);
        vm.SourceOnly = false;
        Assert.IsFalse(vm.SourceOnly);
    }

    [TestMethod]
    [CoversNode("markdown-source-toggle")]
    [CoversNode("markdown-source-box")]
    public void TogglingSourceOnly_PreservesMarkdownText()
    {
        var vm = Make("# Heading\n\nA paragraph.\n");
        var before = vm.Markdown;

        vm.SourceOnly = true;
        Assert.AreEqual(before, vm.Markdown);     // same backing text across both surfaces
        vm.SourceOnly = false;
        Assert.AreEqual(before, vm.Markdown);
    }

    // ── Save command (pure state) ─────────────────────────────────────────────

    [TestMethod]
    [CoversNode("markdown-save")]
    public void SaveCommand_CannotExecute_WhenClean()
    {
        var vm = Make();

        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
    }

    [TestMethod]
    [CoversNode("markdown-save")]
    public void SaveCommand_ExecuteWhenClean_IsNoOp_AndStaysClean()
    {
        var vm = Make("# Clean\n");

        vm.SaveCommand.Execute(null);             // disabled-but-invoked: must not throw or dirty

        Assert.IsFalse(vm.IsDirty);
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
    }

    [TestMethod]
    [CoversNode("markdown-save")]
    public void EditThenSave_FlipsCanExecute_ThenBackToFalse()
    {
        var vm = Make("# Original\n");
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));

        vm.Markdown = "# Edited\n";
        Assert.IsTrue(vm.SaveCommand.CanExecute(null));

        vm.SaveCommand.Execute(null);
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
    }

    // ── AI-context surface ────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("markdown-ai-context")]
    public void GetContext_Clean_OmitsUnsavedMarker_AndNamesFile()
    {
        var vm = Make();

        var context = vm.GetContext();

        StringAssert.Contains(context, vm.FileName);
        Assert.IsFalse(context.Contains("unsaved"), "Clean document should not advertise unsaved changes.");
    }

    [TestMethod]
    [CoversNode("markdown-ai-context")]
    public void GetContext_Dirty_AdvertisesUnsavedChanges()
    {
        var vm = Make();

        vm.Markdown = "# Dirty now\n";
        var context = vm.GetContext();

        StringAssert.Contains(context, "unsaved");
    }

    [TestMethod]
    [CoversNode("markdown-ai-context")]
    public void GetContextObject_SelectsTheFile_UnderItsFolder()
    {
        var vm = Make();

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
    public void Zoom_ScalesTheDocumentBodySize()
    {
        var vm = Make();
        Assert.AreEqual(100, vm.Zoom.Percent, "a freshly opened document is unzoomed");

        var unzoomed = vm.Zoom.FontSize;
        vm.Zoom.Percent = 150;
        Assert.AreEqual(unzoomed * 1.5, vm.Zoom.FontSize, 1e-9,
            "the rendered surface and the source box both bind this");
    }
}
