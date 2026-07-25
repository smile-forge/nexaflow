using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Scratchpad;
using Nexaflow.Features.Scratchpad.Models;
using Nexaflow.Features.Scratchpad.Services;
using Nexaflow.Features.Scratchpad.ViewModels;
using NSubstitute;

using Nexaflow.Tests.Fixtures;

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

    private ScratchpadViewModel NewVm(IShellServices? shell = null) => new(_config, _store, shell);

    /// <summary>A shell whose confirmation overlay auto-answers (confirm or cancel).</summary>
    private static IShellServices ShellWithConfirm(bool confirm)
    {
        var shell = Substitute.For<IShellServices>();
        shell.When(s => s.ShowConfirmation(Arg.Any<string>(), Arg.Any<string>(),
                                           Arg.Any<Action>(), Arg.Any<Action>()))
             .Do(ci => ci.ArgAt<Action>(confirm ? 2 : 3).Invoke());
        return shell;
    }

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
    [CoversNode("scratchpad-persistence")]
    public void Ctor_LoadsExistingNotesFromStore()
    {
        _store.Save(new PostItNote { Content = "preloaded" });
        _store.Save(new PostItNote { Content = "second" });

        using var vm = NewVm();
        Assert.AreEqual(2, vm.Notes.Count);
        Assert.AreEqual("2 notes", vm.StatusText);
    }

    [TestMethod]
    [CoversNode("toolbar-new-note")]
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
    [CoversNode("toolbar-new-note")]
    public void AddNote_PositionsRelativeToClickPoint()
    {
        using var vm = NewVm();
        var added = vm.AddNote(new Point(500, 400));
        // The note's top-left should be offset by half its default size (100) from the click point.
        Assert.AreEqual(400, added.X);
        Assert.AreEqual(300, added.Y);
    }

    [TestMethod]
    [CoversNode("toolbar-new-note")]
    public void AddNote_AssignsIncrementingZIndex()
    {
        using var vm = NewVm();
        var a = vm.AddNote(new Point(0, 0));
        var b = vm.AddNote(new Point(0, 0));
        Assert.IsTrue(b.ZIndex > a.ZIndex);
    }

    [TestMethod]
    [CoversNode("toolbar-new-note")]
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
    [CoversNode("note-text")]
    public void AddNoteWithContent_PersistsContent()
    {
        using var vm = NewVm();
        vm.AddNoteWithContent("typed text", new Point(0, 0));

        Assert.AreEqual(1, vm.Notes.Count);
        Assert.AreEqual("typed text", vm.Notes[0].Content);
        Assert.AreEqual("typed text", _store.LoadAll()[0].Content);
    }

    [TestMethod]
    [CoversNode("note-file-link")]
    public void AddFileLinkNote_CreatesClickableFileLink()
    {
        var file = Path.Combine(_root, "report.pdf");
        File.WriteAllText(file, "x");

        using var vm = NewVm();
        var note = vm.AddFileLinkNote(file, new Point(0, 0));

        StringAssert.Contains(note.Content, "report.pdf");
        StringAssert.Contains(note.Content, new Uri(file).AbsoluteUri);
    }

    [TestMethod]
    [CoversNode("note-url-preview")]
    public void AddUrlNote_ShowsBareUrlImmediately()
    {
        using var vm = NewVm();   // null shell → no preview task, just the bare URL
        var note = vm.AddUrlNote("https://example.com/some/page", new Point(0, 0));

        Assert.AreEqual("https://example.com/some/page", note.Content);
    }

    [TestMethod]
    [CoversNode("note-image")]
    public void AddImageNote_EmbedsImageAndCopiesIntoAttachmentFolder()
    {
        var img = Path.Combine(_root, "pic.png");
        File.WriteAllText(img, "pretend-image");   // copy succeeds; sizing fails gracefully

        using var vm = NewVm();
        var note = vm.AddImageNote(img, new Point(0, 0));

        StringAssert.StartsWith(note.Content, "![](");
        var attDir = _store.AttachmentDir(note.Note.Id);
        Assert.IsTrue(Directory.Exists(attDir));
        Assert.AreEqual(1, Directory.GetFiles(attDir).Length, "the image was copied into the note's folder");
    }

    [TestMethod]
    [CoversNode("postit-close")]
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
    [CoversNode("toolbar-recycle-toggle")]
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
    [CoversNode("recycle-empty")]
    public void EmptyRecycleBin_Confirmed_ClearsBin()
    {
        var trash = new PostItNote();
        _store.Save(trash);
        _store.MoveToRecycleBin(trash);

        using var vm = NewVm(ShellWithConfirm(confirm: true));
        vm.ToggleRecycleBinCommand.Execute(null);
        vm.EmptyRecycleBinCommand.Execute(null);

        Assert.AreEqual(0, vm.RecycleBinNotes.Count);
        Assert.AreEqual(0, _store.LoadRecycleBin().Count);
    }

    [TestMethod]
    [CoversNode("recycle-empty")]
    public void EmptyRecycleBin_Cancelled_KeepsBin()
    {
        var trash = new PostItNote();
        _store.Save(trash);
        _store.MoveToRecycleBin(trash);

        using var vm = NewVm(ShellWithConfirm(confirm: false));
        vm.ToggleRecycleBinCommand.Execute(null);
        vm.EmptyRecycleBinCommand.Execute(null);

        Assert.AreEqual(1, vm.RecycleBinNotes.Count);
        Assert.AreEqual(1, _store.LoadRecycleBin().Count);
    }

    [TestMethod]
    [CoversNode("recycle-restore")]
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
    [CoversNode("recycle-delete")]
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
    [CoversNode("toolbar-zoom-fit")]
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
    [CoversNode("toolbar-zoom-fit")]
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
    [CoversNode("toolbar-zoom-fit")]
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
    [CoversNode("scratchpad-ai-act")]
    [CoversNode("scratchpad-ai-context")]
    public void GetClientTools_ExposesReadOnlyNoteTools()
    {
        using var vm = NewVm();
        var tools = ((IPageViewModel)vm).GetClientTools();
        var names = tools.Select(t => t.Name).ToList();
        CollectionAssert.Contains(names, "scratchpad_list_notes");
        CollectionAssert.Contains(names, "scratchpad_read_notes");
        Assert.IsTrue(tools.All(t => t.Safety == ToolSafety.SafeOperation));
        StringAssert.StartsWith(((IPageViewModel)vm).GetContext(), "Scratchpad");  // context names the board
        Assert.AreEqual("scratchpad", ((IPageViewModel)vm).GetSecurityContext());  // stable scope (aspect 4)
    }

    private IClientTool Tool(ScratchpadViewModel vm, string name)
        => ((IPageViewModel)vm).GetClientTools().First(t => t.Name == name);

    [TestMethod]
    [CoversNode("scratchpad-ai-act-scratchpad-read-notes")]
    public async Task ReadNotes_FiltersByColour()
    {
        _store.Save(new PostItNote { Content = "buy milk",   Color = "Green"  });
        _store.Save(new PostItNote { Content = "call alice", Color = "Yellow" });
        using var vm = NewVm();

        var result = await Tool(vm, "scratchpad_read_notes")
            .InvokeAsync(new JsonObject { ["color"] = "green" }, default);   // case-insensitive

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.ModelText, "buy milk");
        Assert.IsFalse(result.ModelText.Contains("call alice"));
    }

    [TestMethod]
    [CoversNode("scratchpad-ai-act-scratchpad-list-notes")]
    public async Task ListNotes_ReportsColourShapeAndOneLinePreview()
    {
        _store.Save(new PostItNote { Content = "first line\nsecond line", Color = "Green", Shape = "SpeechBubble" });
        using var vm = NewVm();

        var result = await Tool(vm, "scratchpad_list_notes").InvokeAsync(new JsonObject(), default);

        StringAssert.Contains(result.ModelText, "Green");
        StringAssert.Contains(result.ModelText, "SpeechBubble");
        StringAssert.Contains(result.ModelText, "first line");
        Assert.IsFalse(result.ModelText.Contains("second line"));   // preview is the first line only
    }

    [TestMethod]
    [CoversNode("scratchpad-ai-act-scratchpad-read-notes")]
    public async Task ReadNotes_EmptyBoard_SaysSo()
    {
        using var vm = NewVm();
        var result = await Tool(vm, "scratchpad_read_notes").InvokeAsync(new JsonObject(), default);
        StringAssert.Contains(result.ModelText, "no notes");
    }

    [TestMethod]
    [CoversNode("scratchpad-ai-act-scratchpad-add-note")]
    public async Task AddNote_CreatesFixedWhiteDiagonalNote()
    {
        using var vm = NewVm();
        var tool = Tool(vm, "scratchpad_add_note");
        Assert.AreEqual(ToolSafety.SafeOperation, tool.Safety);   // auto-runs, no approval

        var result = await tool.InvokeAsync(new JsonObject { ["content"] = "remember the milk" }, default);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, vm.Notes.Count);
        var note = vm.Notes[0];
        Assert.AreEqual("remember the milk", note.Content);
        Assert.AreEqual("White", note.Color);              // AI notes have a fixed appearance
        Assert.AreEqual("DiagonalsRounded", note.Shape);
    }

    [TestMethod]
    [CoversNode("scratchpad-ai-act-scratchpad-add-note")]
    public async Task AddNote_EmptyContent_IsRejected()
    {
        using var vm = NewVm();
        var result = await Tool(vm, "scratchpad_add_note").InvokeAsync(new JsonObject { ["content"] = "  " }, default);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(0, vm.Notes.Count);
    }

    [TestMethod]
    public void GetContext_PopulatedBoard_ListsColourBreakdown()
    {
        _store.Save(new PostItNote { Color = "Green" });
        _store.Save(new PostItNote { Color = "Green" });
        _store.Save(new PostItNote { Color = "Yellow" });
        using var vm = NewVm();

        var context = vm.GetContext();
        StringAssert.Contains(context, "3 note");
        StringAssert.Contains(context, "2 Green");
    }

    [TestMethod]
    public void Dispose_DoesNotThrow()
    {
        var vm = NewVm();
        vm.AddNote(new Point(0, 0));
        vm.Dispose();
    }
}
