using System.IO;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editor.Highlighting;

namespace Nexaflow.Tests.Visuals.Editor;

[TestClass]
[CoversNode("vtext-highlighting")]
public class HighlightingRegistryTests
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
            var r = HighlightingRegistry.Resolve(name);
            Assert.AreEqual(HighlightMode.TreeSitter, r.Mode, name);
            Assert.AreEqual(grammar, r.TreeSitterLanguage, name);
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
            var r = HighlightingRegistry.Resolve(name);
            Assert.AreEqual(HighlightMode.TreeSitter, r.Mode, name);
            Assert.AreEqual(grammar, r.TreeSitterLanguage, name);
        }
    }

    [TestMethod]
    public void PlainAndUnknown_ResolveToPlainText()
    {
        Assert.AreEqual(HighlightMode.PlainText, HighlightingRegistry.Resolve("notes.txt").Mode);
        Assert.AreEqual(HighlightMode.PlainText, HighlightingRegistry.Resolve("data.zzzunknown").Mode);
        Assert.AreEqual(HighlightMode.PlainText, HighlightingRegistry.Resolve("noextension").Mode);
    }

    [TestMethod]
    public void IsStructured_TracksResolution()
    {
        Assert.IsTrue(HighlightingRegistry.IsStructured("a.xml"));
        Assert.IsFalse(HighlightingRegistry.IsStructured("notes.txt"));
    }

    [TestMethod]
    public void Xaml_ResolvesToItsOwnGrammarId()
    {
        // XAML parses with the xml grammar (CodeHighlighter.NativeAlias) but keeps a distinct id, which is
        // what lets the structure extractor read x:Class / x:Name / x:Key / handlers out of the same tree.
        var r = HighlightingRegistry.Resolve("MainWindow.xaml");
        Assert.AreEqual(HighlightMode.TreeSitter, r.Mode);
        Assert.AreEqual("xaml", r.TreeSitterLanguage);
    }

    [TestMethod]
    public void Ruby_ResolvesToTreeSitter()
    {
        var r = HighlightingRegistry.Resolve("app.rb");
        Assert.AreEqual(HighlightMode.TreeSitter, r.Mode);
        Assert.AreEqual("ruby", r.TreeSitterLanguage);
    }

    [TestMethod]
    public void SystemsAndJvmLanguages_ResolveToTreeSitter()
    {
        foreach (var (name, grammar) in new[]
                 {
                     ("main.rs", "rust"), ("widget.cpp", "cpp"), ("widget.hpp", "cpp"), ("App.java", "java"),
                 })
        {
            var r = HighlightingRegistry.Resolve(name);
            Assert.AreEqual(HighlightMode.TreeSitter, r.Mode, name);
            Assert.AreEqual(grammar, r.TreeSitterLanguage, name);
        }
    }

    [TestMethod]
    public void ScriptsQueriesAndPatches_ResolveToTheirGrammars()
    {
        foreach (var (name, grammar) in new[]
                 {
                     ("build.ps1", "powershell"), ("Module.psm1", "powershell"), ("Module.psd1", "powershell"),
                     ("schema.sql", "sql"), ("change.diff", "diff"), ("fix.patch", "diff"),
                 })
        {
            var r = HighlightingRegistry.Resolve(name);
            Assert.AreEqual(HighlightMode.TreeSitter, r.Mode, name);
            Assert.AreEqual(grammar, r.TreeSitterLanguage, name);
        }
    }

    [TestMethod]
    public void AFormatNoGrammarReads_IsPlainText_SoSpellCheckCanAttach()
    {
        foreach (var name in new[] { "Module.vb", "page.aspx", "script.boo" })
        {
            Assert.AreEqual(HighlightMode.PlainText, HighlightingRegistry.Resolve(name).Mode, name);
            Assert.IsFalse(HighlightingRegistry.IsStructured(name), name);
        }
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
