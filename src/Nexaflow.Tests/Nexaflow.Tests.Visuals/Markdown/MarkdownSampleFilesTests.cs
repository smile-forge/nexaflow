using Nexaflow.Tests.Fixtures;
using System.IO;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Verifies the git-ignored sample dataset materialises the expected markdown fixtures.
/// WPF-free; exercises generation + on-disk presence only (rendering is covered separately).
/// </summary>
[TestClass]
[NoCoverage("markdown sample corpus")]
public class MarkdownSampleFilesTests
{
    [TestMethod]
    public void Dataset_MaterialisesAllMarkdownSamples()
    {
        var files = TestSampleData.Files("markdown");
        Assert.AreEqual(33, files.Count);   // 24 mermaid + extensions + 4 latex-math + 2 music + qr + barcode

        foreach (var path in files)
        {
            Assert.IsTrue(File.Exists(path), $"missing sample: {path}");
            // Diagram docs are named mermaid-*; only those must carry a mermaid fence.
            if (Path.GetFileName(path).StartsWith("mermaid-", StringComparison.Ordinal))
                Assert.IsTrue(File.ReadAllText(path).Contains("```mermaid"), $"no mermaid fence in {path}");
        }
    }

    [TestMethod]
    public void Dataset_RootIsTheCacheDirectory()
    {
        Assert.IsTrue(TestSampleData.Root.EndsWith(TestSampleData.DirName, StringComparison.Ordinal));
    }
}
