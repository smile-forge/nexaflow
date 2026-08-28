using System.Collections.Generic;
using System.Linq;
using Nexaflow.Syntax;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Syntax;

[TestClass]
public class CodeHighlighterTests
{
    public static IEnumerable<object[]> Grammars() =>
        HighlightQueries.ByGrammar.Keys.Select(k => new object[] { k });

    [TestMethod]
    [DynamicData(nameof(Grammars))]
    [CoversNode("syntax-queries")]
    public void Grammar_NativeLoadsAndQueryCompiles(string grammarId)
    {
        using var highlighter = CodeHighlighter.TryCreate(grammarId);
        Assert.IsNotNull(highlighter, $"grammar '{grammarId}' failed (native missing or query invalid)");
    }

    [TestMethod]
    [CoversNode("syntax-queries")]
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
    [CoversNode("syntax-queries")]
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
    [CoversNode("syntax-queries")]
    public void UnknownGrammar_ReturnsNull()
    {
        Assert.IsNull(CodeHighlighter.TryCreate("nonsense-lang"));
    }

    [TestMethod]
    [CoversNode("code-ai-act-get-syntax-tree")]
    public void CSharp_ParseTree_IsSExpression()
    {
        using var highlighter = CodeHighlighter.TryCreate("c-sharp");
        var tree = highlighter!.GetParseTree("class C {}");
        Assert.IsNotNull(tree);
        StringAssert.Contains(tree, "class_declaration");
    }

    [TestMethod]
    [CoversNode("code-folding")]
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

    [TestMethod]
    [CoversNode("code-folding")]
    public void CSharp_FoldsCommentBlocks()
    {
        using var highlighter = CodeHighlighter.TryCreate("c-sharp");
        const string src = "// alpha\n// beta\n// gamma\nclass C\n{\n    /* one\n       two\n       three */\n    void M() { }\n}";
        var folds = highlighter!.GetFolds(src);

        Assert.IsTrue(folds.Any(f => Covers(src, f, "alpha") && Covers(src, f, "gamma")),
            "consecutive line comments fold as one block");
        Assert.IsTrue(folds.Any(f => Covers(src, f, "/* one") && Covers(src, f, "three */")),
            "a multi-line block comment folds");
    }

    [TestMethod]
    [CoversNode("code-folding")]
    public void CSharp_DoesNotFold_LoneOwnLineComment()
    {
        using var highlighter = CodeHighlighter.TryCreate("c-sharp");
        const string src = "// lonely\nclass C {}";   // single comment + empty class body ⇒ nothing to fold
        var folds = highlighter!.GetFolds(src);

        Assert.IsFalse(folds.Any(f => Covers(src, f, "lonely")), "a single own-line comment is not folded");
    }

    /// <summary>
    /// One sample per grammar, each carrying the shape that broke: a run of own-line comments above a
    /// foldable block. Written with \n so each case can be run twice, once converted to \r\n.
    /// </summary>
    private static readonly Dictionary<string, string> FoldSamples = new()
    {
        ["c"]                 = "// alpha\n// beta\nint main(void)\n{\n    return 0;\n}\n",
        ["cpp"]               = "// alpha\n// beta\nint main()\n{\n    return 0;\n}\n",
        ["c-sharp"]           = "// alpha\n// beta\nclass C\n{\n    void M()\n    {\n    }\n}\n",
        ["java"]              = "// alpha\n// beta\nclass C {\n    void m() {\n    }\n}\n",
        ["javascript"]        = "// alpha\n// beta\nfunction f() {\n  return 1;\n}\n",
        ["typescript"]        = "// alpha\n// beta\nfunction f(): number {\n  return 1;\n}\n",
        ["rust"]              = "// alpha\n// beta\nfn main() {\n    let x = 1;\n}\n",
        ["php"]               = "<?php\n// alpha\n// beta\nfunction f() {\n  return 1;\n}\n",
        ["css"]               = "/* alpha */\n/* beta */\n.x {\n  color: red;\n}\n",
        ["python"]            = "# alpha\n# beta\ndef f():\n    return 1\n",
        ["ruby"]              = "# alpha\n# beta\ndef f\n  1\nend\n",
        ["html"]              = "<!-- alpha -->\n<!-- beta -->\n<html>\n  <body>\n    <p>x</p>\n  </body>\n</html>\n",
        ["jinja"]             = "{# alpha #}\n{# beta #}\n<div>\n  <p>x</p>\n</div>\n",
        ["xml"]               = "<!-- alpha -->\n<!-- beta -->\n<root>\n  <a>\n    <b>x</b>\n  </a>\n</root>\n",
        ["xaml"]              = "<!-- alpha -->\n<!-- beta -->\n<Grid>\n  <TextBlock>\n    x\n  </TextBlock>\n</Grid>\n",
        ["json"]              = "{\n  \"a\": {\n    \"b\": 1\n  }\n}\n",
        ["embedded-template"] = "<%# alpha %>\n<%# beta %>\n<div>\n  <p>x</p>\n</div>\n",
        ["razor"]             = "@* alpha *@\n@* beta *@\n<div>\n  <p>x</p>\n</div>\n",
    };

