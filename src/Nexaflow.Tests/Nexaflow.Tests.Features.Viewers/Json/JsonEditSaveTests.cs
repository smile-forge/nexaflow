using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Json.FileActions;
using Nexaflow.Features.Json.Models;
using Nexaflow.Features.Json.Services;
using Nexaflow.Features.Json.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Json;

/// <summary>
/// Editing a JSON document and writing it back — and the one guard that matters.
/// <para>
/// A large file is held as a front batch plus placeholders for everything not yet read. Serialising that
/// tree would produce a document containing only the loaded part, and writing it would destroy the rest
/// with no error and nothing to undo. Save and Format both refuse while any placeholder remains; that
/// refusal is the test.
/// </para>
/// </summary>
[TestClass]
public class JsonEditSaveTests
{
    private string _path = string.Empty;

    [TestCleanup]
    public void RemoveTemp() { try { File.Delete(_path); } catch { } }

    private async Task<(JsonViewModel Vm, IShellServices Shell)> LoadedAsync(string content)
    {
        _path = Path.Combine(Path.GetTempPath(), $"jsonedit_{Guid.NewGuid():N}.json");
        File.WriteAllText(_path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var shell = Substitute.For<IShellServices>();
        var vm = new JsonViewModel(_path, new JsonFileLoader(), new JsonPathEvaluator(), shell);
        await vm.LoadAsync(CancellationToken.None);

        // LoadAsync deliberately returns before the placeholder fill finishes, so the first screen isn't
        // blocked. Everything below reads that fill — HasVirtualItems is what it produces — and off the
        // dispatcher its continuation runs on the thread pool, so reading now races the collection
        // mid-mutation. Wait for it the way the running app's dispatcher would have serialised it.
        await vm.Prepopulation;
        return (vm, shell);
    }

    /// <summary>An array past the loader's 1 MB large-file threshold, so it streams a front batch and
    /// leaves a placeholder standing in for the unread tail.</summary>
    private static string LargeArray(int items = 30_000) =>
        "[" + string.Join(",\n", Enumerable.Range(0, items)
                  .Select(i => $"{{\"id\":{i},\"name\":\"item_{i}\",\"value\":{i * 3}}}")) + "]";

    // ── The guard ─────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("json-edit-save")]
    public async Task SavingAPartlyLoadedFileIsRefused_AndSaysWhy()
    {
        var (vm, shell) = await LoadedAsync(LargeArray());
        Assert.IsTrue(vm.HasVirtualItems, "the fixture has to actually be windowed for this to mean anything");
        vm.FormatJsonCommand.Execute(null);   // whatever marks it dirty
        var before = File.ReadAllText(_path);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.AreEqual(before, File.ReadAllText(_path),
                        "writing here would replace the file with only the part that had been read");
        shell.ReceivedWithAnyArgs().ShowError(default!);
    }

    [TestMethod]
    [CoversNode("json-format")]
    public async Task FormattingAPartlyLoadedFileIsRefusedForTheSameReason()
    {
        var (vm, shell) = await LoadedAsync(LargeArray());

        vm.FormatJsonCommand.Execute(null);

        Assert.IsFalse(vm.IsModified, "a re-indent of half a document is not a re-indent");
        shell.ReceivedWithAnyArgs().ShowError(default!);
    }

    // ── Saving a whole document ───────────────────────────────────────────────

    [TestMethod]
    [CoversNode("json-edit-save")]
    public async Task SavingAFullyLoadedDocumentWritesItBack_AndClearsTheDirtyFlag()
    {
        var (vm, _) = await LoadedAsync("""{"b":1,"a":2}""");
        vm.FormatJsonCommand.Execute(null);
        Assert.IsTrue(vm.IsModified);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsModified, "the Save button goes away again");
        StringAssert.Contains(File.ReadAllText(_path), "\n", "the re-indent reached disk");
    }

    [TestMethod]
    [CoversNode("json-edit-save")]
    public async Task SavingAnUntouchedDocumentDoesNothing()
    {
        var (vm, shell) = await LoadedAsync("""{"a":1}""");
        var before = File.ReadAllText(_path);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.AreEqual(before, File.ReadAllText(_path), "no rewrite, so no needless mtime change");
        shell.DidNotReceiveWithAnyArgs().ShowError(default!);
    }

    // ── Drag-reorder ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("json-edit-save")]
    public async Task ReorderingSiblingsMovesTheNode_AndMarksTheDocumentDirty()
    {
        var (vm, _) = await LoadedAsync("""{"first":1,"second":2,"third":3}""");
        var root = (JsonObjectNodeModel)vm.Root!;
        var third = root.Children.Single(c => c.Key == "third");
        var first = root.Children.Single(c => c.Key == "first");

        vm.MoveNode(third, first, insertBefore: true);

        CollectionAssert.AreEqual(new[] { "third", "first", "second" },
                                  root.Children.Select(c => c.Key).ToArray());
        Assert.IsTrue(vm.IsModified);
    }

    [TestMethod]
    [CoversNode("json-edit-save")]
    public async Task ANodeCannotBeDraggedOutOfItsParent()
    {
        // Dropping a property of one object onto a property of another would silently move it between
        // objects. Reordering is a sibling operation; a cross-parent drop is refused.
        var (vm, _) = await LoadedAsync("""{"outer":{"x":1},"other":{"y":2}}""");
        var root = (JsonObjectNodeModel)vm.Root!;
        var x = ((JsonObjectNodeModel)root.Children.Single(c => c.Key == "outer")).Children[0];
        var y = ((JsonObjectNodeModel)root.Children.Single(c => c.Key == "other")).Children[0];

        vm.MoveNode(x, y, insertBefore: true);

        Assert.IsFalse(vm.IsModified, "nothing moved, so nothing is dirty");
        Assert.AreEqual(1, ((JsonObjectNodeModel)root.Children.Single(c => c.Key == "outer")).Children.Count);
    }
}

/// <summary>
/// "As Json" — the file action for the <c>/text/json</c> experience.
/// </summary>
[TestClass]
[CoversNode("json-open-actions")]
public class JsonOpenActionTests
{
    private static (IShellServices Shell, List<Dictionary<string, string>> Opened) Shell()
    {
        var shell = Substitute.For<IShellServices>();
        var opened = new List<Dictionary<string, string>>();
        shell.When(s => s.OpenTab(Arg.Any<string>(), Arg.Any<Dictionary<string, string>>()))
             .Do(ci => opened.Add(ci.ArgAt<Dictionary<string, string>>(1)));
        return (shell, opened);
    }


    [TestMethod]
    public void ItOwnsTheJsonExperience_AndTakesOneFileAtATime()
    {
        var action = new ShowJsonAction(Substitute.For<IShellServices>());

        Assert.AreEqual("/text/json", action.ExperienceId);
        Assert.IsTrue(action.OpensViewer);
        Assert.IsFalse(action.SupportsMultipleFiles, "the viewer holds one document");
    }

}
