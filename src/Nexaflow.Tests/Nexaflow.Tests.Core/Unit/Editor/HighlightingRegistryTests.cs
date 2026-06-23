using System.IO;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editor.Highlighting;

namespace Nexaflow.Tests.Core.Unit.Editor;

[TestClass]
public class HighlightingRegistryTests
{
    [TestMethod]
    public void MarkupFormats_ResolveToXshd()
    {
        foreach (var name in new[] { "a.xml", "page.html", "styles.css" })
        {
            var r = HighlightingRegistry.Resolve(name);
            Assert.AreEqual(HighlightMode.Xshd, r.Mode, name);
            Assert.IsNotNull(r.Definition, name);
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
    public void Xaml_ResolvesToXmlXshd()
    {
        var r = HighlightingRegistry.Resolve("MainWindow.xaml");
        Assert.AreEqual(HighlightMode.Xshd, r.Mode);
        Assert.IsNotNull(r.Definition);
    }

    [TestMethod]
    public void Ruby_ResolvesToTreeSitter()
    {
        var r = HighlightingRegistry.Resolve("app.rb");
        Assert.AreEqual(HighlightMode.TreeSitter, r.Mode);
        Assert.AreEqual("ruby", r.TreeSitterLanguage);
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
