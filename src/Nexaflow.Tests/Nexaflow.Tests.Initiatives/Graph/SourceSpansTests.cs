using System.Linq;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Syntax;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Graph;

/// <summary>
/// <see cref="SourceSpans"/> decides where a declaration's source starts and stops, so it sets what
/// <c>graph code</c>, <c>graph context</c> and a content <c>graph grep</c> can see. Get it wrong and a grep
/// reports "no match" for something present — indistinguishable from absent.
/// <para>
/// These are the cases <c>GraphQuery.BlockEnd</c> used to answer: a hand-written C# lexer that counted braces
/// while tracking comments, verbatim strings, char literals and raw-string fences of any length. The behaviour
/// still has to hold, so the cases stay — but they now hold because tree-sitter parsed the file, not because a
/// second, partial C# parser guessed right. Several of them were bugs in that scanner first.
/// </para>
/// </summary>
[TestClass]
[CoversNode("product-graph-query")]
public class SourceSpansTests
{
    /// <summary>The 1-based line range reported for <paramref name="ast"/>, so assertions read like the source.</summary>
    private static (int First, int Last) Span(string source, string ast, int start0 = 0,
                                              int maxLines = 400, string rel = "Sample.cs")
    {
        var (s, e) = new SourceSpans().Block(rel, source.Replace("\r\n", "\n").Split('\n'), ast, start0, maxLines);
        return (s + 1, e + 1);
    }

    /// <summary>A member wrapped in the class it has to be a member of, so its declaration starts on line 3.</summary>
    private static string InClass(params string[] body)
        => "public class C\n{\n" + string.Join("\n", body) + "\n}";

    // ── The ordinary shapes ───────────────────────────────────────────────────

    [TestMethod]
    public void BracedMember_EndsAtItsClosingBrace()
    {
        var src = InClass(
            "    public void M()",   // 3
            "    {",                 // 4
            "        Inner();",      // 5
            "    }",                 // 6
            "    public void After() { }");

        Assert.AreEqual((3, 6), Span(src, "T:C/M:M"));
    }

    [TestMethod]
    public void ExpressionBodiedMember_EndsAtItsSemicolon()
    {
        var src = InClass(
            "    public int M() => 42;",   // 3
            "    public int After() => 1;");

        Assert.AreEqual((3, 3), Span(src, "T:C/M:M"));
    }

    [TestMethod]
    public void FieldWithATrailingComment_EndsOnItsOwnLine()
    {
        // What decided this in the scanner: it tested the TRIMMED LINE END for ';', so a trailing comment made
        // a field run on to whatever brace came next.
        var src = InClass(
            "    private const int X = 1; // why it is one",   // 3
            "    public void After()",
            "    {",
            "    }");

        Assert.AreEqual((3, 3), Span(src, "T:C/F:X"));
    }

    [TestMethod]
    public void BraceInsideAStringOrComment_IsNotABrace()
    {
        var src = InClass(
            "    public void M()",       // 3
            "    {",                     // 4
            "        var a = \"}\";",    // 5
            "        // }",              // 6
            "        /* } */",           // 7
            "    }");                    // 8

        Assert.AreEqual((3, 8), Span(src, "T:C/M:M"));
    }

    [TestMethod]
    public void VerbatimStringStartingALine_IsStillAString()
    {
        // One of the scanner's three divergences: it detected @" by looking BACK one character, so a line
        // beginning with one was missed and the brace inside it counted.
        var src = InClass(
            "    public void M()",   // 3
            "    {",                 // 4
            "        var a =",       // 5
            "@\"}\";",               // 6
            "    }");                // 7

        Assert.AreEqual((3, 7), Span(src, "T:C/M:M"));
    }

    // ── Declarations that continue past a closing brace ───────────────────────

    [TestMethod]
    public void AutoPropertyWithoutAnInitializer_EndsAtItsAccessorList()
    {
        var src = InClass(
            "    public int X { get; set; }",   // 3
            "    public int After { get; set; }");

        Assert.AreEqual((3, 3), Span(src, "T:C/P:X"));
    }

