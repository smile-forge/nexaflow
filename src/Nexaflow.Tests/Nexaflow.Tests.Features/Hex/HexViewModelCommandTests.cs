using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Hex.ViewModels;
using NSubstitute;

namespace Nexaflow.Tests.Features.Hex;

/// <summary>
/// Headless coverage of the Hex toolbar-command <b>enabled/disabled state</b> — the <c>CanExecute</c>
/// gating that drives the Save / Undo / Redo buttons — plus the dirty→clean save cycle at the ViewModel
/// layer. Complements <see cref="HexViewModelTests"/> (which exercises command <i>effects</i>) and
/// <c>HexBufferTests</c> (piece-table mutations); nothing here re-asserts those. NSubstitute stands in for
/// the shell so the save-failure branch can be observed without a real UI.
/// </summary>
[TestClass]
public class HexViewModelCommandTests
{
    private static string WriteTempFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hexvmcmd_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // ── Save / SaveAs gating ────────────────────────────────────────────────────

    [TestMethod]
    public void SaveCommands_DisabledUntilModified_ThenEnabled()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var vm = new HexViewModel(path, Substitute.For<IShellServices>());
            Assert.IsFalse(vm.IsModified);
            Assert.IsFalse(vm.SaveCommand.CanExecute(null), "Save should be disabled on a clean buffer.");
            Assert.IsFalse(vm.SaveAsCommand.CanExecute(null), "Save As should be disabled on a clean buffer.");

            vm.SetModeOverwriteCommand.Execute(null);
            vm.WriteByte(0, 0xEE);

            Assert.IsTrue(vm.IsModified);
            Assert.IsTrue(vm.SaveCommand.CanExecute(null), "Save should enable once the buffer is dirty.");
            Assert.IsTrue(vm.SaveAsCommand.CanExecute(null), "Save As should enable once the buffer is dirty.");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Save_PersistsEdit_ClearsDirty_AndRedisablesSave()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            var vm = new HexViewModel(path, Substitute.For<IShellServices>());
            vm.SetModeOverwriteCommand.Execute(null);
            vm.WriteByte(0, 0x42);
            Assert.IsTrue(vm.SaveCommand.CanExecute(null));

            vm.SaveCommand.Execute(null);

            Assert.IsFalse(vm.IsModified, "Save must clear the dirty flag.");
            Assert.IsFalse(vm.SaveCommand.CanExecute(null), "Save must re-disable after a clean save.");
            Assert.IsFalse(vm.UndoCommand.CanExecute(null), "Save must clear the undo stack gating.");
            Assert.IsFalse(vm.RedoCommand.CanExecute(null), "Save must clear the redo stack gating.");

