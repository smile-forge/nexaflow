using System;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// <see cref="GraphQuery.BlockEnd"/> decides where a member's source stops, so it sets what
/// <c>graph code</c>, <c>graph context</c> and a content <c>graph grep</c> can see. Get it wrong and a grep
/// reports "no match" for something present — indistinguishable from absent.
/// <para>
/// It went untested while two copies of it drifted apart in three ways. These are the semantics that survived
/// the merge, each pinned to the case that decided it.
/// </para>
/// </summary>
[TestClass]
[CoversNode("product-graph-query")]
public class BlockEndTests
{
    /// <summary>1-based line (as the graph stores it) → the 0-based index BlockEnd takes.</summary>
    private static int End(string source, int startLine = 1, int maxLines = 400)
        => GraphQuery.BlockEnd(source.Replace("\r\n", "\n").Split('\n'), startLine - 1, maxLines);

    /// <summary>The 1-based line BlockEnd chose, for assertions that read like the source does.</summary>
    private static int EndLine(string source, int startLine = 1, int maxLines = 400)
        => End(source, startLine, maxLines) + 1;

    // ── The ordinary shapes ───────────────────────────────────────────────────

    [TestMethod]
    public void BracedMember_EndsAtItsClosingBrace()
    {
        var src = string.Join("\n",
            "public void M()",      // 1
            "{",                    // 2
            "    Inner();",         // 3
            "}",                    // 4
            "public void After() { }");

        Assert.AreEqual(4, EndLine(src));
    }

    [TestMethod]
    public void ExpressionBodiedMember_EndsAtItsSemicolon()
    {
        var src = string.Join("\n",
            "public int M() => 42;",
            "public int After() => 1;");

        Assert.AreEqual(1, EndLine(src));
    }

    [TestMethod]
    public void FieldWithATrailingComment_EndsOnItsOwnLine()
    {
        // The rule that decided this: the other copy tested the TRIMMED LINE END for ';', so a trailing
        // comment made a field run on to whatever brace came next.
        var src = string.Join("\n",
            "private const int X = 1; // why it is one",
            "public void After()",
            "{",
            "}");

        Assert.AreEqual(1, EndLine(src));
    }

    [TestMethod]
    public void BraceInsideAStringOrComment_IsNotABrace()
    {
        var src = string.Join("\n",
            "public void M()",
            "{",
            "    var a = \"}\";",
            "    // }",
            "    /* } */",
            "}");

        Assert.AreEqual(6, EndLine(src));
    }

    [TestMethod]
    public void VerbatimStringStartingALine_IsStillAString()
    {
        // The other copy detected @" by looking BACK one character, so a line beginning with it was missed
        // and the brace inside counted.
        var src = string.Join("\n",
            "public void M()",
            "{",
            "    var a =",
            "@\"}\";",
            "}");

        Assert.AreEqual(5, EndLine(src));
    }

    [TestMethod]
    public void NoClosingBrace_FallsBackToTheFullBudgetAsked()
    {
        // The other copy clamped this to 40 lines whatever was asked, so raising the shared scan budget
        // fixed one caller and silently left the other short.
        var src = string.Join("\n", new string[120]).Replace("\n", "x\n"); // 120 lines, no braces at all
        var lines = src.Split('\n').Length;

        Assert.AreEqual(Math.Min(lines - 1, 100), End(src, maxLines: 100),
            "an unterminated block should stop where the caller's budget says, not at a hidden 40");
    }

    // ── Declarations that continue past a closing brace ───────────────────────

    [TestMethod]
    public void AutoPropertyWithoutAnInitializer_EndsAtItsAccessorList()
    {
        var src = string.Join("\n",
            "public int X { get; set; }",
            "public int After { get; set; }");

        Assert.AreEqual(1, EndLine(src));
    }

    [TestMethod]
    public void AutoPropertyWithAnInitializer_EndsAtItsSemicolon()
    {
        // The accessor list's '}' is not the end of the declaration - '= 5;' still follows it.
        var src = string.Join("\n",
            "public int X { get; } = 5;",
            "public int After { get; } = 6;");

        Assert.AreEqual(1, EndLine(src));
    }

