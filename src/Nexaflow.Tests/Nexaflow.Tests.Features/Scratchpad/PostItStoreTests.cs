using System.IO;
using Nexaflow.Features.Scratchpad.Models;
using Nexaflow.Features.Scratchpad.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Scratchpad;

[TestClass]
public class PostItStoreTests
{
    private string _root = string.Empty;
    private PostItStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _root  = Path.Combine(Path.GetTempPath(), $"scratchpad_{Guid.NewGuid():N}");
        _store = new PostItStore(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static PostItNote MakeNote(string content = "hello") =>
        new() { Content = content, X = 1, Y = 2, Color = "Pink", Shape = "Circle" };

    [TestMethod]
    [CoversNode("scratchpad-persistence")]
    public void Constructor_CreatesPostitsAndRecycleFolders()
    {
        Assert.IsTrue(Directory.Exists(Path.Combine(_root, "postits")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_root, "recyclebin")));
    }

    [TestMethod]
    [CoversNode("scratchpad-persistence")]
    public void Save_ThenLoadAll_RoundTripsNote()
    {
        var note = MakeNote("first");
        _store.Save(note);

        var loaded = _store.LoadAll();
        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual(note.Id,      loaded[0].Id);
        Assert.AreEqual("first",      loaded[0].Content);
        Assert.AreEqual(1,            loaded[0].X);
        Assert.AreEqual(2,            loaded[0].Y);
        Assert.AreEqual("Pink",       loaded[0].Color);
        Assert.AreEqual("Circle",     loaded[0].Shape);
    }

    [TestMethod]
    [CoversNode("scratchpad-persistence")]
    public void Save_TwiceWithSameId_OverwritesExistingFile()
    {
        var note = MakeNote("v1");
        _store.Save(note);
        note.Content = "v2";
        _store.Save(note);

        var loaded = _store.LoadAll();
        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual("v2", loaded[0].Content);
    }

    [TestMethod]
    public void Save_MultipleDistinctNotes_AllPersisted()
    {
        _store.Save(MakeNote("a"));
        _store.Save(MakeNote("b"));
        _store.Save(MakeNote("c"));

        var loaded = _store.LoadAll();
        Assert.AreEqual(3, loaded.Count);
        CollectionAssert.AreEquivalent(new[] { "a", "b", "c" }, loaded.Select(n => n.Content).ToArray());
    }

    [TestMethod]
    [CoversNode("recycle-delete")]
    public void Delete_RemovesNote()
    {
        var note = MakeNote();
        _store.Save(note);
        _store.Delete(note);

        Assert.AreEqual(0, _store.LoadAll().Count);
    }

    [TestMethod]
    [CoversNode("scratchpad-persistence")]
    public void Delete_NonexistentNote_DoesNotThrow()
    {
        _store.Delete(MakeNote());
        Assert.AreEqual(0, _store.LoadAll().Count);
    }

    [TestMethod]
    [CoversNode("postit-close")]
    public void MoveToRecycleBin_RemovesFromPostits_AndAppearsInBin()
    {
        var note = MakeNote("trash me");
        _store.Save(note);
        _store.MoveToRecycleBin(note);

        Assert.AreEqual(0, _store.LoadAll().Count);
        var bin = _store.LoadRecycleBin();
        Assert.AreEqual(1, bin.Count);
        Assert.AreEqual("trash me", bin[0].Content);
    }

    [TestMethod]
    [CoversNode("recycle-restore")]
    public void RestoreFromRecycleBin_ReturnsNoteToPostits()
    {
        var note = MakeNote("come back");
        _store.Save(note);
        _store.MoveToRecycleBin(note);
        _store.RestoreFromRecycleBin(note);

        Assert.AreEqual(0, _store.LoadRecycleBin().Count);
        var live = _store.LoadAll();
        Assert.AreEqual(1, live.Count);
        Assert.AreEqual("come back", live[0].Content);
    }

    [TestMethod]
    [CoversNode("recycle-empty")]
    public void EmptyRecycleBin_DeletesEverythingInBin()
    {
        for (int i = 0; i < 3; i++)
        {
            var n = MakeNote($"n{i}");
            _store.Save(n);
            _store.MoveToRecycleBin(n);
        }
        Assert.AreEqual(3, _store.LoadRecycleBin().Count);

        _store.EmptyRecycleBin();
        Assert.AreEqual(0, _store.LoadRecycleBin().Count);
    }

    [TestMethod]
    [CoversNode("scratchpad-expiry")]
    public void PurgeRecycleBin_NullRetention_KeepsEverything()
    {
        var old = MakeNote();
        old.CreatedAt = DateTimeOffset.Now.AddDays(-100);
        _store.Save(old);
        _store.MoveToRecycleBin(old);

        _store.PurgeRecycleBin(null);
        Assert.AreEqual(1, _store.LoadRecycleBin().Count);
    }

