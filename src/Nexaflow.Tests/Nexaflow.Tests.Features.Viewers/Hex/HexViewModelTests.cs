using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Hex.ViewModels;
using NSubstitute;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Hex;

[TestClass]
[CoversNode("hex-ui")]
public class HexViewModelTests
{
    private static readonly IShellServices _shell = Substitute.For<IShellServices>();

    private static string WriteTempFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hexvm_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [TestMethod]
    public void Ctor_NoFile_StartsReadOnly_WithAsciiEncoding()
    {
        using var vm = new HexViewModel(string.Empty, _shell);
        Assert.AreEqual(HexEditMode.ReadOnly, vm.EditMode);
        Assert.IsTrue(vm.IsReadOnly);
        Assert.AreEqual(HexEncoding.Auto, vm.Encoding);
        Assert.AreEqual(HexEncoding.Ascii, vm.ResolvedEncoding);
        Assert.AreEqual(0, vm.TotalRows);
    }

    [TestMethod]
    public void Ctor_LoadsFile_AndFormatsSize()
    {
        var path = WriteTempFile(new byte[100]);
        try
        {
            using var vm = new HexViewModel(path, _shell);
            Assert.AreEqual(path, vm.FilePath);
            Assert.AreEqual(100, vm.Buffer.VirtualLength);
            Assert.AreEqual("100 B", vm.FileSizeText);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void DetectEncoding_Utf8Bom()
    {
        var path = WriteTempFile(new byte[] { 0xEF, 0xBB, 0xBF, 0x41 });
        try
        {
            using var vm = new HexViewModel(path, _shell);
            Assert.AreEqual(HexEncoding.Utf8, vm.ResolvedEncoding);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void DetectEncoding_Utf16Le()
    {
        var path = WriteTempFile(new byte[] { 0xFF, 0xFE, 0x41, 0x00 });
        try
        {
            using var vm = new HexViewModel(path, _shell);
            Assert.AreEqual(HexEncoding.Utf16LE, vm.ResolvedEncoding);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void DetectEncoding_Utf16Be()
    {
        var path = WriteTempFile(new byte[] { 0xFE, 0xFF, 0x00, 0x41 });
        try
        {
            using var vm = new HexViewModel(path, _shell);
            Assert.AreEqual(HexEncoding.Utf16BE, vm.ResolvedEncoding);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void DetectEncoding_NoBomFallsBackToAscii()
    {
        var path = WriteTempFile(new byte[] { 0x48, 0x69 }); // "Hi"
        try
        {
            using var vm = new HexViewModel(path, _shell);
            Assert.AreEqual(HexEncoding.Ascii, vm.ResolvedEncoding);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void SetEncodingCommand_OverridesAutoDetection()
    {
        var path = WriteTempFile(new byte[] { 0xEF, 0xBB, 0xBF, 0x41 });
        try
        {
            using var vm = new HexViewModel(path, _shell);
            Assert.AreEqual(HexEncoding.Utf8, vm.ResolvedEncoding);

            vm.SetEncodingUtf16LECommand.Execute(null);
            Assert.AreEqual(HexEncoding.Utf16LE, vm.Encoding);
            Assert.AreEqual(HexEncoding.Utf16LE, vm.ResolvedEncoding);

            vm.SetEncodingAutoCommand.Execute(null);
            Assert.AreEqual(HexEncoding.Auto, vm.Encoding);
            Assert.AreEqual(HexEncoding.Utf8, vm.ResolvedEncoding); // re-detected
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void SetModeCommands_FlipBooleanFlags()
    {
        using var vm = new HexViewModel(string.Empty, _shell);

        vm.SetModeInsertCommand.Execute(null);
        Assert.AreEqual(HexEditMode.Insert, vm.EditMode);
        Assert.IsTrue(vm.IsInsert);
        Assert.IsFalse(vm.IsReadOnly);
        Assert.IsFalse(vm.IsOverwrite);

        vm.SetModeOverwriteCommand.Execute(null);
        Assert.AreEqual(HexEditMode.Overwrite, vm.EditMode);
        Assert.IsTrue(vm.IsOverwrite);
        Assert.IsFalse(vm.IsInsert);

        vm.SetModeReadOnlyCommand.Execute(null);
        Assert.AreEqual(HexEditMode.ReadOnly, vm.EditMode);
        Assert.IsTrue(vm.IsReadOnly);
        Assert.IsFalse(vm.IsInsert);
        Assert.IsFalse(vm.IsOverwrite);
    }

    [TestMethod]
    public void WriteByte_InReadOnlyMode_IsNoOp()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.WriteByte(0, 0xFF);
            Assert.IsFalse(vm.IsModified);
            Assert.AreEqual(1, vm.Buffer.ReadByte(0));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void WriteByte_OverwriteMode_ReplacesByte_AndKeepsLength()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.SetModeOverwriteCommand.Execute(null);
            vm.WriteByte(1, 0xAB);

            Assert.IsTrue(vm.IsModified);
            Assert.AreEqual(3, vm.Buffer.VirtualLength);
            Assert.AreEqual(0xAB, vm.Buffer.ReadByte(1));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void WriteByte_InsertMode_GrowsBuffer()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.SetModeInsertCommand.Execute(null);
            vm.WriteByte(1, 0x99);

            Assert.AreEqual(4, vm.Buffer.VirtualLength);
            Assert.AreEqual(0x99, vm.Buffer.ReadByte(1));
            Assert.AreEqual(2, vm.Buffer.ReadByte(2));
            Assert.IsTrue(vm.IsModified);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void DeleteByte_InReadOnlyMode_IsNoOp()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.DeleteByte(0);
            Assert.IsFalse(vm.IsModified);
            Assert.AreEqual(3, vm.Buffer.VirtualLength);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void DeleteByte_InEditMode_RemovesByte()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.SetModeInsertCommand.Execute(null);
            vm.DeleteByte(1);

            Assert.AreEqual(2, vm.Buffer.VirtualLength);
            Assert.AreEqual(1, vm.Buffer.ReadByte(0));
            Assert.AreEqual(3, vm.Buffer.ReadByte(1));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void SetCursor_ClampsToBufferBounds()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3, 4 });
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.SetCursor(2);
            Assert.AreEqual(2, vm.CursorOffset);

            vm.SetCursor(100);
            Assert.AreEqual(3, vm.CursorOffset); // VirtualLength - 1

            vm.SetCursor(-5);
            Assert.AreEqual(0, vm.CursorOffset);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void SetCursor_ClearsSelection()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3, 4 });
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.SetSelection(1, 2);
            vm.SetCursor(0);
            Assert.AreEqual(0, vm.SelectionLength);
            Assert.AreEqual(0, vm.SelectionStart);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void SetSelection_AppliesAndUpdatesSelectionText()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3, 4, 5 });
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.SetSelection(1, 3);
            Assert.AreEqual(1, vm.SelectionStart);
            Assert.AreEqual(3, vm.SelectionLength);
            Assert.AreEqual(1, vm.CursorOffset);
            StringAssert.Contains(vm.SelectionText, "Selection:");
            StringAssert.Contains(vm.SelectionText, "3");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ExtendSelection_GrowsForward_AndBackward()
    {
        var path = WriteTempFile(new byte[10]);
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.SetCursor(3);
            vm.ExtendSelection(6);
            Assert.AreEqual(3, vm.SelectionStart);
            Assert.AreEqual(4, vm.SelectionLength); // inclusive: 3..6

            vm.SetCursor(5);
            vm.ExtendSelection(1);
            Assert.AreEqual(1, vm.SelectionStart);
            Assert.AreEqual(5, vm.SelectionLength); // inclusive: 1..5
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void SelectionText_WhenNoSelection_ShowsOffset()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.SetCursor(1);
            StringAssert.Contains(vm.SelectionText, "Offset:");
            StringAssert.Contains(vm.SelectionText, "0x");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void GotoOffset_Hex_MovesCursorAndScrolls()
    {
        var data = new byte[1024];
        var path = WriteTempFile(data);
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.VisibleRowCount = 4;
            vm.GotoText = "0x100"; // 256 → row 16
            vm.GotoOffsetCommand.Execute(null);

            Assert.AreEqual(0x100, vm.CursorOffset);
            Assert.AreEqual(16, vm.TopRow);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void GotoOffset_Decimal_Fallback()
    {
        var data = new byte[256];
        var path = WriteTempFile(data);
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.GotoText = "32"; // valid hex too — parser will take hex path → 0x32 = 50
            vm.GotoOffsetCommand.Execute(null);
            Assert.AreEqual(0x32, vm.CursorOffset);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void GotoOffset_ClampsToBufferEnd()
    {
        var path = WriteTempFile(new byte[16]);
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.GotoText = "0xFFFFFF";
            vm.GotoOffsetCommand.Execute(null);
            Assert.AreEqual(15, vm.CursorOffset);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void GotoOffset_InvalidText_NoChange()
    {
        var path = WriteTempFile(new byte[64]);
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.SetCursor(5);
            vm.GotoText = "notahex";
            vm.GotoOffsetCommand.Execute(null);
            Assert.AreEqual(5, vm.CursorOffset);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ToggleEvaluatePane_FlipsFlag()
    {
        using var vm = new HexViewModel(string.Empty, _shell);
        Assert.IsTrue(vm.ShowEvaluatePane);
        vm.ToggleEvaluatePaneCommand.Execute(null);
        Assert.IsFalse(vm.ShowEvaluatePane);
        vm.ToggleEvaluatePaneCommand.Execute(null);
        Assert.IsTrue(vm.ShowEvaluatePane);
    }

    [TestMethod]
    public void UndoCommand_RevertsEdit_AndUpdatesIsModified()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.SetModeOverwriteCommand.Execute(null);
            vm.WriteByte(0, 0xAA);
            Assert.IsTrue(vm.IsModified);

            vm.UndoCommand.Execute(null);
            Assert.IsFalse(vm.IsModified);
            Assert.AreEqual(1, vm.Buffer.ReadByte(0));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ScrollToRow_ClampsToMaxRow()
    {
        var path = WriteTempFile(new byte[16 * 100]);
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.VisibleRowCount = 10;

            vm.ScrollToRow(-50);
            Assert.AreEqual(0, vm.TopRow);

            vm.ScrollToRow(1_000_000);
            Assert.AreEqual(vm.TotalRows - vm.VisibleRowCount, vm.TopRow);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void EnsureCursorVisible_ScrollsDownToCursor()
    {
        var path = WriteTempFile(new byte[16 * 100]);
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.VisibleRowCount = 10;
            vm.TopRow = 0;
            vm.SetCursor(16 * 50); // row 50, off-screen below
            vm.EnsureCursorVisible();
            Assert.AreEqual(50 - 10 + 1, vm.TopRow);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void EnsureCursorVisible_ScrollsUpToCursor()
    {
        var path = WriteTempFile(new byte[16 * 100]);
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.VisibleRowCount = 10;
            vm.TopRow = 80;
            vm.SetCursor(16 * 5); // row 5, off-screen above
            vm.EnsureCursorVisible();
            Assert.AreEqual(5, vm.TopRow);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void InvalidateView_FiresOnTopRowChange()
    {
        var path = WriteTempFile(new byte[1024]);
        try
        {
            using var vm = new HexViewModel(path, _shell);
            int fires = 0;
            vm.InvalidateView += () => fires++;
            vm.TopRow = 5;
            Assert.IsTrue(fires > 0);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void GetContext_IncludesFileAndMode()
    {
        var path = WriteTempFile(new byte[16]);
        try
        {
            using var vm = new HexViewModel(path, _shell);
            vm.SetModeInsertCommand.Execute(null);
            var ctx = vm.GetContext();
            StringAssert.Contains(ctx, path);
            StringAssert.Contains(ctx, "Insert");
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Stepping and clamping belong to the shared <c>TextZoom</c> (pinned in <c>TextZoomTests</c>); what is
    /// hex's alone is that the tab carries one, because both panes read the same instance and that is what
    /// keeps the byte grid and the decoded text row-aligned.
    /// </summary>
    [TestMethod]
    [CoversNode("hex-zoom")]
    public void Zoom_ScalesTheGridTextSize()
    {
        using var vm = new HexViewModel(string.Empty, _shell);
        Assert.AreEqual(100, vm.Zoom.Percent, "a freshly opened file is unzoomed");

        var unzoomed = vm.Zoom.FontSize;
        vm.Zoom.Percent = 150;
        Assert.AreEqual(unzoomed * 1.5, vm.Zoom.FontSize, 1e-9, "both panes bind this one size");
    }
}
