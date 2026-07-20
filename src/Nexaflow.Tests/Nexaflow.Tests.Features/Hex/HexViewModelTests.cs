using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Hex.ViewModels;
using NSubstitute;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Hex;

[TestClass]
[CoversNode("view-hex")]
public class HexViewModelTests
{
    private static readonly IShellServices _shell = Substitute.For<IShellServices>();

    private static string WriteTempFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hexvm_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>A shell whose RunOnUiAsync actually runs the action — the plain substitute swallows it,
    /// no-opping every UI-marshalled tool (goto / select / set-encoding / overwrite / save).</summary>
    private static IShellServices RunningShell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>())
             .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        return shell;
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

    // ── AI integration: enriched context + the read/navigate/mutate act tools ────

    [TestMethod]
    [CoversNode("hex-ai-act")]
    [CoversNode("hex-ai-context")]
    public async Task AiTools_ReadNavigateAndOverwrite_ThroughToolSurface()
    {
        // "Hello" + DE AD BE EF + 00 01 02 03  (13 bytes, no BOM → ASCII)
        var sample = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02, 0x03 };
        var path   = WriteTempFile(sample);
        try
        {
            using var vm = new HexViewModel(path, RunningShell());

            // Scope: distinct security context so two pinned Hex tabs don't collapse first-wins.
            Assert.AreEqual(path, vm.GetSecurityContext());

            // Context is honest about size, edit mode, resolved encoding, and cursor state.
            var ctx = vm.GetContext();
            StringAssert.Contains(ctx, path);
            StringAssert.Contains(ctx, "13 bytes");
            StringAssert.Contains(ctx, "ReadOnly");
            StringAssert.Contains(ctx, "Ascii");
            StringAssert.Contains(ctx, "Cursor: unset");

            var tools = vm.GetClientTools();
            CollectionAssert.AreEquivalent(
                new[] { "read_bytes", "find_bytes", "get_selection", "goto_offset",
                        "select_range", "set_encoding", "overwrite_bytes", "save" },
                tools.Select(t => t.Name).ToArray(),
                "the Hex AI act tool surface changed — update the tree's hex-ai-act leaves to match");

            // read_bytes: reaches any range as hex + ASCII.
            var read = tools.Single(t => t.Name == "read_bytes");
            var r0 = await read.InvokeAsync(new JsonObject { ["offset"] = "0x0", ["length"] = 5 }, CancellationToken.None);
            Assert.IsFalse(r0.IsError);
            StringAssert.Contains(r0.ModelText, "48 65 6C 6C 6F");   // hex
            StringAssert.Contains(r0.ModelText, "Hello");            // ASCII gutter
            var rBad = await read.InvokeAsync(new JsonObject { ["offset"] = "0x100" }, CancellationToken.None);
            Assert.IsTrue(rBad.IsError);                             // out of range is reported, not thrown

            // find_bytes: hex pattern and text pattern both resolve to the right offset.
            var find = tools.Single(t => t.Name == "find_bytes");
            var fHex = await find.InvokeAsync(new JsonObject { ["pattern"] = "DEADBEEF" }, CancellationToken.None);
            StringAssert.Contains(fHex.ModelText, "0x5");
            var fText = await find.InvokeAsync(new JsonObject { ["pattern"] = "Hello", ["type"] = "text" }, CancellationToken.None);
            StringAssert.Contains(fText.ModelText, "0x0");

            // goto_offset: moves the cursor (UI-marshalled → needs the running shell).
            var goto_ = tools.Single(t => t.Name == "goto_offset");
            await goto_.InvokeAsync(new JsonObject { ["offset"] = "0x8" }, CancellationToken.None);
            Assert.AreEqual(8, vm.CursorOffset);

            // select_range + get_selection round-trip.
            var select = tools.Single(t => t.Name == "select_range");
            await select.InvokeAsync(new JsonObject { ["offset"] = 0, ["length"] = 5 }, CancellationToken.None);
            Assert.AreEqual(0, vm.SelectionStart);
            Assert.AreEqual(5, vm.SelectionLength);
            var sel = await tools.Single(t => t.Name == "get_selection")
                                 .InvokeAsync(new JsonObject(), CancellationToken.None);
            StringAssert.Contains(sel.ModelText, "5 bytes");
            StringAssert.Contains(sel.ModelText, "Hello");

            // set_encoding: mirrors the toolbar toggle.
            var setEnc = tools.Single(t => t.Name == "set_encoding");
            await setEnc.InvokeAsync(new JsonObject { ["encoding"] = "utf16le" }, CancellationToken.None);
            Assert.AreEqual(HexEncoding.Utf16LE, vm.Encoding);
            Assert.AreEqual(HexEncoding.Utf16LE, vm.ResolvedEncoding);

            // overwrite_bytes: in-place edit, marked dirty, uncommitted until save.
            var overwrite = tools.Single(t => t.Name == "overwrite_bytes");
            var ov = await overwrite.InvokeAsync(new JsonObject { ["offset"] = "0x0", ["bytes"] = "41" }, CancellationToken.None);
            Assert.IsFalse(ov.IsError);
            Assert.AreEqual(0x41, vm.Buffer.ReadByte(0));
            Assert.IsTrue(vm.IsModified);

            // save: commits to disk and clears dirty.
            var save = tools.Single(t => t.Name == "save");
            var s = await save.InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.IsFalse(s.IsError);
            Assert.IsFalse(vm.IsModified);
            // The VM keeps the file open ReadWrite (FileShare.Read), so a default File.ReadAllBytes
            // (FileShare.Read) collides with its write handle — read with FileShare.ReadWrite.
            using (var verify = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                Assert.AreEqual(0x41, verify.ReadByte());
        }
        finally { File.Delete(path); }
    }
}