    public static IEnumerable<object[]> FoldSampleGrammars() =>
        FoldSamples.Keys.Select(k => new object[] { k });

    /// <summary>
    /// A fold must never end strictly inside a line delimiter. AvalonEdit refuses such an element, and it
    /// refuses it from the render pass — where the shell cannot recover, because handling the exception
    /// leaves the layout dirty and WPF just re-measures and throws again. One customer's log was that
    /// live-lock: the same trace 17,724 times in 85 seconds, 170MB.
    /// <para>
    /// It only bites on CRLF, since a one-character delimiter has no "strictly inside". Every folding test
    /// here used \n literals, which is exactly why c, cpp, java, rust, python and ruby all shipped broken —
    /// their grammars match a line comment as "up to \n", so the token swallows the CR.
    /// </para>
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(FoldSampleGrammars))]
    [CoversNode("code-folding")]
    public void Folds_NeverEndInsideALineDelimiter(string grammarId)
    {
        using var highlighter = CodeHighlighter.TryCreate(grammarId);
        Assert.IsNotNull(highlighter, $"grammar '{grammarId}' failed to load");

        var lf   = FoldSamples[grammarId];
        var crlf = lf.Replace("\n", "\r\n");

        AssertNoneEndInDelimiter(highlighter, lf,   grammarId, "LF");
        AssertNoneEndInDelimiter(highlighter, crlf, grammarId, "CRLF");
    }

    private static void AssertNoneEndInDelimiter(CodeHighlighter highlighter, string src,
                                                 string grammarId, string endings)
    {
        foreach (var f in highlighter.GetFolds(src))
        {
            Assert.IsTrue(f.Start >= 0 && f.End <= src.Length && f.Start < f.End,
                $"[{grammarId}/{endings}] fold [{f.Start}..{f.End}) is out of bounds for {src.Length} chars");

            bool insideDelimiter = f.End < src.Length && src[f.End - 1] == '\r' && src[f.End] == '\n';
            Assert.IsFalse(insideDelimiter,
                $"[{grammarId}/{endings}] fold [{f.Start}..{f.End}) ends between the \\r and the \\n — "
                + "AvalonEdit throws on this from the render pass and the shell cannot recover");
        }
    }

    /// <summary>The guard above is only worth as much as its coverage: a new grammar must bring a sample.</summary>
    [TestMethod]
    [CoversNode("code-folding")]
    public void EveryGrammar_HasAFoldSample()
    {
        var missing = HighlightQueries.ByGrammar.Keys.Where(k => !FoldSamples.ContainsKey(k)).ToList();
        Assert.AreEqual(0, missing.Count,
            $"grammars with no fold sample, so untested against the CRLF delimiter bug: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Trimming must take the CR off the fold, not take the fold away — the six broken grammars should still
    /// collapse a comment run, just ending on the comment text.
    /// </summary>
    [TestMethod]
    [CoversNode("code-folding")]
    public void CommentRun_StillFolds_OnCrlf_AndEndsOnTheText()
    {
        using var highlighter = CodeHighlighter.TryCreate("python");
        const string src = "# alpha\r\n# beta\r\ndef f():\r\n    return 1\r\n";

        var run = highlighter!.GetFolds(src).FirstOrDefault(f => Covers(src, f, "alpha") && Covers(src, f, "beta"));
        Assert.AreNotEqual(default, run, "the comment run should still fold on CRLF");
        Assert.AreEqual("# alpha\r\n# beta", src.Substring(run.Start, run.End - run.Start),
            "the fold should end on the comment text, not on the CR that the grammar swallowed");
    }

    private static bool Covers(string src, FoldRange f, string needle) =>
        src.Substring(f.Start, f.End - f.Start).Contains(needle);
}
