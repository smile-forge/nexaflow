using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.Tests.Fixtures;
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
/// The view-model constructs headless from a file path alone (no <c>IShellServices</c>) — the
/// shell dependency lives in the registration/action, not the VM — so nothing here touches WPF.
/// </summary>
[TestClass]
[CoversNode("markdown-toolbar")]
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
        return new MarkdownViewModel(path);
    }

    // ── Source / preview toggle (pure state) ──────────────────────────────────

    [TestMethod]
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
    public void SaveCommand_CannotExecute_WhenClean()
    {
        var vm = Make();

        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
    }

    [TestMethod]
    public void SaveCommand_ExecuteWhenClean_IsNoOp_AndStaysClean()
    {
        var vm = Make("# Clean\n");

        vm.SaveCommand.Execute(null);             // disabled-but-invoked: must not throw or dirty

        Assert.IsFalse(vm.IsDirty);
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
    }

    [TestMethod]
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
    public void GetContext_Clean_OmitsUnsavedMarker_AndNamesFile()
    {
        var vm = Make();

        var context = vm.GetContext();

        StringAssert.Contains(context, vm.FileName);
        Assert.IsFalse(context.Contains("unsaved"), "Clean document should not advertise unsaved changes.");
    }

    [TestMethod]
    public void GetContext_Dirty_AdvertisesUnsavedChanges()
    {
        var vm = Make();

        vm.Markdown = "# Dirty now\n";
        var context = vm.GetContext();

        StringAssert.Contains(context, "unsaved");
    }

    [TestMethod]
    public void GetContextObject_SelectsTheFile_UnderItsFolder()
    {
        var vm = Make();

        var ctx = vm.GetContextObject();

        Assert.IsNotNull(ctx);
        var fileCtx = (Nexaflow.Features.Common.FileSystemContext)ctx!;
        Assert.AreEqual(Path.GetDirectoryName(vm.FilePath), fileCtx.RootPath);
        CollectionAssert.Contains(fileCtx.SelectedItems.ToList(), vm.FilePath);
    }
}
