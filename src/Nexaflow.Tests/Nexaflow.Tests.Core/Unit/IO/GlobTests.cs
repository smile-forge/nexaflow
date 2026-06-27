using Nexaflow.IO.Common;

namespace Nexaflow.Tests.Core.Unit.IO;

[TestClass]
public class GlobTests
{
    [TestMethod]
    public void DoubleStar_MatchesZeroSubfolders()
        => Assert.IsTrue(Glob.IsMatch(@"C:\proj\a.md", @"C:\proj\**\*.md"));

    [TestMethod]
    public void DoubleStar_MatchesNestedSubfolders()
        => Assert.IsTrue(Glob.IsMatch(@"C:\proj\sub\deep\a.md", @"C:\proj\**\*.md"));

    [TestMethod]
    public void DoubleStar_RejectsOtherExtension()
        => Assert.IsFalse(Glob.IsMatch(@"C:\proj\sub\a.txt", @"C:\proj\**\*.md"));

    [TestMethod]
    public void DoubleStar_RejectsOutsideFolder()
        => Assert.IsFalse(Glob.IsMatch(@"C:\other\a.md", @"C:\proj\**\*.md"));

    [TestMethod]
    public void SingleStar_DoesNotCrossSeparator()
        => Assert.IsFalse(Glob.IsMatch(@"C:\proj\sub\a.md", @"C:\proj\*.md"));

    [TestMethod]
    public void Exact_MatchesOnlyThatPath()
    {
        Assert.IsTrue(Glob.IsMatch(@"C:\proj\a.md", @"C:\proj\a.md"));
        Assert.IsFalse(Glob.IsMatch(@"C:\proj\b.md", @"C:\proj\a.md"));
    }

    [TestMethod]
    public void Question_MatchesSingleChar()
    {
        Assert.IsTrue(Glob.IsMatch(@"C:\a1.md", @"C:\a?.md"));
        Assert.IsFalse(Glob.IsMatch(@"C:\a12.md", @"C:\a?.md"));
    }

    [TestMethod]
    public void Separators_AreInterchangeable()
        => Assert.IsTrue(Glob.IsMatch(@"C:\proj\sub\a.md", "C:/proj/**/*.md"));

    [TestMethod]
    public void Match_IsCaseInsensitive()
        => Assert.IsTrue(Glob.IsMatch(@"C:\Proj\A.MD", @"c:\proj\**\*.md"));

    // ── Bare file-name patterns (the Tabular templates use case) ──────────────

    [TestMethod]
    public void FileName_StarExtension_Matches()
    {
        Assert.IsTrue(Glob.IsMatch("a.csv", "*.csv"));
        Assert.IsFalse(Glob.IsMatch("a.tsv", "*.csv"));
    }

    [TestMethod]
    public void FileName_PrefixPattern_Matches()
    {
        Assert.IsTrue(Glob.IsMatch("sales-q1.csv", "sales-*.csv"));
        Assert.IsFalse(Glob.IsMatch("orders-q1.csv", "sales-*.csv"));
    }

    // ── ContainsGlobChars / ToSqlLike ────────────────────────────────────────

    [TestMethod]
    public void ContainsGlobChars_DetectsWildcards()
    {
        Assert.IsTrue(Glob.ContainsGlobChars("a*.csv"));
        Assert.IsTrue(Glob.ContainsGlobChars("a?b"));
        Assert.IsFalse(Glob.ContainsGlobChars("plain.txt"));
    }

    [TestMethod]
    public void ToSqlLike_MapsWildcardsAndEscapesLiterals()
    {
        Assert.AreEqual("%.csv", Glob.ToSqlLike("*.csv"));
        Assert.AreEqual("a_b",   Glob.ToSqlLike("a?b"));
        // Existing % and _ become bracket-escaped literals; single quotes doubled.
        Assert.AreEqual("[%]50[_]o''", Glob.ToSqlLike("%50_o'"));
    }
}
