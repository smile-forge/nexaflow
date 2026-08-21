using System.IO;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editor.Highlighting;

namespace Nexaflow.Tests.Core.Unit.Editor;

[TestClass]
[CoversNode("vtext-highlighting")]
public class HighlightingRegistryTests
{
    [TestMethod]
    public void XmlFamily_ResolvesToTreeSitter()
    {
        // The xml grammar is built from external/tree-sitter-xml, so the whole family left the .xshd engine
        // behind: one parser now serves highlighting, folding and the structure outline.
        foreach (var (name, grammar) in new[] { ("a.xml", "xml"), ("b.xsl", "xml"), ("c.xslt", "xml") })
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
    public void CodeSampleSet_Materializes()
    {
        var files = TestSampleData.Files("code");
        Assert.IsTrue(files.Count >= 8, "expected one sample per supported language/format");
        foreach (var path in files)
            Assert.IsTrue(File.Exists(path), $"missing generated sample: {path}");
    }
}
