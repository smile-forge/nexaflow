using System;
using System.Linq;
using Nexaflow.Syntax;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.Editor;

/// <summary>
/// Colouring inside a XAML attribute value. The <c>xml</c> grammar sees <c>AttValue</c> as one opaque token,
/// so <c>Width="420"</c> and <c>Style="{StaticResource Foo}"</c> would paint identically — these assert the
/// structure a reader actually scans for is picked out: the extension name, its arguments, the argument names,
/// and the namespace prefixes.
/// </summary>
[TestClass]
[CoversNode("parser-xaml")]
public class XamlValueHighlightingTests
{
    /// <summary>
    /// The role each character ends up with, folded the way <c>TreeSitterColorizer</c> folds it: spans are
    /// applied in order and each overwrites, so the last span covering a character wins.
    /// </summary>
    private static string[] Roles(string xaml)
    {
        using var hl = CodeHighlighter.TryCreate("xaml");
        Assert.IsNotNull(hl);

        var roles = new string[xaml.Length];
        foreach (var s in hl.Highlight(xaml))
            for (var i = s.Start; i < s.Start + s.Length && i < roles.Length; i++)
                roles[i] = s.Capture;
        return roles;
    }

    /// <summary>The single role covering <paramref name="fragment"/>, or a description of the split if it is not one.</summary>
    private static string RoleOf(string xaml, string fragment, int from = 0)
    {
        var at = xaml.IndexOf(fragment, from, StringComparison.Ordinal);
        Assert.IsTrue(at >= 0, $"'{fragment}' is not in the sample");

        var roles = Roles(xaml);
        var distinct = Enumerable.Range(at, fragment.Length).Select(i => roles[i] ?? "·none·").Distinct().ToList();
        return distinct.Count == 1 ? distinct[0] : "mixed:" + string.Join("+", distinct);
    }

    [TestMethod]
    public void APlainLiteralValue_StaysAString()
    {
        // Nothing to pick apart, so nothing should repaint it.
        const string Xaml = """<Border Width="420" HorizontalAlignment="Center"/>""";
        Assert.AreEqual("string", RoleOf(Xaml, "\"420\""));
        Assert.AreEqual("string", RoleOf(Xaml, "\"Center\""));
    }

    [TestMethod]
    public void AMarkupExtension_SeparatesItsBracesNameAndArgument()
    {
        const string Xaml = """<Border Style="{StaticResource PopupBorder}"/>""";

        Assert.AreEqual("operator", RoleOf(Xaml, "{"));
        Assert.AreEqual("keyword", RoleOf(Xaml, "StaticResource"), "the extension name is the part that says what this value is");
        Assert.AreEqual("variable", RoleOf(Xaml, "PopupBorder"));
        Assert.AreEqual("operator", RoleOf(Xaml, "}"));
        Assert.AreEqual("string", RoleOf(Xaml, "\"", from: Xaml.IndexOf('=')), "the quotes still belong to the value");
    }

    [TestMethod]
    public void ANamespacePrefix_ReadsAsSeparateFromTheNameItQualifies()
    {
        const string Xaml = """<Button x:Name="Go" Tag="{x:Type vmo:PromptOverlay}"/>""";

        Assert.AreEqual("type", RoleOf(Xaml, "x:"), "on an attribute name");
        Assert.AreEqual("attribute", RoleOf(Xaml, "Name"));
        Assert.AreEqual("keyword", RoleOf(Xaml, "Type"), "inside an extension the local name is the extension");
        Assert.AreEqual("type", RoleOf(Xaml, "vmo:"), "and on an argument");
        Assert.AreEqual("variable", RoleOf(Xaml, "PromptOverlay"));
    }

    [TestMethod]
    public void AnArgumentName_ReadsDifferentlyFromItsValue()
    {
        const string Xaml = """<TextBox Text="{Binding Value, Mode=TwoWay}"/>""";

        Assert.AreEqual("keyword", RoleOf(Xaml, "Binding"));
        Assert.AreEqual("variable", RoleOf(Xaml, "Value"), "a positional argument");
        Assert.AreEqual("attribute", RoleOf(Xaml, "Mode"), "a named one");
        Assert.AreEqual("variable", RoleOf(Xaml, "TwoWay"));
    }

    [TestMethod]
    public void ANestedExtension_IsScoredAsOne_NotAsItsParentsArgument()
    {
        // RelativeSource appears twice and means two different things — an argument name outside, the
        // extension itself inside. Painting both the same is exactly what makes a long binding unreadable.
        const string Xaml =
            """<TextBox Text="{Binding X, RelativeSource={RelativeSource AncestorType=Window}}"/>""";

        var argumentName = Xaml.IndexOf("RelativeSource", StringComparison.Ordinal);
        var nestedName = Xaml.IndexOf("RelativeSource", argumentName + 1, StringComparison.Ordinal);

        Assert.AreEqual("attribute", RoleOf(Xaml, "RelativeSource", from: argumentName));
        Assert.AreEqual("keyword", RoleOf(Xaml, "RelativeSource", from: nestedName));
        Assert.AreEqual("attribute", RoleOf(Xaml, "AncestorType"));
        Assert.AreEqual("variable", RoleOf(Xaml, "Window"));
    }

    [TestMethod]
    public void TheLiteralBraceEscape_IsLeftAlone()
    {
        // "{}" says the rest is literal text, not an extension — painting it as one would be a lie.
        const string Xaml = """<TextBlock Text="{}{0:N0} items"/>""";
        Assert.AreEqual("string", RoleOf(Xaml, "\"{}{0:N0} items\""));
    }

    [TestMethod]
    public void AnUnclosedExtension_DoesNotThrowOrBleedPastItsValue()
    {
        // The state of a file being typed into. It must degrade, not throw and not paint the rest of the line.
        const string Xaml = """<TextBlock Text="{Binding Foo" Tag="plain"/>""";
        Assert.AreEqual("string", RoleOf(Xaml, "\"plain\""), "the next value is untouched");
        Assert.AreEqual("attribute", RoleOf(Xaml, "Tag"));
    }
}
