using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Text.Editor.Commands;

namespace Nexaflow.Tests.Core.Unit.Editor;

[TestClass]
public class BuiltInTextEditorCommandsTests
{
    private sealed class Probe
    {
        public string Doc = string.Empty;
        public string Sel = string.Empty;
        public readonly List<(string label, string value)> Results = [];
        public readonly List<string> Errors = [];
        public TextEditorContext Ctx = null!;
    }

    private static Probe Make(string doc, string sel, bool hasSel = true, bool canEdit = true)
    {
        var p = new Probe { Doc = doc, Sel = sel };
        p.Ctx = new TextEditorContext
        {
            DocumentText     = doc,
            SelectedText     = sel,
            HasSelection     = hasSel,
            CanEdit          = canEdit,
            Encoding         = Encoding.UTF8,
            Eol              = LineEnding.Preserve,
            ReplaceDocument  = t => p.Doc = t,
            ReplaceSelection = t => p.Sel = t,
            ShowResult       = (l, v) => p.Results.Add((l, v)),
            ShowError        = e => p.Errors.Add(e),
        };
        return p;
    }

    private static ITextEditorCommand Cmd(string name) => BuiltInTextEditorCommands.All.First(c => c.Name == name);

    [TestMethod]
    public void SortLines_SortsDocument()
    {
        var p = Make("b\na\nc\n", "");
        var cmd = Cmd("Sort lines (A→Z)");
        Assert.IsTrue(cmd.CanExecute(p.Ctx));
        cmd.Execute(p.Ctx);
        Assert.AreEqual("a\nb\nc\n", p.Doc);
    }

    [TestMethod]
    public void RemoveEmptyLines_DropsBlanks()
    {
        var p = Make("a\n\nb\n", "");
        Cmd("Remove empty lines").Execute(p.Ctx);
        Assert.AreEqual("a\nb\n", p.Doc);
    }

    [TestMethod]
    public void Base64_EncodeThenDecode_RoundTrips()
    {
        var enc = Make("", "hello");
        Cmd("Base64 encode selection").Execute(enc.Ctx);
        Assert.AreEqual("aGVsbG8=", enc.Sel);

        var dec = Make("", "aGVsbG8=");
        Cmd("Base64 decode selection").Execute(dec.Ctx);
        Assert.AreEqual("hello", dec.Sel);
    }

    [TestMethod]
    public void Base64Decode_Invalid_ReportsErrorAndLeavesSelection()
    {
        var p = Make("", "!!not base64!!");
        Cmd("Base64 decode selection").Execute(p.Ctx);
        Assert.AreEqual(1, p.Errors.Count);
        Assert.AreEqual("!!not base64!!", p.Sel);
    }

    [TestMethod]
    public void ChecksumDocument_SurfacesHash()
    {
        var p = Make("abc", "");
        Cmd("SHA-256 of document").Execute(p.Ctx);
        Assert.AreEqual(("SHA-256", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"), p.Results[0]);
    }

    [TestMethod]
    public void SelectionCommands_DisabledWithoutSelection()
    {
        var p = Make("x", "", hasSel: false);
        Assert.IsFalse(Cmd("MD5 of selection").CanExecute(p.Ctx));
        Assert.IsFalse(Cmd("Base64 encode selection").CanExecute(p.Ctx));
    }

    [TestMethod]
    public void DestructiveCommands_DisabledWhenReadOnly_ButChecksumAllowed()
    {
        var p = Make("b\na\n", "", canEdit: false);
        Assert.IsFalse(Cmd("Sort lines (A→Z)").CanExecute(p.Ctx));
        Assert.IsTrue(Cmd("MD5 of document").CanExecute(p.Ctx));
    }
}
