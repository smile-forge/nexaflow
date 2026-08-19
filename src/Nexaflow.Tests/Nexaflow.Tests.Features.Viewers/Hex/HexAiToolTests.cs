using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Hex.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Hex;

/// <summary>
/// The Hex editor's AI surface, one test per tool.
/// <para>
/// A hex editor is the one viewer where the model genuinely cannot see what it is working on — the tab
/// shows sixteen bytes a row and the file may be gigabytes. So <c>read_bytes</c> and <c>find_bytes</c>
/// deliberately reach past the visible window, and the context has to be honest about where the cursor and
/// the window actually are, or every offset the model quotes is relative to a position it guessed.
/// </para>
/// </summary>
[TestClass]
public class HexAiToolTests
{
    // "Hello" + DE AD BE EF + 00 01 02 03 — 13 bytes, no BOM, so it resolves to ASCII.
    private static readonly byte[] Sample =
        [0x48, 0x65, 0x6C, 0x6C, 0x6F, 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02, 0x03];

    private string _path = string.Empty;

    [TestInitialize]
    public void WriteSample()
    {
        _path = Path.Combine(Path.GetTempPath(), $"hexai_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(_path, Sample);
    }

    [TestCleanup]
    public void RemoveSample() { try { File.Delete(_path); } catch { } }

    /// <summary>A shell whose RunOnUiAsync actually runs the action — the plain substitute swallows it,
    /// no-opping every UI-marshalled tool (goto / select / set-encoding / overwrite / save).</summary>
    private HexViewModel Loaded()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>())
             .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        return new HexViewModel(_path, shell);
    }

    private static Task<ToolResult> Run(HexViewModel vm, string tool, JsonObject? args = null)
        => vm.GetClientTools().Single(t => t.Name == tool).InvokeAsync(args ?? [], CancellationToken.None);

    // ── The surface ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("hex-ai-act")]
    public void TheToolSurfaceIsExactlyWhatTheTreeSaysItIs()
    {
        using var vm = Loaded();

        CollectionAssert.AreEquivalent(
            new[] { "read_bytes", "find_bytes", "get_selection", "goto_offset",
                    "select_range", "set_encoding", "overwrite_bytes", "save" },
            vm.GetClientTools().Select(t => t.Name).ToArray(),
            "the Hex AI act tool surface changed — update the tree's hex-ai-act leaves to match");
    }

    [TestMethod]
    [CoversNode("hex-ai-context")]
    public void TwoOpenFilesAreDistinctScopes_NotFirstWins()
    {
        using var vm = Loaded();

        Assert.AreEqual(_path, vm.GetSecurityContext(),
                        "two pinned Hex tabs must expose separately-named tool contexts");
    }

    // ── Context honesty ───────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("hex-ai-context")]
    public void TheContextReportsTheFile_ItsSize_TheModeAndTheResolvedEncoding()
    {
        using var vm = Loaded();

        var ctx = vm.GetContext();

        StringAssert.Contains(ctx, _path);
        StringAssert.Contains(ctx, "13 bytes");
        StringAssert.Contains(ctx, "ReadOnly");
        StringAssert.Contains(ctx, "Ascii", "the *resolved* encoding, not the Auto setting that produced it");
    }

    [TestMethod]
    [CoversNode("hex-ai-context")]
    public void TheContextSaysWhereTheCursorIs_AndWhereItIsNot()
    {
        using var vm = Loaded();
        StringAssert.Contains(vm.GetContext(), "Cursor: unset",
                              "claiming a cursor position before there is one invents an anchor");

        vm.SetCursor(8);

        StringAssert.Contains(vm.GetContext(), "0x8");
    }

    [TestMethod]
    [CoversNode("hex-ai-context")]
    public void TheContextSaysWhatIsSelected_AndWhichBytesAreOnScreen()
    {
        // The whole point of the surface is that the model reads past what is displayed. It can only place
        // what it reads if it is told which window the user is actually looking at.
        using var vm = Loaded();
        StringAssert.Contains(vm.GetContext(), "Selection: none");

        vm.SetSelection(0, 5);
        var ctx = vm.GetContext();

        StringAssert.Contains(ctx, "5 bytes");
        StringAssert.Contains(ctx, "Visible: rows", "the visible window is stated, not left to be inferred");
    }

    [TestMethod]
    [CoversNode("hex-ai-context")]
    public void UnsavedEditsAreDeclared_SoTheModelKnowsDiskAndScreenDisagree()
    {
        using var vm = Loaded();
        Assert.IsFalse(vm.GetContext().Contains("unsaved"));

        vm.SetModeOverwriteCommand.Execute(null);
        vm.WriteByte(0, 0x41);

        StringAssert.Contains(vm.GetContext(), "unsaved edits");
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("hex-ai-act-read-bytes")]
    public async Task ReadBytes_ReturnsBothTheHexAndTheAsciiGutter()
    {
        using var vm = Loaded();

        var r = await Run(vm, "read_bytes", new JsonObject { ["offset"] = "0x0", ["length"] = 5 });

        Assert.IsFalse(r.IsError);
        StringAssert.Contains(r.ModelText, "48 65 6C 6C 6F");
        StringAssert.Contains(r.ModelText, "Hello", "the ASCII gutter is how text inside a binary is spotted");
    }

    [TestMethod]
    [CoversNode("hex-ai-act-read-bytes")]
    public async Task ReadBytes_PastTheEndIsReported_NotThrown()
    {
        using var vm = Loaded();

        var r = await Run(vm, "read_bytes", new JsonObject { ["offset"] = "0x100" });

        Assert.IsTrue(r.IsError, "an offset outside the file is an answerable mistake, not a crash");
    }

    [TestMethod]
    [CoversNode("hex-ai-act-find-bytes")]
    public async Task FindBytes_TakesAHexPatternOrText_AndBothLandOnTheSameKindOfOffset()
    {
        using var vm = Loaded();

        var hex = await Run(vm, "find_bytes", new JsonObject { ["pattern"] = "DEADBEEF" });
        StringAssert.Contains(hex.ModelText, "0x5");

        var text = await Run(vm, "find_bytes", new JsonObject { ["pattern"] = "Hello", ["type"] = "text" });
        StringAssert.Contains(text.ModelText, "0x0", "a text pattern is the same search over the same bytes");
    }

    [TestMethod]
    [CoversNode("hex-ai-act-get-selection")]
    public async Task GetSelection_ReportsTheRangeAndItsBytes()
    {
        using var vm = Loaded();
        await Run(vm, "select_range", new JsonObject { ["offset"] = 0, ["length"] = 5 });

        var sel = await Run(vm, "get_selection");

        StringAssert.Contains(sel.ModelText, "5 bytes");
        StringAssert.Contains(sel.ModelText, "Hello");
    }

    // ── Navigating ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("hex-ai-act-goto-offset")]
    public async Task GotoOffset_MovesTheUsersCursor()
    {
        using var vm = Loaded();

        await Run(vm, "goto_offset", new JsonObject { ["offset"] = "0x8" });

        Assert.AreEqual(8, vm.CursorOffset, "the model navigates the real view, not a private one");
    }

    [TestMethod]
    [CoversNode("hex-ai-act-select-range")]
    public async Task SelectRange_SelectsInTheView()
    {
        using var vm = Loaded();

        await Run(vm, "select_range", new JsonObject { ["offset"] = 0, ["length"] = 5 });

        Assert.AreEqual(0, vm.SelectionStart);
        Assert.AreEqual(5, vm.SelectionLength);
    }

    [TestMethod]
    [CoversNode("hex-ai-act-set-encoding")]
    public async Task SetEncoding_OverridesTheSniffedOne()
    {
        using var vm = Loaded();
        Assert.AreEqual(HexEncoding.Ascii, vm.ResolvedEncoding, "sniffed from a file with no BOM");

        await Run(vm, "set_encoding", new JsonObject { ["encoding"] = "utf16le" });

        Assert.AreEqual(HexEncoding.Utf16LE, vm.Encoding);
        Assert.AreEqual(HexEncoding.Utf16LE, vm.ResolvedEncoding,
                        "an explicit choice wins over the sniff, exactly as the toolbar combo does");
    }

    // ── Writing ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("hex-ai-act-overwrite-bytes")]
    public async Task OverwriteBytes_EditsInPlace_ButLeavesDiskAlone()
    {
        using var vm = Loaded();

        var r = await Run(vm, "overwrite_bytes", new JsonObject { ["offset"] = "0x0", ["bytes"] = "41" });

        Assert.IsFalse(r.IsError);
        Assert.AreEqual(0x41, vm.Buffer.ReadByte(0));
        Assert.IsTrue(vm.IsModified, "the edit is uncommitted — the user still has undo and a Save button");
        CollectionAssert.AreEqual(Sample, ReadBack(), "nothing reached the file");
    }

    [TestMethod]
    [CoversNode("hex-ai-act-save")]
    public async Task Save_IsWhatFinallyTouchesTheFile()
    {
        using var vm = Loaded();
        await Run(vm, "overwrite_bytes", new JsonObject { ["offset"] = "0x0", ["bytes"] = "41" });

        var s = await Run(vm, "save");

        Assert.IsFalse(s.IsError);
        Assert.IsFalse(vm.IsModified);
        Assert.AreEqual(0x41, ReadBack()[0]);
    }

    /// <summary>The VM holds the file open ReadWrite (FileShare.Read), so a default File.ReadAllBytes
    /// collides with its handle — read sharing ReadWrite.</summary>
    private byte[] ReadBack()
    {
        using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buf = new byte[fs.Length];
        fs.ReadExactly(buf);
        return buf;
    }
}