    [TestMethod]
    public void AutoPropertyWithAMultiLineInitializer_EndsAtTheTerminatingSemicolon()
    {
        // The shape that exposed this: a property whose collection initializer runs for a hundred lines
        // reported itself as one line long, so `graph code` on it showed the declaration and nothing else.
        var src = string.Join("\n",
            "public static IReadOnlyDictionary<string, string> Map { get; } = new Dictionary<string, string>",  // 1
            "{",                                                                                                // 2
            "    [\"a\"] = \"one\",",                                                                           // 3
            "    [\"b\"] = \"two\",",                                                                           // 4
            "};",                                                                                               // 5
            "public int After => 1;");

        Assert.AreEqual(5, EndLine(src));
    }

    [TestMethod]
    public void FieldWithALambdaInitializer_EndsAtTheSemicolon()
    {
        var src = string.Join("\n",
            "private static readonly Func<int> F = () =>",
            "{",
            "    return 1;",
            "};",
            "public int After => 1;");

        Assert.AreEqual(4, EndLine(src));
    }

    [TestMethod]
    public void MethodWithATrailingComment_StillEndsAtItsBrace()
    {
        // A closing brace followed by nothing that matters is still the end - the rule keys on whether the
        // DECLARATION continues, not on whether the line does.
        var src = string.Join("\n",
            "public void M() { } // still the end",
            "public void After() { }");

        Assert.AreEqual(1, EndLine(src));
    }

    [TestMethod]
    public void EventWithAccessors_EndsAtItsOuterBrace()
    {
        var src = string.Join("\n",
            "public event EventHandler E { add { } remove { } }",
            "public int After => 1;");

        Assert.AreEqual(1, EndLine(src));
    }

    // ── Raw string literals ───────────────────────────────────────────────────

    [TestMethod]
    public void RawString_WithAnUnpairedQuote_DoesNotEndTheBlockEarly()
    {
        // The failing shape. Three quotes toggle a naive in-string flag on-off-on, so a raw string is
        // "inside" only while its content has an EVEN number of quotes. One unpaired quote flips the parity
        // and every brace after it is counted as real code.
        var src = string.Join("\n",
            "public void M()",              // 1
            "{",                            // 2
            "    var s = \"\"\"",           // 3
            "        he said \"hello",      // 4  <- one unpaired quote
            "        }",                    // 5  <- must NOT be read as the member's closing brace
            "        \"\"\";",              // 6
            "}");                           // 7

        Assert.AreEqual(7, EndLine(src));
    }

    [TestMethod]
    public void RawString_WithBracesAndQuotes_IsOpaque()
    {
        var src = string.Join("\n",
            "public void M()",
            "{",
            "    var json = \"\"\"",
            "        { \"a\": 1, \"b\": \"}\" }",
            "        \"\"\";",
            "}");

        Assert.AreEqual(6, EndLine(src));
    }

    [TestMethod]
    public void SingleLineRawString_ClosesOnItsOwnLine()
    {
        var src = string.Join("\n",
            "public void M()",
            "{",
            "    var s = \"\"\"a } b\"\"\";",
            "}");

        Assert.AreEqual(4, EndLine(src));
    }

    [TestMethod]
    public void RawString_WithMoreThanThreeQuotes_UsesItsOwnDelimiterLength()
    {
        // A four-quote fence exists so the content can contain three quotes. A three-quote run inside it
        // must not close it.
        var src = string.Join("\n",
            "public void M()",
            "{",
            "    var s = \"\"\"\"",
            "        \"\"\" }",
            "        \"\"\"\";",
            "}");

        Assert.AreEqual(6, EndLine(src));
    }

    [TestMethod]
    public void InterpolatedRawString_IsOpaqueToo()
    {
        var src = string.Join("\n",
            "public void M()",
            "{",
            "    var s = $$\"\"\"",
            "        { not an interpolation } {{Real}}",
            "        \"\"\";",
            "}");

        Assert.AreEqual(6, EndLine(src));
    }

    [TestMethod]
    public void CodeAfterARawString_IsReadAsCodeAgain()
    {
        // The other half of opacity: having skipped the literal, the scanner has to come back out of it.
        var src = string.Join("\n",
            "public void M()",
            "{",
            "    var s = \"\"\"",
            "        \"",
            "        \"\"\";",
            "    if (x) { }",
            "}");

        Assert.AreEqual(7, EndLine(src));
    }
}
