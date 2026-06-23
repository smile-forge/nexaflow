using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Features.Code;
using Nexaflow.Features.Code.FileActions;
using Nexaflow.Features.Common;
using Nexaflow.Features.Text.FileActions;
using Nexaflow.Syntax;
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;
using NSubstitute;

namespace Nexaflow.Tests.Features.CodeIntel;

/// <summary>
/// Covers the "As Code" structure pipeline: tree-sitter outline extraction + stable AST-path resolution, the
/// markdown the panel renders (dependency links + clickable mermaid), the parser's per-member link token, and
/// the As Code / As Text actions.
/// </summary>
[TestClass]
public class CodeIntelligenceTests
{
    private const string CSharp = """
        using System;
        using System.Text;

        namespace Demo;

        public class Calculator
        {
            private int _total;
            public int Total => _total;
            public int Add(int a) => a;
            public int Add(int a, int b) => a + b;
            public void Reset() { _total = 0; }

            public class Inner
            {
                public void Ping() { }
            }
        }
        """;

    // ── Extraction ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Extract_CSharp_FindsImportsTypesAndMembers()
    {
        var outline = new CodeStructureExtractor().Extract("c-sharp", CSharp);

        Assert.AreEqual(2, outline.Imports.Count, "two using directives");
        Assert.IsTrue(outline.Imports.All(i => i.ResolvedPath is null), "namespaces don't resolve to files");

        var calc = outline.Types.Single(t => t.AstPath == "T:Calculator");
        Assert.AreEqual(OutlineKind.Class, calc.Kind);

        var inner = outline.Types.Single(t => t.AstPath == "T:Calculator/T:Inner");
        Assert.AreEqual("Inner", inner.Name);
        Assert.IsTrue(inner.Members.Any(m => m.AstPath == "T:Calculator/T:Inner/M:Ping"));

        // overloads disambiguate with #index, in declaration order
        CollectionAssert.IsSubsetOf(
            new[] { "T:Calculator/M:Add#0", "T:Calculator/M:Add#1" },
            calc.Members.Select(m => m.AstPath).ToArray());

        Assert.IsTrue(calc.Members.Any(m => m is { Name: "Total", Kind: OutlineKind.Property }));
        Assert.IsTrue(calc.Members.Any(m => m is { Name: "_total", Kind: OutlineKind.Field }));
    }

    [TestMethod]
    public void ResolveLine_IsStableAcrossInsertedLines()
    {
        var ext = new CodeStructureExtractor();
        var line1 = ext.ResolveLine("c-sharp", CSharp, "T:Calculator/M:Reset");
        var line2 = ext.ResolveLine("c-sharp", "\n\n" + CSharp, "T:Calculator/M:Reset");

        Assert.IsNotNull(line1);
        Assert.IsNotNull(line2);
        Assert.AreEqual(line1!.Value + 2, line2!.Value, "the path is unchanged; the line just shifts down");
    }

    [TestMethod]
    public void ResolveLine_ReturnsNull_WhenMemberRenamed()
    {
        var ext = new CodeStructureExtractor();
        var renamed = CSharp.Replace("Reset", "Clear");
        Assert.IsNull(ext.ResolveLine("c-sharp", renamed, "T:Calculator/M:Reset"));
    }

    [TestMethod]
    public void Extract_Python_ResolvesRelativeImportButNotLibrary()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"codeintel_{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "util.py"), "def helper():\n    pass\n");
        try
        {
            var src = "from .util import helper\nimport os\n";
            var outline = new CodeStructureExtractor().Extract("python", src, dir);

            var rel = outline.Imports.Single(i => i.Text.Contains(".util"));
            Assert.IsNotNull(rel.ResolvedPath);
            Assert.IsTrue(File.Exists(rel.ResolvedPath!));

            var lib = outline.Imports.Single(i => i.Text.Contains("import os"));
            Assert.IsNull(lib.ResolvedPath);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Markdown ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Build_EmitsDependencyLinksAndClickableMermaid()
    {
        var outline = new CodeOutline(
            Imports: [new ImportRef("import os", null), new ImportRef("from .util import x", @"C:\proj\util.py")],
            Types: [new OutlineType("Shape", 3, OutlineKind.Class, "T:Shape",
                       [new OutlineMember("draw", 5, OutlineKind.Method, "draw()", "T:Shape/M:draw")])],
            TopLevel: []);

        var md = CodeIntelligenceMarkdown.Build(@"C:\proj\shape.cs", outline);

        StringAssert.Contains(md, "## Dependencies");
        StringAssert.Contains(md, "- import os");                                   // library import: plain text
        StringAssert.Contains(md, "[from .util import x](file:///C:/proj/util.py)"); // local import: link
        StringAssert.Contains(md, "classDiagram");
        StringAssert.Contains(md, "class Shape {");
        StringAssert.Contains(md, "+draw() @@file:///C:/proj/shape.cs#ast=T%3AShape%2FM%3Adraw");
    }

    // ── Parser link token ───────────────────────────────────────────────────

    [TestMethod]
    public void MermaidClassParser_PeelsHrefToken_FromMember()
    {
        const string src = "classDiagram\n  class Shape {\n    +draw() @@nx:line#42\n    -color\n  }\n";
        var graph = new MermaidClassParser().Parse(src);
        var shape = graph.FindNode("Shape");

        Assert.IsNotNull(shape?.Class);
        var method = shape!.Class!.Methods.Single();
        Assert.AreEqual("+draw()", method.Text);
        Assert.AreEqual("nx:line#42", method.Href);

        var attr = shape.Class.Attributes.Single();
        Assert.AreEqual("-color", attr.Text);
        Assert.IsNull(attr.Href);
    }

    // ── Actions ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void ShowCodeAction_OpensCodeTab()
    {
        var shell = Substitute.For<IShellServices>();
        var action = new ShowCodeAction(shell);

        Assert.AreEqual("As Code", action.DisplayName);
        Assert.AreEqual("/text/code", action.ExperienceId);
        Assert.IsTrue(action.OpensViewer);

        action.PerformAction(@"C:\x.cs");
        shell.Received().OpenTab("Code", Arg.Is<Dictionary<string, string>>(d => d["path"] == @"C:\x.cs"));
    }

    [TestMethod]
    public void ShowTextAction_IsRenamedToAsText()
        => Assert.AreEqual("As Text", new ShowTextAction(Substitute.For<IShellServices>()).DisplayName);
}
