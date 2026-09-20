using System;
using System.IO;
using System.Linq;
using Nexaflow.Services.Initiatives.Cli;
using Nexaflow.Services.Initiatives.Cli.Daemon;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Cli;

/// <summary>
/// <c>graph edit undo</c>: an edit written and then taken back, instead of a dry run followed by the same edit again. What
/// matters is that it puts back exactly what was there, walks back one edit at a time, and never overwrites a change
/// made after the edit it undoes.
/// </summary>
[TestClass]
[CoversNode("graph-edit-undo")]
public class EditUndoTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Directory.CreateTempSubdirectory("nexa-undo-").FullName;
        new ProductStore(_root).Initialize("P");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
        try { Directory.Delete(EditJournal.DirectoryFor(_root), recursive: true); } catch { }
    }

    private int Run(params string[] args)
    {
        using var scope = RequestScope.Begin(new StringWriter(), new StringWriter(), new RequestContext(_root));
        return Program.Execute([.. args, _root]);
    }

    private string At(string name) => Path.Combine(_root, name);

    [TestMethod]
    public void EachUndo_TakesBackTheLastEdit_ThenTheOneBeforeIt()
    {
        Assert.AreEqual(0, Run("graph", "edit", "create", "a.md", "--text", "one"));
        Assert.AreEqual(0, Run("graph", "edit", "create", "b.md", "--text", "two"));

        Assert.AreEqual(0, Run("graph", "edit", "undo"));
        Assert.IsFalse(File.Exists(At("b.md")), "the file the last edit created is gone");
        Assert.IsTrue(File.Exists(At("a.md")), "and the edit before it is untouched");

        Assert.AreEqual(0, Run("graph", "edit", "undo"));
        Assert.IsFalse(File.Exists(At("a.md")));

        Assert.AreNotEqual(0, Run("graph", "edit", "undo"), "with nothing left to undo, it says so");
    }

    [TestMethod]
    public void AFileDeleted_IsGone_AndComesBackByteForByte()
    {
        Assert.AreEqual(0, Run("graph", "edit", "create", "notes.md", "--text-escaped", "one\\ntwo"));
        var written = File.ReadAllText(At("notes.md"));

        Assert.AreEqual(0, Run("graph", "edit", "delete", "file:notes.md"), "no graph has been built here, and none is needed");
        Assert.IsFalse(File.Exists(At("notes.md")));

        Assert.AreEqual(0, Run("graph", "edit", "undo"));
        Assert.AreEqual(written, File.ReadAllText(At("notes.md")), "line endings and all");
    }

    [TestMethod]
    public void DeletingAFileThatIsNotThere_IsRefused()
    {
        Assert.AreNotEqual(0, Run("graph", "edit", "delete", "file:absent.md"));
        Assert.AreNotEqual(0, Run("graph", "edit", "undo"), "a refusal is not an edit to take back");
    }

    /// <summary>Hangs a code snaplink naming <paramref name="doc"/> on a node, the way a feature claims a file.</summary>
    private void SnaplinkNaming(string doc)
    {
        var store = new ProductStore(_root);
        var state = store.Load();
        state.Nodes["claimant"] = new ProductNode { Title = "Claimant", Snaplinks = [new Snaplink { Type = "code", Doc = doc }] };
        store.SaveTree(state.Nodes);
    }

    [TestMethod]
    public void WithMustCompile_ADeleteThatStrandsASnaplink_IsRefused()
    {
        Assert.AreEqual(0, Run("graph", "edit", "create", "claimed.md", "--text", "one"));
        SnaplinkNaming("claimed.md");

        Assert.AreNotEqual(0, Run("graph", "edit", "delete", "file:claimed.md", "--must-compile"),
                           "nothing compiles a snaplink, so only this check stands between a delete and a broken tree");
        Assert.IsTrue(File.Exists(At("claimed.md")), "refused means nothing was written");

        Assert.AreEqual(0, Run("graph", "edit", "delete", "file:claimed.md"),
                        "without the flag it is reported and written — the caller decides");
        Assert.IsFalse(File.Exists(At("claimed.md")));
    }

    [TestMethod]
    public void WithMustCompile_ADeleteNoSnaplinkNames_GoesAhead()
    {
        Assert.AreEqual(0, Run("graph", "edit", "create", "spare.md", "--text", "one"));
        SnaplinkNaming("elsewhere.md");

        Assert.AreEqual(0, Run("graph", "edit", "delete", "file:spare.md", "--must-compile"));
        Assert.IsFalse(File.Exists(At("spare.md")));
    }

    [TestMethod]
    public void AnUndo_IsRefused_WhenTheFileHasChangedSinceTheEdit()
    {
        Assert.AreEqual(0, Run("graph", "edit", "create", "a.md", "--text", "one"));
        File.WriteAllText(At("a.md"), "changed afterwards");

        Assert.AreNotEqual(0, Run("graph", "edit", "undo"));
        Assert.AreEqual("changed afterwards", File.ReadAllText(At("a.md")), "later work is never overwritten");
    }

    [TestMethod]
    public void TheJournal_KeepsOnlyTheNewestEdits()
    {
        var directory = Path.Combine(_root, "journal");
        for (var i = 0; i < EditJournal.Kept + 5; i++)
            EditJournal.Record(directory, $"edit {i}", [new EditPlan.Written($"f{i}.md", null, "x")]);

        Assert.AreEqual(EditJournal.Kept, Directory.GetFiles(directory).Length);
        Assert.AreEqual($"edit {EditJournal.Kept + 4}", EditJournal.Last(directory)!.Value.Entry.Label);
    }
}