            vm.Dispose();   // release the buffer's file handle before reading it back
            CollectionAssert.AreEqual(new byte[] { 0x42, 2, 3 }, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Save_WhenBufferSaveThrows_ReportsViaShell_AndStaysDirty()
    {
        // Read-only file makes the atomic File.Replace path throw inside Buffer.Save.
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            var shell = Substitute.For<IShellServices>();
            using var vm = new HexViewModel(path, shell);
            vm.SetModeOverwriteCommand.Execute(null);
            vm.WriteByte(0, 0x55);

            File.SetAttributes(path, FileAttributes.ReadOnly);
            vm.SaveCommand.Execute(null);

            shell.Received().ShowError(Arg.Is<string>(m => m.Contains("Save failed")));
            Assert.IsTrue(vm.IsModified, "A failed save must leave the buffer dirty.");
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }

    // ── Undo / Redo availability ────────────────────────────────────────────────

    [TestMethod]
    public void UndoRedo_CanExecute_TracksBufferHistory()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var vm = new HexViewModel(path, Substitute.For<IShellServices>());
            Assert.IsFalse(vm.UndoCommand.CanExecute(null), "No history yet — undo disabled.");
            Assert.IsFalse(vm.RedoCommand.CanExecute(null), "No history yet — redo disabled.");

            vm.SetModeOverwriteCommand.Execute(null);
            vm.WriteByte(0, 0xAA);
            Assert.IsTrue(vm.UndoCommand.CanExecute(null), "After an edit undo should be available.");
            Assert.IsFalse(vm.RedoCommand.CanExecute(null), "Redo stays disabled until an undo happens.");

            vm.UndoCommand.Execute(null);
            Assert.IsFalse(vm.UndoCommand.CanExecute(null), "Stack emptied — undo disabled again.");
            Assert.IsTrue(vm.RedoCommand.CanExecute(null), "An undo enables redo.");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void RedoCommand_ReappliesEdit_AndRestoresDirtyState()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var vm = new HexViewModel(path, Substitute.For<IShellServices>());
            vm.SetModeOverwriteCommand.Execute(null);
            vm.WriteByte(0, 0xBC);
            vm.UndoCommand.Execute(null);
            Assert.AreEqual(1, vm.Buffer.ReadByte(0));
            Assert.IsFalse(vm.IsModified);

            vm.RedoCommand.Execute(null);

            Assert.AreEqual(0xBC, vm.Buffer.ReadByte(0), "Redo must re-apply the edit.");
            Assert.IsTrue(vm.IsModified, "Redo restores the dirty flag.");
            Assert.IsFalse(vm.RedoCommand.CanExecute(null), "Redo stack emptied — redo disabled.");
            Assert.IsTrue(vm.SaveCommand.CanExecute(null), "Re-applied edit re-enables Save.");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void NewEdit_AfterUndo_ClearsRedoAvailability()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var vm = new HexViewModel(path, Substitute.For<IShellServices>());
            vm.SetModeOverwriteCommand.Execute(null);
            vm.WriteByte(0, 0xAA);
            vm.UndoCommand.Execute(null);
            Assert.IsTrue(vm.RedoCommand.CanExecute(null));

            vm.WriteByte(1, 0xBB);      // a fresh edit invalidates the redo branch
            Assert.IsFalse(vm.RedoCommand.CanExecute(null));
            Assert.IsTrue(vm.UndoCommand.CanExecute(null));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void MultipleEdits_UndoStackUnwinds_ToCleanState()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var vm = new HexViewModel(path, Substitute.For<IShellServices>());
            vm.SetModeOverwriteCommand.Execute(null);
            vm.WriteByte(0, 0x11);
            vm.WriteByte(1, 0x22);
            Assert.IsTrue(vm.IsModified);

            vm.UndoCommand.Execute(null);
            Assert.IsTrue(vm.IsModified, "One edit remains after a single undo.");
            Assert.IsTrue(vm.UndoCommand.CanExecute(null));

            vm.UndoCommand.Execute(null);
            Assert.IsFalse(vm.IsModified, "Buffer is clean once every edit is undone.");
            Assert.IsFalse(vm.UndoCommand.CanExecute(null));
            Assert.AreEqual(1, vm.Buffer.ReadByte(0));
            Assert.AreEqual(2, vm.Buffer.ReadByte(1));
        }
        finally { File.Delete(path); }
    }

    // ── Goto clamping edge cases not covered elsewhere ──────────────────────────

    [TestMethod]
    public void GotoOffset_OnEmptyBuffer_LeavesCursorUnset()
    {
        using var vm = new HexViewModel(string.Empty, Substitute.For<IShellServices>());
        vm.GotoText = "0x10";
        vm.GotoOffsetCommand.Execute(null);
        Assert.AreEqual(0, vm.CursorOffset, "With no bytes, goto clamps to offset 0.");
        Assert.AreEqual(0, vm.TopRow);
    }

    [TestMethod]
    public void GotoOffset_EmptyText_JumpsToStart()
    {
        var path = WriteTempFile(new byte[64]);
        try
        {
            using var vm = new HexViewModel(path, Substitute.For<IShellServices>());
            vm.SetCursor(20);
            vm.GotoText = "   ";           // whitespace → parses as 0
            vm.GotoOffsetCommand.Execute(null);
            Assert.AreEqual(0, vm.CursorOffset);
        }
        finally { File.Delete(path); }
    }
}