    [TestMethod]
    public void AutoPropertyWithAMultiLineInitializer_EndsAtTheTerminatingSemicolon()
    {
        // The accessor list's '}' is not the end of the declaration — a collection initializer can run for a
        // hundred lines after it. A brace count that stopped at the accessor list reported the property as one
        // line long, so `graph code` on it showed the declaration and nothing else.
        var src = InClass(
            "    public static IReadOnlyDictionary<string, string> Map { get; } = new Dictionary<string, string>",  // 3
            "    {",                                                                                                // 4
            "        [\"a\"] = \"one\",",                                                                           // 5
            "        [\"b\"] = \"two\",",                                                                           // 6
            "    };",                                                                                               // 7
            "    public int After => 1;");

        Assert.AreEqual((3, 7), Span(src, "T:C/P:Map"));
    }

    [TestMethod]
    public void FieldWithALambdaInitializer_EndsAtTheSemicolon()
    {
        var src = InClass(
            "    private static readonly Func<int> F = () =>",   // 3
            "    {",                                             // 4
            "        return 1;",                                 // 5
            "    };",                                            // 6
            "    public int After => 1;");

        Assert.AreEqual((3, 6), Span(src, "T:C/F:F"));
    }

    [TestMethod]
    public void MethodWithATrailingComment_StillEndsAtItsBrace()
    {
        var src = InClass(
            "    public void M() { } // still the end",   // 3
            "    public void After() { }");

        Assert.AreEqual((3, 3), Span(src, "T:C/M:M"));
    }

    [TestMethod]
    public void EventWithAccessors_EndsAtItsOuterBrace()
    {
        var src = InClass(
            "    public event EventHandler E { add { } remove { } }",   // 3
            "    public int After => 1;");

        Assert.AreEqual((3, 3), Span(src, "T:C/P:E"));
    }

    // ── Raw string literals ───────────────────────────────────────────────────

    [TestMethod]
    public void RawString_WithAnUnpairedQuote_DoesNotEndTheBlockEarly()
    {
        // The shape that broke the scanner outright. Three quotes toggle a naive in-string flag on-off-on, so
        // a raw string is "inside" only while its content holds an EVEN number of quotes; one unpaired quote
        // flips the parity and every brace after it counts as real code. This repo's fixtures are full of raw
        // strings containing C#, so it was not a theoretical case.
        var src = InClass(
            "    public void M()",              // 3
            "    {",                            // 4
            "        var s = \"\"\"",           // 5
            "            he said \"hello",      // 6  <- one unpaired quote
            "            }",                    // 7  <- must NOT be read as the member's closing brace
            "            \"\"\";",              // 8
            "    }");                           // 9

        Assert.AreEqual((3, 9), Span(src, "T:C/M:M"));
    }

    [TestMethod]
    public void RawString_WithMoreThanThreeQuotes_UsesItsOwnDelimiterLength()
    {
        // A four-quote fence exists so the content can contain three quotes; a three-quote run inside it must
        // not close it.
        var src = InClass(
            "    public void M()",              // 3
            "    {",                            // 4
            "        var s = \"\"\"\"",         // 5
            "            \"\"\" }",             // 6
            "            \"\"\"\";",            // 7
            "    }");                           // 8

        Assert.AreEqual((3, 8), Span(src, "T:C/M:M"));
    }

    // ── The whole type ────────────────────────────────────────────────────────

    [TestMethod]
    public void TypeNode_SpansTheWholeDeclaration()
    {
        var src = InClass(
            "    public void M() { }",   // 3
            "    public int X => 1;");   // 4

        Assert.AreEqual((1, 5), Span(src, "T:C"));
    }

    // ── Where the graph and the working tree disagree ─────────────────────────

