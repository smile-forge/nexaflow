using System.IO;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Syntax;

namespace Nexaflow.Tests.Visuals.Editor;

[TestClass]
[CoversNode("vtext-highlighting")]
public class TreeSitterLanguagesTests
{
    [TestMethod]
    public void XmlFamily_ResolvesToTreeSitter()
    {
        // One parser serves the whole family's highlighting, folding and structure outline — the configuration,
        // schema, package and resource files as much as .xml, and project files, which are XML to an editor too.
        foreach (var (name, grammar) in new[]
                 {
                     ("a.xml", "xml"), ("b.xsl", "xml"), ("c.xslt", "xml"), ("app.config", "xml"), ("s.xsd", "xml"),
                     ("p.nuspec", "xml"), ("Strings.resx", "xml"), ("x.ps1xml", "xml"), ("App.csproj", "xml"),
                 })
        {
            Assert.AreEqual(grammar, TreeSitterLanguages.ForEdit(name), name);
        }
    }

    [TestMethod]
    public void MarkupAndTemplating_ResolveToTreeSitter()
    {
        // html/css/erb/razor/php now parse with tree-sitter (so embedded languages can be injected).
        foreach (var (name, grammar) in new[]
                 {
                     ("page.html", "html"), ("styles.css", "css"), ("view.erb", "embedded-template"),
                     ("Page.razor", "razor"), ("Page.cshtml", "razor"), ("index.php", "php"),
                     ("home.jinja", "jinja"), ("home.j2", "jinja"),
                 })
        {
            Assert.AreEqual(grammar, TreeSitterLanguages.ForEdit(name), name);
        }
    }

    [TestMethod]
    public void PlainAndUnknown_ResolveToNoGrammar()
    {
        Assert.IsNull(TreeSitterLanguages.ForEdit("notes.txt"));
        Assert.IsNull(TreeSitterLanguages.ForEdit("data.zzzunknown"));
        Assert.IsNull(TreeSitterLanguages.ForEdit("noextension"));
    }

    [TestMethod]
    public void Xaml_ResolvesToItsOwnGrammarId()
    {
        // XAML parses with the xml grammar (CodeHighlighter.NativeAlias) but keeps a distinct id, which is
        // what lets the structure extractor read x:Class / x:Name / x:Key / handlers out of the same tree.
        Assert.AreEqual("xaml", TreeSitterLanguages.ForEdit("MainWindow.xaml"));
    }

    [TestMethod]
    public void Ruby_ResolvesToTreeSitter() => Assert.AreEqual("ruby", TreeSitterLanguages.ForEdit("app.rb"));

    [TestMethod]
    public void SystemsAndJvmLanguages_ResolveToTreeSitter()
    {
        foreach (var (name, grammar) in new[]
                 {
                     ("main.rs", "rust"), ("widget.cpp", "cpp"), ("widget.hpp", "cpp"), ("App.java", "java"),
                 })
        {
            Assert.AreEqual(grammar, TreeSitterLanguages.ForEdit(name), name);
        }
    }

    [TestMethod]
    public void ScriptsQueriesAndPatches_ResolveToTheirGrammars()
    {
        foreach (var (name, grammar) in new[]
                 {
                     ("build.ps1", "powershell"), ("Module.psm1", "powershell"), ("Module.psd1", "powershell"),
                     ("schema.sql", "sql"), ("change.diff", "diff"), ("fix.patch", "diff"),
                     ("paper.tex", "tex-file"), ("style.sty", "tex-file"), ("article.cls", "tex-file"),
                 })
        {
            Assert.AreEqual(grammar, TreeSitterLanguages.ForEdit(name), name);
        }
    }

    [TestMethod]
    public void AFormatNoGrammarReads_HasNoGrammar_SoTheEditorShowsItAsPlainText()
    {
        foreach (var name in new[] { "Module.vb", "page.aspx", "script.boo" })
            Assert.IsNull(TreeSitterLanguages.ForEdit(name), name);
    }

    [TestMethod]
    public void CodeSampleSet_Materializes()
    {
        var files = TestSampleData.Files("code");
        Assert.IsTrue(files.Count >= 8, "expected one sample per supported language/format");
        foreach (var path in files)
            Assert.IsTrue(File.Exists(path), $"missing generated sample: {path}");
    }
}
