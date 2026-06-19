using Nexaflow.Features.WindowsFileSystem.Services;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
public class GlobMatcherTests
{
    [TestMethod]
    public void DoubleStar_MatchesZeroSubfolders()
        => Assert.IsTrue(GlobMatcher.IsMatch(@"C:\proj\a.md", @"C:\proj\**\*.md"));

    [TestMethod]
    public void DoubleStar_MatchesNestedSubfolders()
        => Assert.IsTrue(GlobMatcher.IsMatch(@"C:\proj\sub\deep\a.md", @"C:\proj\**\*.md"));

    [TestMethod]
    public void DoubleStar_RejectsOtherExtension()
        => Assert.IsFalse(GlobMatcher.IsMatch(@"C:\proj\sub\a.txt", @"C:\proj\**\*.md"));

    [TestMethod]
    public void DoubleStar_RejectsOutsideFolder()
        => Assert.IsFalse(GlobMatcher.IsMatch(@"C:\other\a.md", @"C:\proj\**\*.md"));

    [TestMethod]
    public void SingleStar_DoesNotCrossSeparator()
        => Assert.IsFalse(GlobMatcher.IsMatch(@"C:\proj\sub\a.md", @"C:\proj\*.md"));

    [TestMethod]
    public void Exact_MatchesOnlyThatPath()
    {
        Assert.IsTrue(GlobMatcher.IsMatch(@"C:\proj\a.md", @"C:\proj\a.md"));
        Assert.IsFalse(GlobMatcher.IsMatch(@"C:\proj\b.md", @"C:\proj\a.md"));
    }

    [TestMethod]
    public void Question_MatchesSingleChar()
    {
        Assert.IsTrue(GlobMatcher.IsMatch(@"C:\a1.md", @"C:\a?.md"));
        Assert.IsFalse(GlobMatcher.IsMatch(@"C:\a12.md", @"C:\a?.md"));
    }

    [TestMethod]
    public void Separators_AreInterchangeable()
        => Assert.IsTrue(GlobMatcher.IsMatch(@"C:\proj\sub\a.md", "C:/proj/**/*.md"));

    [TestMethod]
    public void Match_IsCaseInsensitive()
        => Assert.IsTrue(GlobMatcher.IsMatch(@"C:\Proj\A.MD", @"c:\proj\**\*.md"));
}
