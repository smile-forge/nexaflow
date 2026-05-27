using Nexaflow.Features.WindowsFileSystem.FileActions;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
public class ActionTemplateExpanderTests
{
    private static readonly string[] OnePath  = ["C:\\folder\\photo.jpg"];
    private static readonly string[] TwoPaths = ["C:\\a\\one.txt", "C:\\b\\two.log"];

    [TestMethod]
    public void Expand_File_ReturnsFilenameWithExtension()
    {
        Assert.AreEqual("photo.jpg",
            ActionTemplateExpander.Expand("#file", OnePath));
    }

    [TestMethod]
    public void Expand_FileNoExt_DropsExtension()
    {
        Assert.AreEqual("photo",
            ActionTemplateExpander.Expand("#filenoext", OnePath));
    }

    [TestMethod]
    public void Expand_FilePath_ReturnsFullPath()
    {
        Assert.AreEqual("C:\\folder\\photo.jpg",
            ActionTemplateExpander.Expand("#filepath", OnePath));
    }

    [TestMethod]
    public void Expand_PathOnly_ReturnsDirectory()
    {
        Assert.AreEqual("C:\\folder",
            ActionTemplateExpander.Expand("#pathonly", OnePath));
    }

    [TestMethod]
    public void Expand_IndexedToken_PicksNthFile()
    {
        Assert.AreEqual("two.log",
            ActionTemplateExpander.Expand("#file[1]", TwoPaths));
    }

    [TestMethod]
    public void Expand_PathWithSpace_GetsQuoted()
    {
        var result = ActionTemplateExpander.Expand("#filepath",
            new[] { "C:\\a folder\\note.txt" });
        Assert.AreEqual("\"C:\\a folder\\note.txt\"", result);
    }

    [TestMethod]
    public void Expand_PathWithSpaceAlreadyQuoted_NoDoubleQuote()
    {
        var result = ActionTemplateExpander.Expand("\"#filepath\"",
            new[] { "C:\\a folder\\note.txt" });
        Assert.AreEqual("\"C:\\a folder\\note.txt\"", result);
    }

    [TestMethod]
    public void Expand_EnvVar_Expanded()
    {
        Environment.SetEnvironmentVariable("NEXATEST_TOKEN", "hello");
        try
        {
            Assert.AreEqual("hello-photo.jpg",
                ActionTemplateExpander.Expand("%NEXATEST_TOKEN%-#file", OnePath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXATEST_TOKEN", null);
        }
    }

    [TestMethod]
    public void Expand_TokenIsCaseInsensitive()
    {
        Assert.AreEqual("photo.jpg",
            ActionTemplateExpander.Expand("#FILE", OnePath));
    }

    [TestMethod]
    public void Expand_IndexOutOfRange_LeavesTokenIntact()
    {
        Assert.AreEqual("#file[5]",
            ActionTemplateExpander.Expand("#file[5]", OnePath));
    }
}

// Tests for extension normalization logic used by ExternalAppRegistry.MatchesAll.
// The registry itself is a singleton with private ctor; we test the normalization
// rule in isolation via a local helper that mirrors it exactly.
[TestClass]
public class ExternalAppExtensionNormTests
{
    // Mirror of ExternalAppRegistry.MatchesAll — kept in sync manually.
    private static bool Matches(string defExt, string fileExt)
    {
        if (string.IsNullOrEmpty(defExt) || defExt == "*" || defExt == "*.*") return true;
        var want = defExt.StartsWith("*.") ? defExt[2..].ToLowerInvariant()
                 : defExt.TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(want) || want == "*") return true;
        return string.Equals(fileExt, want, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod] public void GlobForm_Matches()         => Assert.IsTrue(Matches("*.tft", "tft"));
    [TestMethod] public void DotForm_Matches()          => Assert.IsTrue(Matches(".tft",  "tft"));
    [TestMethod] public void BareForm_Matches()         => Assert.IsTrue(Matches("tft",   "tft"));
    [TestMethod] public void StarStar_MatchesAny()      => Assert.IsTrue(Matches("*.*",   "tft"));
    [TestMethod] public void Star_MatchesAny()          => Assert.IsTrue(Matches("*",     "tft"));
    [TestMethod] public void Empty_MatchesAny()         => Assert.IsTrue(Matches("",      "tft"));
    [TestMethod] public void WrongExtension_NoMatch()   => Assert.IsFalse(Matches("*.txt", "tft"));
    [TestMethod] public void CaseInsensitive_Matches()  => Assert.IsTrue(Matches("*.TFT", "tft"));
}
