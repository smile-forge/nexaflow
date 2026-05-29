using System.IO;
using System.Windows;
using Nexaflow.Features.Common;
using Nexaflow.Features.Scratchpad;
using Nexaflow.Features.Scratchpad.Models;
using Nexaflow.Features.Scratchpad.Services;
using Nexaflow.Features.Scratchpad.ViewModels;

namespace Nexaflow.Tests.Features.Scratchpad;

[TestClass]
public class ScratchpadViewModelTests
{
    private string _root = string.Empty;
    private PostItStore _store = null!;
    private ScratchpadConfig _config = null!;

    [TestInitialize]
    public void Setup()
    {
        _root   = Path.Combine(Path.GetTempPath(), $"scratchpadvm_{Guid.NewGuid():N}");
        _store  = new PostItStore(_root);
        _config = new ScratchpadConfig();
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private ScratchpadViewModel NewVm() => new(_config, _store);

    [TestMethod]
    public void Ctor_EmptyStore_HasNoNotes_AndStatusText()
    {
        using var vm = NewVm();
        Assert.AreEqual(0, vm.Notes.Count);
        Assert.AreEqual(0, vm.RecycleBinNotes.Count);
        Assert.AreEqual("0 notes", vm.StatusText);
        Assert.IsFalse(vm.ShowingRecycleBin);
    }

    [TestMethod]
    public void Ctor_LoadsExistingNotesFromStore()
    {
        _store.Save(new PostItNote { Content = "preloaded" });
        _store.Save(new PostItNote { Content = "second" });

        using var vm = NewVm();
        Assert.AreEqual(2, vm.Notes.Count);
        Assert.AreEqual("2 notes", vm.StatusText);
    }

    [TestMethod]
    public void AddNote_PersistsAndAppearsInNotes()
    {
        using var vm = NewVm();
        var added = vm.AddNote(new Point(300, 300));

        Assert.AreEqual(1, vm.Notes.Count);
        Assert.AreSame(added, vm.Notes[0]);
        Assert.AreEqual(1, _store.LoadAll().Count);
        Assert.AreEqual("1 note", vm.StatusText);
    }

    [TestMethod]
    public void AddNote_PositionsRelativeToClickPoint()
    {
        using var vm = NewVm();
        var added = vm.AddNote(new Point(500, 400));
        // The note's top-left should be offset by half its default size (100) from the click point.
        Assert.AreEqual(400, added.X);
        Assert.AreEqual(300, added.Y);
    }

    [TestMethod]
    public void AddNote_AssignsIncrementingZIndex()
    {
        using var vm = NewVm();
        var a = vm.AddNote(new Point(0, 0));
        var b = vm.AddNote(new Point(0, 0));
        Assert.IsTrue(b.ZIndex > a.ZIndex);
    }

    [TestMethod]
    public void AddNote_SetsExpiryFromConfig()
    {
        _config.NoteLifetime = "1 hour";
        using var vm = NewVm();
        var added = vm.AddNote(new Point(0, 0));

        Assert.IsNotNull(added.ExpiresAt);
        var remaining = added.ExpiresAt!.Value - DateTimeOffset.Now;
        Assert.IsTrue(remaining > TimeSpan.FromMinutes(55) && remaining < TimeSpan.FromMinutes(65));
    }

    [TestMethod]
    public void AddNoteWithContent_PersistsContent()
    {
        using var vm = NewVm();
        vm.AddNoteWithContent("typed text", new Point(0, 0));

        Assert.AreEqual(1, vm.Notes.Count);
        Assert.AreEqual("typed text", vm.Notes[0].Content);
        Assert.AreEqual("typed text", _store.LoadAll()[0].Content);
    }

    [TestMethod]
    public void LoadedNote_RemoveCommand_MovesToRecycleBin()
    {
        var existing = new PostItNote { Content = "kill me" };
        _store.Save(existing);

        using var vm = NewVm();
        var target = vm.Notes[0];
        target.RemoveCommand.Execute(null);

        Assert.AreEqual(0, vm.Notes.Count);
        Assert.AreEqual(0, _store.LoadAll().Count);
        var bin = _store.LoadRecycleBin();
        Assert.AreEqual(1, bin.Count);
        Assert.AreEqual("kill me", bin[0].Content);
    }

    [TestMethod]
    public void ToggleRecycleBinCommand_LoadsBinContents()
    {
        var trash = new PostItNote { Content = "old" };
        _store.Save(trash);
        _store.MoveToRecycleBin(trash);

        using var vm = NewVm();
        Assert.AreEqual(0, vm.RecycleBinNotes.Count);

        vm.ToggleRecycleBinCommand.Execute(null);
        Assert.IsTrue(vm.ShowingRecycleBin);
        Assert.AreEqual(1, vm.RecycleBinNotes.Count);
        Assert.AreEqual("old", vm.RecycleBinNotes[0].Content);

        vm.ToggleRecycleBinCommand.Execute(null);
        Assert.IsFalse(vm.ShowingRecycleBin);
    }

    [TestMethod]
    public void EmptyRecycleBin_Confirmed_ClearsBin()
    {
        var trash = new PostItNote();
        _store.Save(trash);
        _store.MoveToRecycleBin(trash);

        using var vm = NewVm();
        vm.ToggleRecycleBinCommand.Execute(null);
        vm.ConfirmAction = (_, _) => true;
        vm.EmptyRecycleBinCommand.Execute(null);

        Assert.AreEqual(0, vm.RecycleBinNotes.Count);
        Assert.AreEqual(0, _store.LoadRecycleBin().Count);
    }

    [TestMethod]
    public void EmptyRecycleBin_Cancelled_KeepsBin()
    {
        var trash = new PostItNote();
        _store.Save(trash);
        _store.MoveToRecycleBin(trash);

        using var vm = NewVm();
        vm.ToggleRecycleBinCommand.Execute(null);
        vm.ConfirmAction = (_, _) => false;
        vm.EmptyRecycleBinCommand.Execute(null);

        Assert.AreEqual(1, vm.RecycleBinNotes.Count);
        Assert.AreEqual(1, _store.LoadRecycleBin().Count);
    }

    [TestMethod]
    public void RestoreNote_MovesBackToActiveNotes_AndUnpins()
    {
        var trash = new PostItNote
        {
            Content   = "restore me",
            ExpiresAt = DateTimeOffset.Now.AddHours(1),
        };
        _store.Save(trash);
        _store.MoveToRecycleBin(trash);

        using var vm = NewVm();
        vm.ToggleRecycleBinCommand.Execute(null);
        var trashed = vm.RecycleBinNotes[0];

        vm.RestoreNoteCommand.Execute(trashed);

        Assert.AreEqual(0, vm.RecycleBinNotes.Count);
        Assert.AreEqual(1, vm.Notes.Count);
        Assert.AreSame(trashed, vm.Notes[0]);
        Assert.IsTrue(trashed.IsPinned, "restored notes should be pinned (no expiry)");
        Assert.AreEqual(1, _store.LoadAll().Count);
        Assert.AreEqual(0, _store.LoadRecycleBin().Count);
    }

    [TestMethod]
    public void DeleteFromBinCommand_PermanentlyDeletes()
    {
        var trash = new PostItNote { Content = "bye" };
        _store.Save(trash);
        _store.MoveToRecycleBin(trash);

        using var vm = NewVm();
        vm.ToggleRecycleBinCommand.Execute(null);
        vm.DeleteFromBinCommand.Execute(vm.RecycleBinNotes[0]);

        Assert.AreEqual(0, vm.RecycleBinNotes.Count);
        Assert.AreEqual(0, _store.LoadRecycleBin().Count);
    }

    [TestMethod]
    public void ZoomToFitWithViewport_NoNotes_ResetsTransform()
    {
        using var vm = NewVm();
        vm.Scale = 3; vm.OffsetX = 50; vm.OffsetY = 50;
        vm.ZoomToFitWithViewport(800, 600);

        Assert.AreEqual(1, vm.Scale);
        Assert.AreEqual(0, vm.OffsetX);
        Assert.AreEqual(0, vm.OffsetY);
    }

    [TestMethod]
    public void ZoomToFitWithViewport_WithNotes_FitsContent()
    {
        _store.Save(new PostItNote { X = 0,    Y = 0,    Width = 200, Height = 200 });
        _store.Save(new PostItNote { X = 1000, Y = 1000, Width = 200, Height = 200 });

        using var vm = NewVm();
        vm.ZoomToFitWithViewport(800, 600);

        Assert.IsTrue(vm.Scale > 0 && vm.Scale <= 4.0);
        // Content size ~1200; viewport 600 with padding => scale should shrink (<1).
        Assert.IsTrue(vm.Scale < 1);
    }

    [TestMethod]
    public void ZoomToFitWithViewport_ClampsScale()
    {
        // Tiny content => scale would blow up; should clamp at 4.0.
        _store.Save(new PostItNote { X = 0, Y = 0, Width = 10, Height = 10 });
        using var vm = NewVm();
        vm.ZoomToFitWithViewport(800, 600);
        Assert.IsTrue(vm.Scale <= 4.0);
    }

    [TestMethod]
    public void StatusText_PluralisesCorrectly()
    {
        using var vm = NewVm();
        Assert.AreEqual("0 notes", vm.StatusText);
        vm.AddNote(new Point(0, 0));
        Assert.AreEqual("1 note", vm.StatusText);
        vm.AddNote(new Point(0, 0));
        Assert.AreEqual("2 notes", vm.StatusText);
    }

    [TestMethod]
    public void GetContext_EmptyOrPopulated()
    {
        using var vm = NewVm();
        Assert.AreEqual("Scratchpad: empty.", vm.GetContext());
        vm.AddNote(new Point(0, 0));
        StringAssert.Contains(vm.GetContext(), "1 note");
    }

    [TestMethod]
    public void GetClientTools_ReturnsEmpty()
    {
        using var vm = NewVm();
        Assert.AreEqual(0, ((IPageViewModel)vm).GetClientTools().Count);
    }

    [TestMethod]
    public void Dispose_DoesNotThrow()
    {
        var vm = NewVm();
        vm.AddNote(new Point(0, 0));
        vm.Dispose();
    }
}
