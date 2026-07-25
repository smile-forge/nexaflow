using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editor.Highlighting;

namespace Nexaflow.Tests.Core.Unit.Editor;

/// <summary>
/// The role palette — the one map from a syntax role to a <c>TextSwatch.*</c> theme token, shared by both
/// highlighting engines. It is what lets a theme art-direct code colours: every colour the editor paints
/// must arrive through a token here, never as a literal. Two entry points, one vocabulary: tree-sitter
/// capture names, and AvalonEdit's .xshd named colours (whose names differ per shipped definition, so that
/// side is a substring heuristic worth pinning down).
/// </summary>
[TestClass]
public class SyntaxTokenMapTests
{
    // ── Tree-sitter captures ──────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("code-role-palette")]
    public void EveryCapture_ResolvesToATextSwatchToken()
    {
        string[] captures = ["comment", "string", "number", "keyword", "type",
                             "constant", "function", "parameter", "variable", "tag", "attribute"];

        foreach (var capture in captures)
        {
            var key = SyntaxTokenMap.ResourceKey(capture);
            Assert.IsNotNull(key, $"capture '{capture}' has no colour role");
            StringAssert.StartsWith(key, "TextSwatch.", $"capture '{capture}' must resolve to a theme token");
        }
    }

    [TestMethod]
    [CoversNode("code-role-palette")]
    public void VariableAndParameter_ShareOneRole()
        => Assert.AreEqual(SyntaxTokenMap.ResourceKey("parameter"), SyntaxTokenMap.ResourceKey("variable"));

    [TestMethod]
    [CoversNode("code-role-palette")]
    public void UnknownCapture_HasNoRole_SoItPaintsAsPlainText()
        => Assert.IsNull(SyntaxTokenMap.ResourceKey("no-such-capture"));

    // ── AvalonEdit .xshd named colours ────────────────────────────────────────

    [TestMethod]
    [CoversNode("code-xshd")]
    public void XshdColourNames_MapOntoTheSameRoles()
    {
        (string Name, string Token)[] cases =
        [
            ("Comment",       "TextSwatch.Comment"),
            ("XmlString",     "TextSwatch.String"),
            ("Char",          "TextSwatch.String"),
            ("Keywords",      "TextSwatch.Keyword"),
            ("DigitNumber",   "TextSwatch.Number"),
            ("ClassName",     "TextSwatch.Type"),
            ("AttributeName", "TextSwatch.Attribute"),
            ("XmlTag",        "TextSwatch.Tag"),
            ("Value",         "TextSwatch.String"),
            ("Punctuation",   "TextSwatch.Operator"),
        ];

        foreach (var (name, token) in cases)
            Assert.AreEqual(token, SyntaxTokenMap.XshdResourceKey(name), $"xshd colour '{name}'");
    }

    /// <summary>
    /// The .xshd side is a first-match-wins substring scan, so a compound colour name resolves to whichever
    /// role appears earliest in the chain — "TypeKeywords" colours as a keyword, not a type. That's the
    /// intended reading (it *is* a keyword list), and it is the kind of behaviour that silently changes if
    /// the chain is ever reordered.
    /// </summary>
    [TestMethod]
    [CoversNode("code-xshd")]
    public void CompoundColourNames_ResolveByFirstMatchWins()
    {
        Assert.AreEqual("TextSwatch.Keyword", SyntaxTokenMap.XshdResourceKey("TypeKeywords"));
        Assert.AreEqual("TextSwatch.Attribute", SyntaxTokenMap.XshdResourceKey("AttributeValue"));
    }

    [TestMethod]
    [CoversNode("code-xshd")]
    public void XshdColourNames_AreMatchedCaseInsensitively()
        => Assert.AreEqual(SyntaxTokenMap.XshdResourceKey("Comment"), SyntaxTokenMap.XshdResourceKey("COMMENT"));

    [TestMethod]
    [CoversNode("code-xshd")]
    public void UnrecognisedXshdColour_IsLeftAlone()
        => Assert.IsNull(SyntaxTokenMap.XshdResourceKey("Vendor.Thing"),
                         "a colour with no role keeps the definition's own foreground");
}
