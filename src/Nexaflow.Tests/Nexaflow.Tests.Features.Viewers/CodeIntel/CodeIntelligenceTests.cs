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
using Nexaflow.Tests.Fixtures;

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
    [CoversNode("syntax-outline")]
    [CoversNode("code-structure")]
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
    [CoversNode("code-structure")]
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
    [CoversNode("code-structure")]
    public void ResolveLine_ReturnsNull_WhenMemberRenamed()
    {
        var ext = new CodeStructureExtractor();
        var renamed = CSharp.Replace("Reset", "Clear");
        Assert.IsNull(ext.ResolveLine("c-sharp", renamed, "T:Calculator/M:Reset"));
    }

    private const string Inheritance = """
        public interface IBark { }
        public class Animal { protected int Age; }
        public class Dog : Animal, IBark
        {
            private string _name;
            public string Name => _name;
            public int Add(int a, int b) => a + b;
            protected void Speak() { }
        }
        """;

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void Extract_CSharp_CapturesVisibilityAndSignatures()
    {
        var outline = new CodeStructureExtractor().Extract("c-sharp", Inheritance);
        var dog = outline.Types.Single(t => t.Name == "Dog");

        var name = dog.Members.Single(m => m.Name == "_name");
        Assert.AreEqual(OutlineVisibility.Private, name.Visibility);
        Assert.AreEqual("_name : string", name.Signature);

        var prop = dog.Members.Single(m => m.Name == "Name");
        Assert.AreEqual(OutlineVisibility.Public, prop.Visibility);
        Assert.AreEqual("Name : string", prop.Signature);

        var add = dog.Members.Single(m => m.Name == "Add");
        Assert.AreEqual(OutlineVisibility.Public, add.Visibility);
        Assert.AreEqual("Add(int a, int b) int", add.Signature, "method shows parameters and return type");

        var speak = dog.Members.Single(m => m.Name == "Speak");
        Assert.AreEqual(OutlineVisibility.Protected, speak.Visibility);
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void Extract_CSharp_CapturesBaseClassAndInterface()
    {
        var outline = new CodeStructureExtractor().Extract("c-sharp", Inheritance);
        var dog = outline.Types.Single(t => t.Name == "Dog");

        Assert.IsTrue(dog.Bases.Any(b => b is { Name: "Animal", IsInterface: false }), "base class");
        Assert.IsTrue(dog.Bases.Any(b => b is { Name: "IBark", IsInterface: true }), "interface (I-prefix)");
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    [CoversNode("code-map-class-view")]
    public void Build_EmitsInheritanceEdgesAndVisibility()
    {
        var outline = new CodeStructureExtractor().Extract("c-sharp", Inheritance);
        var md = CodeIntelligenceMarkdown.Build(@"C:\proj\animals.cs", outline);

        StringAssert.Contains(md, "Animal <|-- Dog");   // class inheritance: solid
        StringAssert.Contains(md, "IBark <|.. Dog");    // interface realization: dashed
        StringAssert.Contains(md, "-_name : string");   // private field with type
        // Return type is emitted after the paren; the Mermaid class parser reflows it to "(…) : int" at render.
        StringAssert.Contains(md, "+Add(int a, int b) int");
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    [CoversNode("code-map-class-view")]
    public void Build_AbbreviatesLongMemberSignatures()
    {
        const string src = """
            public class Calc
            {
                public int Calculate(int alpha, string beta, bool gamma, double delta) => alpha;
            }
            """;
        var outline = new CodeStructureExtractor().Extract("c-sharp", src);
        var md = CodeIntelligenceMarkdown.Build(@"C:\proj\calc.cs", outline);

        StringAssert.Contains(md, "+Calculate(int…) int");          // params collapsed; name + return kept
        Assert.IsFalse(md.Contains("string beta"), "the full parameter list is not drawn in the box");
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    [CoversNode("code-map-deps")]
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

    // ── More languages (Java / PHP / C++ / Rust / TypeScript) ────────────────

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void Extract_Java_TypesMembersBasesVisibility()
    {
        const string src = """
            package com.example;
            import java.util.List;

            public interface Greeter { String greet(String name); }

            public class App extends Base implements Greeter {
                private int count;
                public App(int count) { this.count = count; }
                public String greet(String name) { return "Hi"; }
                private void helper() {}
                enum Status { ON, OFF }
            }
            """;
        var o = new CodeStructureExtractor().Extract("java", src);

        Assert.IsTrue(o.Imports.Any(i => i.Text.Contains("java.util.List")));
        var greeter = o.Types.Single(t => t.Name == "Greeter");
        Assert.AreEqual(OutlineKind.Interface, greeter.Kind);
        Assert.IsTrue(greeter.Members.Any(m => m is { Name: "greet", Visibility: OutlineVisibility.Public }), "interface members default public");

        var app = o.Types.Single(t => t.Name == "App");
        Assert.IsTrue(app.Bases.Any(b => b is { Name: "Base", IsInterface: false }));
        Assert.IsTrue(app.Bases.Any(b => b is { Name: "Greeter", IsInterface: true }));
        Assert.IsTrue(app.Members.Any(m => m is { Name: "count", Kind: OutlineKind.Field, Visibility: OutlineVisibility.Private }));
        Assert.IsTrue(app.Members.Any(m => m is { Name: "App", Kind: OutlineKind.Constructor }));
        Assert.IsTrue(app.Members.Any(m => m is { Name: "helper", Visibility: OutlineVisibility.Private }));
        Assert.IsTrue(o.Types.Any(t => t is { Name: "Status", Kind: OutlineKind.Enum }), "nested enum");
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void Extract_Php_TypesMembersBasesVisibility()
    {
        const string src = """
            <?php
            namespace App;
            use App\Models\User;

            interface Greeter { public function greet(string $name): string; }

            class Service implements Greeter {
                private int $count = 0;
                public const NAME = "svc";
                public function __construct(int $count) { $this->count = $count; }
                public function greet(string $name): string { return "Hi"; }
                private function helper(): void {}
            }

            function freeFn(int $a): int { return $a; }
            """;
        var o = new CodeStructureExtractor().Extract("php", src);

        Assert.IsTrue(o.Imports.Any(i => i.Text.Contains("User")));
        var svc = o.Types.Single(t => t.Name == "Service");
        Assert.AreEqual(OutlineKind.Class, svc.Kind);
        Assert.IsTrue(svc.Bases.Any(b => b is { Name: "Greeter", IsInterface: true }));
        Assert.IsTrue(svc.Members.Any(m => m is { Name: "count", Kind: OutlineKind.Property, Visibility: OutlineVisibility.Private }));
        Assert.IsTrue(svc.Members.Any(m => m is { Name: "NAME", Kind: OutlineKind.Field }));
        Assert.IsTrue(svc.Members.Any(m => m is { Name: "__construct", Kind: OutlineKind.Constructor }));
        Assert.IsTrue(svc.Members.Any(m => m is { Name: "helper", Visibility: OutlineVisibility.Private }));
        Assert.IsTrue(o.TopLevel.Any(m => m.Name == "freeFn"), "top-level function");
    }

    [TestMethod]
    public void Extract_Cpp_TypesMembersAndAccess()
    {
        const string src = """
            #include <vector>

            namespace app {
            class Widget : public Base {
            public:
              Widget(int id);
              void render() const;
              int getId() const { return id_; }
            private:
              int id_;
            };

            struct Point { int x; int y; };
            enum Color { Red, Green };
            void freeFunction(int a) {}
            }
            """;
        var o = new CodeStructureExtractor().Extract("cpp", src);

        Assert.IsTrue(o.Imports.Any(i => i.Text.Contains("vector")));
        var w = o.Types.Single(t => t.Name == "Widget");
        Assert.AreEqual(OutlineKind.Class, w.Kind);
        Assert.IsTrue(w.Bases.Any(b => b.Name == "Base"));
        Assert.IsTrue(w.Members.Any(m => m is { Name: "Widget", Kind: OutlineKind.Constructor, Visibility: OutlineVisibility.Public }));
        Assert.IsTrue(w.Members.Any(m => m is { Name: "render", Kind: OutlineKind.Method, Visibility: OutlineVisibility.Public }));
        Assert.IsTrue(w.Members.Any(m => m.Name == "getId"));
        Assert.IsTrue(w.Members.Any(m => m is { Name: "id_", Kind: OutlineKind.Field, Visibility: OutlineVisibility.Private }));
        Assert.IsTrue(o.Types.Any(t => t is { Name: "Point", Kind: OutlineKind.Struct }));
        Assert.IsTrue(o.Types.Any(t => t is { Name: "Color", Kind: OutlineKind.Enum }));
        Assert.IsTrue(o.TopLevel.Any(m => m.Name == "freeFunction"));
    }

    [TestMethod]
    public void Extract_Rust_StructEnumTraitImplMerge()
    {
        const string src = """
            use std::collections::HashMap;

            pub struct Point { pub x: i32, y: i32 }
            pub enum Shape { Circle, Square }
            pub trait Drawable { fn draw(&self); }

            impl Point {
                pub fn new(x: i32) -> Self { Point { x, y: 0 } }
                fn helper(&self) {}
            }

            impl Drawable for Point {
                fn draw(&self) {}
            }

            pub fn free_fn(a: i32) -> i32 { a }
            """;
        var o = new CodeStructureExtractor().Extract("rust", src);

        Assert.IsTrue(o.Imports.Any(i => i.Text.Contains("HashMap")));
        var point = o.Types.Single(t => t.Name == "Point");
        Assert.AreEqual(OutlineKind.Struct, point.Kind);
        Assert.IsTrue(point.Members.Any(m => m is { Name: "x", Kind: OutlineKind.Field, Visibility: OutlineVisibility.Public }));
        Assert.IsTrue(point.Members.Any(m => m is { Name: "y", Visibility: OutlineVisibility.Private }));
        Assert.IsTrue(point.Members.Any(m => m is { Name: "new", Visibility: OutlineVisibility.Public }), "method merged from impl block");
        Assert.IsTrue(point.Members.Any(m => m is { Name: "helper", Visibility: OutlineVisibility.Private }));
        Assert.IsTrue(point.Members.Any(m => m.Name == "draw"), "method merged from trait impl");
        Assert.IsTrue(point.Bases.Any(b => b is { Name: "Drawable", IsInterface: true }), "trait impl recorded as a base");
        Assert.IsTrue(o.Types.Any(t => t is { Name: "Shape", Kind: OutlineKind.Enum }));
        Assert.IsTrue(o.Types.Any(t => t is { Name: "Drawable", Kind: OutlineKind.Interface }));
        Assert.IsTrue(o.TopLevel.Any(m => m.Name == "free_fn"));
    }

    [TestMethod]
    public void Build_Cpp_PreservesAngleBracketIncludes()
    {
        var outline = new CodeStructureExtractor().Extract("cpp", "#include <string>\nclass C { int x; };\n");
        var md = CodeIntelligenceMarkdown.Build(@"C:\proj\c.cpp", outline);

        // The include is shown as a code span so <string> survives instead of being eaten as an HTML tag.
        StringAssert.Contains(md, "`#include <string>`");
    }

    [TestMethod]
    public void Extract_Razor_CodeBlockMembers()
    {
        const string src = """
            @page "/counter"
            <h1>Counter</h1>
            <button @onclick="Increment">Click</button>

            @code {
                private int count = 0;
                public int Current => count;
                private void Increment() { count++; }
            }
            """;
        var o = new CodeStructureExtractor().Extract("razor", src);

        var code = o.Types.Single(t => t.Name == "Code");   // the @code block surfaced as a component box
        Assert.IsTrue(code.Members.Any(m => m is { Name: "count", Kind: OutlineKind.Field }));
        Assert.IsTrue(code.Members.Any(m => m is { Name: "Increment", Kind: OutlineKind.Method }));
        Assert.IsTrue(code.Members.Any(m => m.Name == "Current"));
    }

    [TestMethod]
    public void Extract_TypeScript_InterfaceEnumClass()
    {
        const string src = """
            interface Greeter { greet(name: string): string; }
            enum Color { Red, Green }
            class App implements Greeter {
                private count: number = 0;
                greet(name: string): string { return "Hi"; }
            }
            """;
        var o = new CodeStructureExtractor().Extract("typescript", src);

        Assert.IsTrue(o.Types.Any(t => t is { Name: "Greeter", Kind: OutlineKind.Interface }));
        Assert.IsTrue(o.Types.Any(t => t is { Name: "Color", Kind: OutlineKind.Enum }));
        var app = o.Types.Single(t => t.Name == "App");
        Assert.IsTrue(app.Bases.Any(b => b.Name == "Greeter"));
        Assert.IsTrue(app.Members.Any(m => m.Name == "greet"));
        Assert.IsTrue(app.Members.Any(m => m is { Name: "count", Visibility: OutlineVisibility.Private }));
    }

    // ── Embedded languages (linked sub-graphs) ───────────────────────────────

    // <script> body starts on line 4 (1-based); the function is line 4.
    private const string HtmlWithScript =
        "<html>\n<body>\n<script>\nfunction greet(name) { return name; }\n</script>\n</body>\n</html>\n";

    [TestMethod]
    [CoversNode("code-embedded")]
    public void Extract_Html_EmbedsJavaScriptStructure_AtAbsoluteLine()
    {
        var outline = new CodeStructureExtractor().Extract("html", HtmlWithScript);

        var js = outline.Embedded.Single(e => e.Language == "javascript");
        Assert.AreEqual(3, js.HostLine, "the <script> tag is on line 3");

        var greet = js.Outline.TopLevel.Single(m => m.Name == "greet");
        Assert.AreEqual(4, greet.Line, "the embedded function's line is mapped into the host document");
        StringAssert.StartsWith(greet.AstPath, "E0:javascript/", "embedded paths are namespaced");
    }

    [TestMethod]
    public void ResolveLine_FindsEmbeddedMember()
    {
        var ext = new CodeStructureExtractor();
        var outline = ext.Extract("html", HtmlWithScript);
        var path = outline.Embedded.Single().Outline.TopLevel.Single(m => m.Name == "greet").AstPath;

        Assert.AreEqual(4, ext.ResolveLine("html", HtmlWithScript, path));
    }

    [TestMethod]
    [CoversNode("code-embedded")]
    public void Build_EmitsEmbeddedNamespaceCluster_WithMemberLink()
    {
        var outline = new CodeStructureExtractor().Extract("html", HtmlWithScript);
        var md = CodeIntelligenceMarkdown.Build(@"C:\proj\page.html", outline);

        StringAssert.Contains(md, "namespace javascript_L3 {");                  // the linked sub-graph cluster
        StringAssert.Contains(md, "greet");                                      // embedded member shown
        StringAssert.Contains(md, "#ast=E0%3Ajavascript%2FM%3Agreet");           // and it links to its line
    }

    // ── Markdown ────────────────────────────────────────────────────────────

    /// <summary>
    /// Languages with free functions (Python, JS, Go…) have code that belongs to no type, so the class
    /// diagram alone would hide most of the file. Those land in a "Members" list, each linking to its
    /// definition line — and a file that is all classes must not grow an empty Members heading.
    /// </summary>
    [TestMethod]
    [CoversNode("code-map-members")]
    public void Build_ListsFreeFunctions_UnderMembers_AndOmitsTheHeadingWhenThereAreNone()
    {
        var freeFunctions = new CodeOutline(
            Imports: [],
            Types: [],
            TopLevel: [new OutlineMember("helper", 7, OutlineKind.Method, "helper()", "M:helper")]);

        var md = CodeIntelligenceMarkdown.Build(@"C:\proj\util.py", freeFunctions);

        StringAssert.Contains(md, "## Members");
        StringAssert.Contains(md, "helper");
        StringAssert.Contains(md, "#ast=M%3Ahelper", "each free function links to its definition");

        var classesOnly = new CodeOutline(
            Imports: [],
            Types: [new OutlineType("Shape", 1, OutlineKind.Class, "T:Shape", [])],
            TopLevel: []);

        Assert.IsFalse(CodeIntelligenceMarkdown.Build(@"C:\proj\shape.cs", classesOnly).Contains("## Members"),
                       "no free functions ⇒ no Members section");
    }

    [TestMethod]
    [CoversNode("code-map-deps")]
    public void Build_EmitsDependencyLinksAndClickableMermaid()
    {
        var outline = new CodeOutline(
            Imports: [new ImportRef("import os", null), new ImportRef("from .util import x", @"C:\proj\util.py")],
            Types: [new OutlineType("Shape", 3, OutlineKind.Class, "T:Shape",
                       [new OutlineMember("draw", 5, OutlineKind.Method, "draw()", "T:Shape/M:draw")])],
            TopLevel: []);

        var md = CodeIntelligenceMarkdown.Build(@"C:\proj\shape.cs", outline);

        StringAssert.Contains(md, "## Dependencies");
        StringAssert.Contains(md, "- `import os`");                                  // library import: code span
        StringAssert.Contains(md, "[from .util import x](file:///C:/proj/util.py)"); // local import: link
        StringAssert.Contains(md, "classDiagram");
        StringAssert.Contains(md, "direction LR");   // stacks a file's classes into a vertical column
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
    [CoversNode("code-open-action")]
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

    // ── Declaration spans ─────────────────────────────────────────────────────
    //
    // The extractor used to keep a node's StartPosition and throw its EndPosition away, so every consumer
    // re-derived where a declaration stopped by counting braces — a small C# parser beside a real one, wrong
    // on raw string literals and on property initialisers. Both ends now come from the parse.

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void Outline_RecordsWhereEachDeclarationEnds()
    {
        var outline = new CodeStructureExtractor().Extract("c-sharp", CSharp);
        var calc = outline.Types.Single(t => t.Name == "Calculator");

        Assert.IsTrue(calc.EndLine > calc.Line, "a class spans more than its declaration line");
        foreach (var m in calc.Members)
            Assert.IsTrue(m.EndLine >= m.Line, $"{m.Name} ends before it starts");

        var reset = calc.Members.Single(m => m.Name == "Reset");
        Assert.AreEqual(reset.Line, reset.EndLine, "a one-line method starts and ends on the same line");
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void Outline_EndLineCoversAMultiLineMember()
    {
        const string src = """
            public class C
            {
                public void M()
                {
                    var x = 1;
                }
            }
            """;

        var m = new CodeStructureExtractor().Extract("c-sharp", src).Types.Single().Members.Single();

        Assert.AreEqual(3, m.Line);
        Assert.AreEqual(6, m.EndLine, "the member ends at its closing brace, not at its signature");
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void Outline_EndLineSpansAPropertyInitialiser()
    {
        // The accessor list's '}' is not the end of the declaration. A brace counter stops there; the parser
        // does not.
        const string src = """
            public class C
            {
                public static Dictionary<string, string> Map { get; } = new Dictionary<string, string>
                {
                    ["a"] = "one",
                };
            }
            """;

        var m = new CodeStructureExtractor().Extract("c-sharp", src).Types.Single().Members.Single();

        Assert.AreEqual(3, m.Line);
        Assert.AreEqual(6, m.EndLine, "the property ends at the initialiser's semicolon");
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void Outline_EndLineSpansARawStringLiteral()
    {
        // Deliberately an ODD number of quotes in the body: that is the case a naive in-string flag gets
        // wrong, because three quotes toggle it on-off-on. (Four-quote fence here so the fixture can contain
        // a three-quote one.)
        const string src = """"
            public class C
            {
                public string M()
                {
                    return """
                        he said "hello
                        }
                        """;
                }
            }
            """";

        var m = new CodeStructureExtractor().Extract("c-sharp", src).Types.Single().Members.Single();

        Assert.AreEqual(3, m.Line);
        Assert.AreEqual(9, m.EndLine, "the brace inside the raw literal is text, not the end of the method");
    }

    [TestMethod]
    [CoversNode("code-structure")]
    public void ResolveSpan_ReturnsBothEnds_AndTracksInsertedLines()
    {
        var ext = new CodeStructureExtractor();

        var span = ext.ResolveSpan("c-sharp", CSharp, "T:Calculator/M:Reset");
        Assert.IsNotNull(span);
        Assert.AreEqual(ext.ResolveLine("c-sharp", CSharp, "T:Calculator/M:Reset"), span!.Value.Line,
            "ResolveLine is the same answer, narrowed");

        var shifted = ext.ResolveSpan("c-sharp", "\n\n" + CSharp, "T:Calculator/M:Reset");
        Assert.AreEqual(span.Value.Line + 2, shifted!.Value.Line);
        Assert.AreEqual(span.Value.EndLine + 2, shifted.Value.EndLine, "both ends move with the declaration");
    }
}
