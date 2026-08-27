using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Syntax;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Graph;

/// <summary>
/// The three things a structural edit promises not to make the caller think about: line endings, the
/// indentation of the place the code is going, and the escaping needed to get code through a command line.
/// </summary>
[TestClass]
[CoversNode("graph-edit")]
public class SourceTextTests
{
    [TestMethod]
    public void RoundTripsAFileExactly_WhicheverEndingsItUses()
    {
        foreach (var raw in new[]
                 {
                     "a\nb\nc\n", "a\nb\nc", "a\r\nb\r\nc\r\n", "a\r\nb\r\nc", "", "\n", "single line",
                 })
            Assert.AreEqual(raw, SourceText.Of(raw).Compose(), $"round trip changed {Quote(raw)}");
    }

    [TestMethod]
    public void MixedEndingsResolveToTheMajority()
    {
        Assert.AreEqual("\r\n", SourceText.Of("a\r\nb\r\nc\nd\r\n").Newline, "three CRLF against one LF");
        Assert.AreEqual("\n",   SourceText.Of("a\nb\nc\r\nd\n").Newline,     "three LF against one CRLF");
    }

    [TestMethod]
    public void ATrailingNewlineIsRememberedRatherThanBecomingABlankLine()
    {
        Assert.IsTrue(SourceText.Of("a\nb\n").FinalNewline);
        Assert.AreEqual(2, SourceText.Of("a\nb\n").Lines.Count, "a trailing newline is not a third line");
        Assert.IsFalse(SourceText.Of("a\nb").FinalNewline);
    }

    [TestMethod]
    public void ReindentStripsTheBlocksOwnIndentAndAppliesTheDestinations()
    {
        // Written flush-left…
        CollectionAssert.AreEqual(
            new[] { "    void M()", "    {", "        return;", "    }" },
            SourceText.Reindent(SourceText.BlockOf("void M()\n{\n    return;\n}"), "    ").ToArray());

        // …or lifted out of somewhere deeper, at whatever depth it had there.
        CollectionAssert.AreEqual(
            new[] { "  void M()", "  {", "      return;", "  }" },
            SourceText.Reindent(SourceText.BlockOf("        void M()\n        {\n            return;\n        }"), "  ").ToArray());
    }

    [TestMethod]
    public void ReindentLeavesBlankLinesBlank()
    {
        CollectionAssert.AreEqual(
            new[] { "    a", "", "    b" },
            SourceText.Reindent(SourceText.BlockOf("a\n\nb"), "    ").ToArray(),
            "a blank line must not become a line of trailing whitespace");
    }

    [TestMethod]
    public void UnescapeDecodesWhatAShellCannotCarry()
    {
        Assert.AreEqual("a\nb",     SourceText.Unescape(@"a\nb"));
        Assert.AreEqual("a\tb",     SourceText.Unescape(@"a\tb"));
        Assert.AreEqual("a\\b",     SourceText.Unescape(@"a\\b"));
        Assert.AreEqual("say \"x\"", SourceText.Unescape("say \\\"x\\\""));
        Assert.AreEqual("\u00e9",   SourceText.Unescape(@"\u00e9"));
    }

    [TestMethod]
    public void UnescapeLeavesAnythingItDoesNotOwnAlone()
    {
        // A regex or a Windows path in the payload must survive intact rather than being quietly eaten.
        Assert.AreEqual(@"\d+\s*",           SourceText.Unescape(@"\d+\s*"));
        Assert.AreEqual(@"C:\Users\aquen",   SourceText.Unescape(@"C:\Users\aquen"));
        Assert.AreEqual(@"no escapes here",  SourceText.Unescape(@"no escapes here"));
    }

    [TestMethod]
    public void IndentUnitIsMeasuredFromTheFile()
    {
        Assert.AreEqual("    ", SourceText.Of("class C\n{\n    void M() { }\n}\n").IndentUnit());
        Assert.AreEqual("  ",   SourceText.Of("class C\n{\n  void M() { }\n}\n").IndentUnit(),
            "a two-space file must not have four-space members appended to it");
        Assert.AreEqual("\t",   SourceText.Of("class C\n{\n\tvoid M() { }\n}\n").IndentUnit());
    }

    private static string Quote(string s) => "\"" + s.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
}
