using Nexaflow.Tests.Fixtures;
using System.IO;

namespace Nexaflow.Tests.Core.Unit.Markdown;

/// <summary>
/// Verifies the git-ignored sample dataset materialises the expected markdown fixtures.
/// WPF-free; exercises generation + on-disk presence only (rendering is covered separately).
/// </summary>
[TestClass]
public class MarkdownSampleFilesTests
{
    [TestMethod]
    public void Dataset_MaterialisesAllMarkdownSamples()
    {
        var files = TestSampleData.Files("markdown");
        Assert.AreEqual(7, files.Count);

        foreach (var path in files)
        {
            Assert.IsTrue(File.Exists(path), $"missing sample: {path}");
            Assert.IsTrue(File.ReadAllText(path).Contains("```mermaid"), $"no mermaid fence in {path}");
        }
    }

    [TestMethod]
    public void Dataset_RootIsTheCacheDirectory()
    {
        Assert.IsTrue(TestSampleData.Root.EndsWith(TestSampleData.DirName, StringComparison.Ordinal));
    }
}
