using System.Collections.Generic;
using System.Linq;
using Nexaflow.Syntax;

namespace Nexaflow.Tests.Core.Unit.Editor;

[TestClass]
public class CodeHighlighterTests
{
    public static IEnumerable<object[]> Grammars() =>
        HighlightQueries.ByGrammar.Keys.Select(k => new object[] { k });

    [TestMethod]
    [DynamicData(nameof(Grammars))]
    public void Grammar_NativeLoadsAndQueryCompiles(string grammarId)
    {
        using var highlighter = CodeHighlighter.TryCreate(grammarId);
        Assert.IsNotNull(highlighter, $"grammar '{grammarId}' failed (native missing or query invalid)");
    }

    [TestMethod]
    public void CSharp_ProducesExpectedCaptures_WithCorrectOffsets()
    {
        using var highlighter = CodeHighlighter.TryCreate("c-sharp");
        Assert.IsNotNull(highlighter);

        const string src = "// hi\nclass C { string s = \"x\"; int n = 42; }";
        var spans = highlighter!.Highlight(src);

        var captures = spans.Select(s => s.Capture).Distinct().ToList();
        CollectionAssert.Contains(captures, "comment");
        CollectionAssert.Contains(captures, "keyword");
        CollectionAssert.Contains(captures, "string");
        CollectionAssert.Contains(captures, "number");

        // Offsets index into the same UTF-16 space as the editor document.
        var comment = spans.First(s => s.Capture == "comment");
        Assert.AreEqual("// hi", src.Substring(comment.Start, comment.Length));
        var number = spans.First(s => s.Capture == "number");
        Assert.AreEqual("42", src.Substring(number.Start, number.Length));
    }

    [TestMethod]
    public void CSharp_DistinguishesTypesFunctionsAndParameters()
    {
        using var highlighter = CodeHighlighter.TryCreate("c-sharp");
        const string src = "class Greeter { string Greet(string name) => $\"Hi {name}!\"; }";
        var pairs = highlighter!.Highlight(src)
            .Select(s => (cap: s.Capture, text: src.Substring(s.Start, s.Length)))
            .ToArray();

        CollectionAssert.Contains(pairs, (cap: "type", text: "Greeter"));      // class name
        CollectionAssert.Contains(pairs, (cap: "function", text: "Greet"));    // method name
        CollectionAssert.Contains(pairs, (cap: "variable", text: "name"));     // parameter → variable role

        // the identifier inside $"…{name}…" shares the variable/parameter colour → two 'name' spans
        Assert.AreEqual(2, pairs.Count(p => p is ("variable", "name")));
        // interpolation braces are deliberately uncaptured (render as normal text)
        Assert.IsFalse(pairs.Any(p => p.text is "{" or "}"));
    }

    [TestMethod]
    public void UnknownGrammar_ReturnsNull()
    {
        Assert.IsNull(CodeHighlighter.TryCreate("nonsense-lang"));
    }

    [TestMethod]
    public void CSharp_ParseTree_IsSExpression()
    {
        using var highlighter = CodeHighlighter.TryCreate("c-sharp");
        var tree = highlighter!.GetParseTree("class C {}");
        Assert.IsNotNull(tree);
        StringAssert.Contains(tree, "class_declaration");
    }

    [TestMethod]
    public void CSharp_ProducesFolds_ForBlocks()
    {
        using var highlighter = CodeHighlighter.TryCreate("c-sharp");
        const string src = "class C\n{\n    void M()\n    {\n        return;\n    }\n}";
        var folds = highlighter!.GetFolds(src);

        Assert.IsTrue(folds.Count >= 2, "expected at least the class body and method body to fold");
        Assert.IsTrue(folds.All(f => f.Start >= 0 && f.End <= src.Length && f.Start < f.End), "folds in bounds");
        Assert.IsTrue(folds.Any(f => src.Substring(f.Start, f.End - f.Start).Contains("return")),
            "a fold should cover the method body");
    }
}
