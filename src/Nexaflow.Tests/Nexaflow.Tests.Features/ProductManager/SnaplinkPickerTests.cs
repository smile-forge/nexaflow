using System.IO;
using Nexaflow.Features.ProductManager.ViewModels;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>The inline snaplink picker's file tree + markdown-heading targets (the code path needs the
/// tree-sitter runtime, so it's exercised manually, not here).</summary>
[TestClass]
public class SnaplinkPickerTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pmpick_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void Tree_ShowsMarkdownAndFolders_SkipsDotFolders()
    {
        File.WriteAllText(Path.Combine(_root, "a.md"), "# A");
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        Directory.CreateDirectory(Path.Combine(_root, "sub"));

        var picker = new SnaplinkPickerViewModel(_root);

        Assert.IsTrue(picker.Roots.Any(n => n is { Name: "a.md", IsDir: false }));
        Assert.IsTrue(picker.Roots.Any(n => n is { Name: "sub", IsDir: true }));
        Assert.IsFalse(picker.Roots.Any(n => n.Name == ".git"));
    }

    [TestMethod]
    public void Markdown_SelectFile_OffersWholeFileAndHeadingsWithTitlePaths()
    {
        File.WriteAllText(Path.Combine(_root, "spec.md"), "# Top\n\n## Sub A\n\nbody\n\n## Sub B\n");
        var picker = new SnaplinkPickerViewModel(_root);

        picker.SelectFile(picker.Roots.Single(n => n.Name == "spec.md"));

        Assert.AreEqual("(whole file)", picker.Targets[0].Label);
        var subA = picker.Targets.Single(t => t.Label == "Sub A");
        Assert.AreEqual("markdown", subA.Link.Type);
        Assert.AreEqual("spec.md", subA.Link.Doc);
        CollectionAssert.AreEqual(new[] { "Top", "Sub A" }, subA.Link.TitlePath!.ToArray());

        // a fresh link is built (status defaults to Should), independent of the target template
        picker.SelectedTarget = subA;
        var link = picker.BuildSelectedLink();
        Assert.IsNotNull(link);
        Assert.AreEqual(Nexaflow.Features.ProductManager.Model.Status.Should, link!.Status);
    }
}