    [TestMethod]
    public void BothEndsComeFromTheParse_NotFromTheLineTheGraphRecorded()
    {
        // What makes a query from a linked worktree describe THAT branch's code: the recorded line is only a
        // fallback. Here it is wrong — the member moved since the graph was built — and the answer is still right.
        var src = InClass(
            "    public void M()",   // 3
            "    {",                 // 4
            "    }");                // 5

        Assert.AreEqual((3, 5), Span(src, "T:C/M:M", start0: 99),
            "a stale start line must not survive a parse that can place the declaration itself");
    }

    [TestMethod]
    public void AnAstPathThatNoLongerResolves_FallsBackToTheDeclarationAroundTheLine()
    {
        // A renamed member against a graph built before the rename. Nothing can place M:Gone, but the parse
        // still knows what surrounds the recorded line — so the block is bounded by something the parser saw
        // rather than by a brace count starting mid-declaration.
        var src = InClass(
            "    public void M()",   // 3
            "    {",                 // 4
            "        Inner();",      // 5
            "    }");                // 6

        Assert.AreEqual((5, 6), Span(src, "T:C/M:Gone", start0: 4));
    }

    [TestMethod]
    public void AFileWithNoGrammar_IsBoundedByTheScanBudget()
    {
        // Nothing can parse a .txt, so the block runs to the end of the file — and the budget is what stops a
        // caller reading all of it. That is the runaway guard, not a working limit.
        var src = string.Join("\n", Enumerable.Range(0, 120).Select(i => "line " + i));

        Assert.AreEqual((1, 101), Span(src, "T:C/M:M", maxLines: 100, rel: "notes.txt"));
    }

    // ── A parse that records no end ───────────────────────────────────────────

    [TestMethod]
    public void ADeclarationTheExtractorGaveNoEnd_StopsBeforeTheNextOne()
    {
        // Not every extractor records an end (Razor's synthetic @code type is one). The same parse still knows
        // where the NEXT declaration begins, and stopping just before it beats guessing.
        var outline = new CodeOutline([], [
            new OutlineType("Code", 4, OutlineKind.Class, "T:Code", []),                     // no EndLine
            new OutlineType("Next", 9, OutlineKind.Class, "T:Next", []) { EndLine = 20 },
        ], []);

        var block = SourceSpans.BlockOf(outline, lineCount: 40, astPath: "T:Code", start0: 3, maxLines: 400);
        Assert.AreEqual((3, 7), block, "0-based: line 4 through the line before line 9");
    }

    [TestMethod]
    public void ADeclarationWithNoEnd_StillStartsWhereTheParseSaysItDoes()
    {
        // The start is exact even when the end is not, so it must beat the caller's fallback. Two of the three
        // call sites pass 0 here, having deliberately left the start to the parse - reading the caller's number
        // would report every such block as beginning at line 1.
        var outline = new CodeOutline([], [
            new OutlineType("Code", 12, OutlineKind.Class, "T:Code", []),                    // no EndLine
            new OutlineType("Next", 20, OutlineKind.Class, "T:Next", []) { EndLine = 30 },
        ], []);

        var block = SourceSpans.BlockOf(outline, lineCount: 40, astPath: "T:Code", start0: 0, maxLines: 400);
        Assert.AreEqual((11, 18), block, "0-based: line 12 through the line before line 20 - not line 1");
    }

    [TestMethod]
    public void ADeclarationWithNoEnd_IsBoundedByWhatContainsIt_WhenNothingFollows()
    {
        // The last member of a type has no next declaration to stop before, and running to end-of-file would
        // hand a grep every line after it. The container the parser DID measure is the tighter bound.
        var outline = new CodeOutline([], [
            new OutlineType("Host", 2, OutlineKind.Class, "T:Host", [
                new OutlineMember("Last", 8, OutlineKind.Method, "Last()", "T:Host/M:Last"),   // no EndLine
            ]) { EndLine = 14 },
        ], []);

        var block = SourceSpans.BlockOf(outline, lineCount: 60, astPath: "T:Host/M:Last", start0: 0, maxLines: 400);
        Assert.AreEqual((7, 13), block,
            "0-based: line 8 through the host type own end, not the end of the file");
    }
}