    [TestMethod]
    [CoversNode("scratchpad-expiry")]
    public void PurgeRecycleBin_Zero_DeletesEverything()
    {
        var fresh = MakeNote();
        _store.Save(fresh);
        _store.MoveToRecycleBin(fresh);

        _store.PurgeRecycleBin(0);
        Assert.AreEqual(0, _store.LoadRecycleBin().Count);
    }

    [TestMethod]
    [CoversNode("scratchpad-expiry")]
    public void PurgeRecycleBin_KeepsNewer_DeletesOlder()
    {
        // ExpiresAt is preferred over CreatedAt; older than cutoff => delete.
        var stale = MakeNote("stale");
        stale.ExpiresAt = DateTimeOffset.Now.AddDays(-10);
        _store.Save(stale);
        _store.MoveToRecycleBin(stale);

        var fresh = MakeNote("fresh");
        fresh.ExpiresAt = DateTimeOffset.Now.AddDays(-1);
        _store.Save(fresh);
        _store.MoveToRecycleBin(fresh);

        _store.PurgeRecycleBin(5);

        var bin = _store.LoadRecycleBin();
        Assert.AreEqual(1, bin.Count);
        Assert.AreEqual("fresh", bin[0].Content);
    }

    [TestMethod]
    [CoversNode("scratchpad-expiry")]
    public void PurgeRecycleBin_UsesCreatedAt_WhenExpiresAtNull()
    {
        var n = MakeNote("via createdat");
        n.ExpiresAt = null;
        n.CreatedAt = DateTimeOffset.Now.AddDays(-100);
        _store.Save(n);
        _store.MoveToRecycleBin(n);

        _store.PurgeRecycleBin(30);
        Assert.AreEqual(0, _store.LoadRecycleBin().Count);
    }

    [TestMethod]
    [CoversNode("scratchpad-persistence")]
    public void LoadAll_SkipsCorruptFiles()
    {
        var good = MakeNote("good");
        _store.Save(good);

        File.WriteAllText(Path.Combine(_root, "postits", "broken.json"), "{not json");

        var loaded = _store.LoadAll();
        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual("good", loaded[0].Content);
    }

    [TestMethod]
    [CoversNode("recycle-restore")]
    public void RestoreFromRecycleBin_WhenAbsent_NoOp()
    {
        _store.RestoreFromRecycleBin(MakeNote()); // never saved/moved
        Assert.AreEqual(0, _store.LoadAll().Count);
    }

    // ── Attachment folders (backing files for image / preview drops) ──────

    /// <summary>Creates the note's attachment folder with one file in it; returns the folder path.</summary>
    private string SeedAttachment(Guid id)
    {
        var dir = _store.EnsureAttachmentDir(id);
        File.WriteAllText(Path.Combine(dir, "img.png"), "x");
        return dir;
    }

    [TestMethod]
    [CoversNode("note-attachments")]
    public void EnsureAttachmentDir_CreatesFolderKeyedOnNoteId()
    {
        var id  = Guid.NewGuid();
        var dir = _store.EnsureAttachmentDir(id);

        Assert.IsTrue(Directory.Exists(dir));
        Assert.AreEqual(dir, _store.AttachmentDir(id));
    }

    [TestMethod]
    [CoversNode("note-attachments")]
    public void Delete_RemovesAttachmentFolder()
    {
        var note = MakeNote();
        _store.Save(note);
        var dir = SeedAttachment(note.Id);

        _store.Delete(note);

        Assert.IsFalse(Directory.Exists(dir), "permanent delete removes attachments");
    }

    [TestMethod]
    [CoversNode("note-attachments")]
    public void RecycleThenRestore_KeepsAttachments()
    {
        var note = MakeNote();
        _store.Save(note);
        var dir = SeedAttachment(note.Id);

        _store.MoveToRecycleBin(note);
        Assert.IsTrue(Directory.Exists(dir), "recycling keeps attachments so a restore still has them");

        _store.RestoreFromRecycleBin(note);
        Assert.IsTrue(Directory.Exists(dir), "restoring keeps attachments");
    }

    [TestMethod]
    [CoversNode("note-attachments")]
    public void PurgeRecycleBin_RemovesAttachmentsForPurgedNotes()
    {
        var note = MakeNote();
        _store.Save(note);
        var dir = SeedAttachment(note.Id);
        _store.MoveToRecycleBin(note);

        _store.PurgeRecycleBin(0);

        Assert.AreEqual(0, _store.LoadRecycleBin().Count);
        Assert.IsFalse(Directory.Exists(dir));
    }

    [TestMethod]
    [CoversNode("note-attachments")]
    public void EmptyRecycleBin_RemovesAttachmentFolders()
    {
        var note = MakeNote();
        _store.Save(note);
        var dir = SeedAttachment(note.Id);
        _store.MoveToRecycleBin(note);

        _store.EmptyRecycleBin();

        Assert.AreEqual(0, _store.LoadRecycleBin().Count);
        Assert.IsFalse(Directory.Exists(dir));
    }
}
