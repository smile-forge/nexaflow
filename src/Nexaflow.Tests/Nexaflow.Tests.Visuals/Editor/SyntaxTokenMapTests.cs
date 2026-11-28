using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editor.Highlighting;

namespace Nexaflow.Tests.Visuals.Editor;

/// <summary>
/// The role palette — the one map from a tree-sitter capture to a <c>TextSwatch.*</c> theme token. It is what
/// lets a theme art-direct code colours: every colour code is painted in must arrive through a token here,
/// never as a literal.
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
                             "constant", "function", "parameter", "variable", "tag", "attribute",
                             "operator", "whitespace", "inserted", "deleted"];

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

}
