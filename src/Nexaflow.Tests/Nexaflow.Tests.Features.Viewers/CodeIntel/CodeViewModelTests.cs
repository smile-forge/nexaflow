using System.Collections.Generic;
using Nexaflow.Features.Code.ViewModels;
using Nexaflow.Features.Common;
using NSubstitute;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.CodeIntel;

/// <summary>
/// Headless command/state logic behind the "As Code" view-model — the map-panel toggle, the derived
/// visibility gate, grammar resolution, and the buffer-driven intelligence rebuild. Distinct from
/// <see cref="CodeIntelligenceTests"/> (which covers the extractor / markdown-builder / file actions):
/// this exercises the <see cref="CodeViewModel"/> orchestration around them. No editor control is
/// touched — the buffer is seeded via <c>Document.Text</c> and the tree-sitter parse runs in-process.
/// </summary>
[TestClass]
[CoversNode("code-map")]
public class CodeViewModelTests
{
    private const string CSharp = """
        namespace Demo;

        public class Calculator
        {
            public int Add(int a, int b) => a + b;
            public void Reset() { }
        }
        """;

    private static CodeViewModel Make(string fileName, IShellServices? shell = null)
        => new(fileName, shell ?? Substitute.For<IShellServices>(), maxEditableBytes: long.MaxValue);

    // ── Grammar resolution ────────────────────────────────────────────────────

    [TestMethod]
    public void GrammarId_ResolvesForCodeFile()
        => Assert.AreEqual("c-sharp", Make(@"C:\proj\calc.cs").GrammarId);

    [TestMethod]
    public void GrammarId_IsNull_ForNonCodeFile()
        => Assert.IsNull(Make(@"C:\proj\notes.bin").GrammarId, "a file with no code grammar keeps the panel hidden");

    // ── Panel toggle + derived visibility ─────────────────────────────────────

    [TestMethod]
    [CoversNode("code-map-toggle")]
    public void TogglePanel_FlipsOpenState()
    {
        var vm = Make(@"C:\proj\calc.cs");
        Assert.IsTrue(vm.IsPanelOpen, "the map panel starts open");

        vm.TogglePanelCommand.Execute(null);
        Assert.IsFalse(vm.IsPanelOpen);

        vm.TogglePanelCommand.Execute(null);
        Assert.IsTrue(vm.IsPanelOpen);
    }

    [TestMethod]
    public void PanelVisible_RequiresBothOpenAndIntelligence()
    {
        var vm = Make(@"C:\proj\calc.cs");
        Assert.IsFalse(vm.PanelVisible, "open but no intelligence yet ⇒ nothing to show");

        vm.Document.Text = CSharp;
        vm.RegenerateIntelligence();
        Assert.IsTrue(vm.HasIntelligence);
        Assert.IsTrue(vm.PanelVisible, "intelligence present + panel open ⇒ visible");

        vm.TogglePanelCommand.Execute(null);   // collapse
        Assert.IsFalse(vm.PanelVisible, "collapsed hides it even though intelligence exists");
    }

    [TestMethod]
    public void PanelVisible_RaisesChange_OnToggleAndOnIntelligence()
    {
        var vm = Make(@"C:\proj\calc.cs");
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Document.Text = CSharp;
        vm.RegenerateIntelligence();            // HasIntelligence flips → PanelVisible notifies
        vm.TogglePanelCommand.Execute(null);    // IsPanelOpen flips → PanelVisible notifies

        CollectionAssert.Contains(raised, nameof(CodeViewModel.PanelVisible));
    }

    // ── Intelligence rebuild ──────────────────────────────────────────────────

    [TestMethod]
    public void RegenerateIntelligence_BuildsMarkdown_ForCode()
    {
        var vm = Make(@"C:\proj\calc.cs");
        vm.Document.Text = CSharp;

        vm.RegenerateIntelligence();

        Assert.IsTrue(vm.HasIntelligence);
        StringAssert.Contains(vm.IntelligenceMarkdown, "classDiagram");
        StringAssert.Contains(vm.IntelligenceMarkdown, "class Calculator");
    }

    [TestMethod]
    public void RegenerateIntelligence_NoGrammar_StaysHidden()
    {
        var vm = Make(@"C:\proj\notes.bin");
        vm.Document.Text = CSharp;   // content is irrelevant — there is no grammar for .bin

        vm.RegenerateIntelligence();

        Assert.IsFalse(vm.HasIntelligence);
        Assert.AreEqual(string.Empty, vm.IntelligenceMarkdown);
    }

    [TestMethod]
    public void RegenerateIntelligence_EmptyBuffer_ClearsIntelligence()
    {
        var vm = Make(@"C:\proj\calc.cs");
        vm.Document.Text = CSharp;
        vm.RegenerateIntelligence();
        Assert.IsTrue(vm.HasIntelligence);

        vm.Document.Text = string.Empty;   // no types/members left
        vm.RegenerateIntelligence();

        Assert.IsFalse(vm.HasIntelligence, "an empty outline hides the panel again");
        Assert.AreEqual(string.Empty, vm.IntelligenceMarkdown);
    }

    // ── Navigation coupling ───────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("code-map-nav")]
    public void NavigateToAst_RaisesScrollRequest_ForKnownMember()
    {
        var vm = Make(@"C:\proj\calc.cs");
        vm.Document.Text = CSharp;
        int? scrolledTo = null;
        vm.ScrollToLineRequested += line => scrolledTo = line;

        vm.NavigateToAst("T:Calculator/M:Reset");

        Assert.IsTrue(scrolledTo is > 0, "resolves the member to its 1-based line and asks the view to scroll");
    }

    [TestMethod]
    [CoversNode("code-map-nav")]
    public void NavigateToAst_UnknownMember_DoesNotScroll()
    {
        var vm = Make(@"C:\proj\calc.cs");
        vm.Document.Text = CSharp;
        bool scrolled = false;
        vm.ScrollToLineRequested += _ => scrolled = true;

        vm.NavigateToAst("T:Calculator/M:DoesNotExist");

        Assert.IsFalse(scrolled, "an unresolved path is a no-op, not a scroll to line 0");
    }

    // ── Cross-file open ───────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("code-map-nav")]
    public void OpenCode_OpensCodeTab_WithPathAndAst()
    {
        var shell = Substitute.For<IShellServices>();
        var vm = Make(@"C:\proj\calc.cs", shell);

        vm.OpenCode(@"C:\proj\other.cs", "T:Other/M:Ping");

        shell.Received().OpenTab("Code",
            Arg.Is<Dictionary<string, string>>(d => d["path"] == @"C:\proj\other.cs" && d["ast"] == "T:Other/M:Ping"),
            Arg.Any<IPageView?>());
    }

    [TestMethod]
    [CoversNode("code-map-nav")]
    public void OpenCode_OmitsAst_WhenNull()
    {
        var shell = Substitute.For<IShellServices>();
        var vm = Make(@"C:\proj\calc.cs", shell);

        vm.OpenCode(@"C:\proj\other.cs", null);

        shell.Received().OpenTab("Code",
            Arg.Is<Dictionary<string, string>>(d => d["path"] == @"C:\proj\other.cs" && !d.ContainsKey("ast")),
            Arg.Any<IPageView?>());
    }
}
