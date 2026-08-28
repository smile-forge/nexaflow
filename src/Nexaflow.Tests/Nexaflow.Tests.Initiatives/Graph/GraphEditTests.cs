using System.Collections.Generic;
using System.Linq;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Syntax;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Graph;

/// <summary>
/// Structural edits addressed by graph node. Two things are on trial throughout: that the edit lands exactly
/// where the parser says the declaration is, and that it refuses when the file in hand no longer matches what
/// the graph recorded — because the graph is built from a checkout that may not be the working tree, so
/// trusting its line numbers is precisely the failure mode this exists to remove.
/// </summary>
[TestClass]
[CoversNode("graph-edit")]
public class GraphEditTests
{
    private const string Rel = "src/Sample.cs";

    private const string Source =
        "using System;\n" +
        "\n" +
        "class C\n" +
        "{\n" +
        "    /// <summary>Adds two numbers.</summary>\n" +
        "    public int Add(int a, int b)\n" +
        "    {\n" +
        "        return a + b;\n" +
        "    }\n" +
        "\n" +
        "    public int Sub(int a, int b)\n" +
        "    {\n" +
        "        return a - b;\n" +
        "    }\n" +
        "}\n";

    /// <summary>A graph holding just the node under test — the edit only ever reads identity from it.</summary>
    private static KnowledgeGraph GraphWith(string id, string label, string astPath, string file = Rel) =>
        new()
        {
            Nodes =
            [
                new GraphNode
                {
                    Id = id, Type = NodeType.Member, Label = label, FilePath = file, Language = "c-sharp",
                    Metadata = new Dictionary<string, string> { ["ast"] = astPath, ["line"] = "6" },
                },
            ],
        };

    private static GraphEdit.ReadText Reader(string text) => _ => text;

    private static readonly KnowledgeGraph AddNode = GraphWith("code:src/Sample.cs#T:C/M:Add", "Add", "T:C/M:Add");

    private static string Applied(GraphEdit.Result r)
    {
        Assert.IsTrue(r.Ok, r.Message);
        Assert.AreEqual(1, r.Changes.Count);
        return r.Changes[0].NewText;
    }

    /// <summary>
    /// Asserts a line is present <i>exactly</i>. Indentation bugs hide from <c>StringAssert.Contains</c>,
    /// because a doubly-indented line still contains the correctly-indented one as a substring — which is how
    /// the first version of these tests passed while the tool was emitting eight spaces for four.
    /// </summary>
    private static void AssertLine(string text, string expected)
    {
        var lines = SourceText.Of(text).Lines;
        Assert.IsTrue(lines.Contains(expected),
            $"expected a line exactly \"{expected}\", got:\n  " + string.Join("\n  ", lines));
    }

    // ── Replace ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void Replace_SwapsTheDeclaration_AndKeepsTheDocWrittenForIt()
    {
        var result = GraphEdit.Plan(AddNode, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.Replace,
            "public int Add(int a, int b)\n{\n    return checked(a + b);\n}", Reader(Source));

        var text = Applied(result);
        StringAssert.Contains(text, "/// <summary>Adds two numbers.</summary>",
            "replacing a method should not silently throw away its documentation");
        StringAssert.Contains(text, "return checked(a + b);");
        StringAssert.Contains(text, "return a - b;", "the neighbouring method must be untouched");
    }

    [TestMethod]
    public void Replace_IndentsFlushLeftTextToItsDestination()
    {
        var text = Applied(GraphEdit.Plan(AddNode, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.Replace,
            "public int Add(int a, int b)\n{\n    return a + b;\n}", Reader(Source)));

        AssertLine(text, "    public int Add(int a, int b)");
        AssertLine(text, "    {");
        AssertLine(text, "        return a + b;");
        AssertLine(text, "    }");
    }

    [TestMethod]
    public void Replace_WithTrivia_TakesTheDocToo()
    {
        var text = Applied(GraphEdit.Plan(AddNode, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.Replace,
            "/// <summary>Adds, carefully.</summary>\npublic int Add(int a, int b) => checked(a + b);",
            Reader(Source), new StructuralEdit.Options(WithTrivia: true)));

        StringAssert.Contains(text, "Adds, carefully.");
        Assert.IsFalse(text.Contains("Adds two numbers."), "the old doc should have gone with it");
    }

    // ── Delete ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Delete_TakesTheDocComment_AndDoesNotLeaveAWideningGap()
    {
        var text = Applied(GraphEdit.Plan(AddNode, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.Delete,
            null, Reader(Source)));

        Assert.IsFalse(text.Contains("Add"), "the declaration should be gone");
        Assert.IsFalse(text.Contains("Adds two numbers."), "its doc comment should go with it");
        Assert.IsFalse(text.Contains("\n\n\n"), "deleting should not leave a doubled blank line behind");
        StringAssert.Contains(text, "public int Sub(int a, int b)");
    }

    // ── Signature and body, each guarding the other half ────────────────────

    [TestMethod]
    public void Signature_ChangesTheHeader_AndLeavesTheBodyByteForByte()
    {
        var text = Applied(GraphEdit.Plan(AddNode, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.Signature,
            "public long Add(int a, int b)", Reader(Source)));

        AssertLine(text, "    public long Add(int a, int b)");
        StringAssert.Contains(text, "    {\n        return a + b;\n    }", "the body must be untouched");
    }

    /// <summary>
    /// A body passed as a signature would leave the method with two of them. Two independent guards catch
    /// it — the doubled body does not parse, and the body compares unequal afterwards — and either refusal
    /// is correct, so this asserts the refusal rather than which guard got there first.
    /// </summary>
    [TestMethod]
    public void Signature_RefusesWhenTheReplacementCarriesABody()
    {
        var result = GraphEdit.Plan(AddNode, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.Signature,
            "public long Add(int a, int b)\n{\n    return 0;\n}", Reader(Source));

        Assert.IsFalse(result.Ok, "a body passed as a signature would duplicate the body");
        StringAssert.Contains(result.Message, "not been applied");
        Assert.AreEqual(0, result.Changes.Count, "a refused edit offers nothing to write");
    }

    [TestMethod]
    public void Body_ChangesTheBody_AndLeavesTheSignature()
    {
        var text = Applied(GraphEdit.Plan(AddNode, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.Body,
            "{\n    checked { return a + b; }\n}", Reader(Source)));

        AssertLine(text, "    public int Add(int a, int b)");
        AssertLine(text, "        checked { return a + b; }");
    }

    // ── Rename ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Rename_RenamesTheDeclarationOnly()
    {
        var text = Applied(GraphEdit.Plan(AddNode, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.Rename,
            null, Reader(Source), renameTo: "Plus"));

        StringAssert.Contains(text, "public int Plus(int a, int b)");
        StringAssert.Contains(text, "return a + b;", "the body is not the declaration and must not change");
    }

    // ── Substitute: find-and-replace that cannot leave the declaration ──────

    [TestMethod]
    public void Substitute_ChangesTextInsideTheDeclaration()
    {
        var text = Applied(GraphEdit.Plan(AddNode, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.Substitute,
            "return checked(a + b);", Reader(Source),
            new StructuralEdit.Options(Find: "return a + b;")));

        AssertLine(text, "        return checked(a + b);");
        AssertLine(text, "        return a - b;");   // the neighbouring method must not be touched
    }

    /// <summary>
    /// The reason to reach for this instead of a stream edit: `a + b` appears in one method and `a - b` in
    /// the other, and a file-wide substitution has no way to know which one was meant. Here the search cannot
    /// see past the declaration being edited.
    /// </summary>
    [TestMethod]
    public void Substitute_CannotReachOutsideTheDeclaration()
    {
        var subNode = GraphWith("code:src/Sample.cs#T:C/M:Sub", "Sub", "T:C/M:Sub");
        var result  = GraphEdit.Plan(subNode, "code:src/Sample.cs#T:C/M:Sub", StructuralEdit.Op.Substitute,
            "return 0;", Reader(Source), new StructuralEdit.Options(Find: "return a + b;"));

        Assert.IsFalse(result.Ok, "text that lives in another method must not be found from this one");
        StringAssert.Contains(result.Message, "does not occur");
    }

    [TestMethod]
    public void Substitute_RefusesAnAmbiguousMatchUnlessAllIsAsked()
    {
        const string twice =
            "class C\n{\n    void M()\n    {\n        Log(x);\n        Log(x);\n    }\n}\n";
        var node = GraphWith("code:src/Sample.cs#T:C/M:M", "M", "T:C/M:M");

        var refused = GraphEdit.Plan(node, "code:src/Sample.cs#T:C/M:M", StructuralEdit.Op.Substitute,
            "Trace(x);", Reader(twice), new StructuralEdit.Options(Find: "Log(x);"));
        Assert.IsFalse(refused.Ok, "two matches is ambiguous, and guessing is what sed does wrong");
        StringAssert.Contains(refused.Message, "occurs 2 times");

        var text = Applied(GraphEdit.Plan(node, "code:src/Sample.cs#T:C/M:M", StructuralEdit.Op.Substitute,
            "Trace(x);", Reader(twice), new StructuralEdit.Options(Find: "Log(x);", AllOccurrences: true)));
        Assert.AreEqual(2, text.Split("Trace(x);").Length - 1);
    }

    /// <summary>
    /// Literal by default. `Log(x)` as a regex means "Log" followed by a captured x, which matches nothing
    /// here — the kind of surprise that makes a stream edit silently do the wrong thing.
    /// </summary>
    [TestMethod]
    public void Substitute_TreatsTheSearchAsLiteralUnlessRegexIsAsked()
    {
        const string src = "class C\n{\n    void M()\n    {\n        Log(x);\n    }\n}\n";
        var node = GraphWith("code:src/Sample.cs#T:C/M:M", "M", "T:C/M:M");

        var literal = Applied(GraphEdit.Plan(node, "code:src/Sample.cs#T:C/M:M", StructuralEdit.Op.Substitute,
            "Trace(x);", Reader(src), new StructuralEdit.Options(Find: "Log(x);")));
        AssertLine(literal, "        Trace(x);");

        var asRegex = GraphEdit.Plan(node, "code:src/Sample.cs#T:C/M:M", StructuralEdit.Op.Substitute,
            "Trace(x);", Reader(src), new StructuralEdit.Options(Find: "Log(x);", FindIsRegex: true));
        Assert.IsFalse(asRegex.Ok, "as a regex those parentheses are a group, and match nothing");
    }

    [TestMethod]
    public void Substitute_RefusesWhenTheResultWouldNotParse()
    {
        var result = GraphEdit.Plan(AddNode, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.Substitute,
            "return a + b;;;{", Reader(Source), new StructuralEdit.Options(Find: "return a + b;"));

        Assert.IsFalse(result.Ok, "a substitution is still an edit, and still has to leave the file parseable");
    }

    // ── Insert and append ───────────────────────────────────────────────────

    [TestMethod]
    public void InsertBefore_GoesAboveTheDoc_NotBetweenItAndItsDeclaration()
    {
        var text = Applied(GraphEdit.Plan(AddNode, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.InsertBefore,
            "public int Zero() => 0;", Reader(Source)));

        var zero = text.IndexOf("Zero", System.StringComparison.Ordinal);
        var doc  = text.IndexOf("Adds two numbers.", System.StringComparison.Ordinal);
        Assert.IsTrue(zero < doc, "an insertion must not be wedged between a doc comment and its declaration");
    }

    [TestMethod]
    public void Append_PutsAMemberAtTheEndOfTheTypeBody()
    {
        var graph = GraphWith("code:src/Sample.cs#T:C", "C", "T:C");
        var text  = Applied(GraphEdit.Plan(graph, "code:src/Sample.cs#T:C", StructuralEdit.Op.Append,
            "public int Zero() => 0;", Reader(Source)));

        var zero = text.IndexOf("Zero", System.StringComparison.Ordinal);
        var sub  = text.IndexOf("return a - b;", System.StringComparison.Ordinal);
        Assert.IsTrue(zero > sub, "an appended member belongs after the existing ones");
        AssertLine(text, "    public int Zero() => 0;");
        StringAssert.EndsWith(text.TrimEnd(), "}");
    }

    [TestMethod]
    public void Doc_ReplacesAnExistingCommentAndAddsOneWhereThereIsNone()
    {
        var replaced = Applied(GraphEdit.Plan(AddNode, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.Doc,
            "/// <summary>Sums.</summary>", Reader(Source)));
        StringAssert.Contains(replaced, "/// <summary>Sums.</summary>");
        Assert.IsFalse(replaced.Contains("Adds two numbers."));

        var sub = GraphWith("code:src/Sample.cs#T:C/M:Sub", "Sub", "T:C/M:Sub");
        var added = Applied(GraphEdit.Plan(sub, "code:src/Sample.cs#T:C/M:Sub", StructuralEdit.Op.Doc,
            "/// <summary>Subtracts.</summary>", Reader(Source)));
        AssertLine(added, "    /// <summary>Subtracts.</summary>");
        AssertLine(added, "    public int Sub(int a, int b)");
    }

    // ── Physical shape ──────────────────────────────────────────────────────

    [TestMethod]
    public void CrlfFilesStayCrlf_AndLfFilesStayLf()
    {
        var crlf = Source.Replace("\n", "\r\n");
        var text = Applied(GraphEdit.Plan(AddNode, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.Replace,
            "public int Add(int a, int b)\n{\n    return a + b;\n}", Reader(crlf)));

        Assert.AreEqual(0, text.Replace("\r\n", "").Count(c => c == '\n'),
            "a CRLF file must come back all-CRLF — a mixed file reads as a whole-file change in every diff");

        var lf = Applied(GraphEdit.Plan(AddNode, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.Replace,
            "public int Add(int a, int b)\r\n{\r\n    return a + b;\r\n}", Reader(Source)));
        Assert.IsFalse(lf.Contains('\r'), "CRLF replacement text must not smuggle carriage returns into an LF file");
    }

    // ── Refusals: the whole point ───────────────────────────────────────────

    [TestMethod]
    public void RefusesWhenTheAstPathNoLongerResolves()
    {
        var stale = GraphWith("code:src/Sample.cs#T:C/M:Gone", "Gone", "T:C/M:Gone");
        var result = GraphEdit.Plan(stale, "code:src/Sample.cs#T:C/M:Gone", StructuralEdit.Op.Delete, null, Reader(Source));

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "no longer resolves");
    }

    [TestMethod]
    public void RefusesWhenTheGraphsLabelDisagreesWithTheFile()
    {
        // The path resolves, but the graph calls it something else — the record is describing another file's
        // state, and position alone is not enough to edit on.
        var mislabelled = GraphWith("code:src/Sample.cs#T:C/M:Add", "Subtract", "T:C/M:Add");
        var result = GraphEdit.Plan(mislabelled, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.Delete, null, Reader(Source));

        Assert.IsFalse(result.Ok);
    }

    [TestMethod]
    public void RefusesWhenTheResultWouldNotParse()
    {
        var result = GraphEdit.Plan(AddNode, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.Replace,
            "public int Add(int a, int b) { return a + b;", Reader(Source));   // unbalanced

        Assert.IsFalse(result.Ok, "an edit that breaks the file must never reach disk");
        StringAssert.Contains(result.Message, "unparseable");
    }

    [TestMethod]
    public void RefusesWhenTheExpectedTextIsNotThere()
    {
        var result = GraphEdit.Plan(AddNode, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.Delete, null,
            Reader(Source), new StructuralEdit.Options(Expect: "return a * b;"));

        Assert.IsFalse(result.Ok, "a caller pinning the edit to what it read should be honoured");
        StringAssert.Contains(result.Message, "expected text");
    }

    [TestMethod]
    public void RefusesANodeThatNamesNoFile()
    {
        var product = new KnowledgeGraph
        {
            Nodes = [new GraphNode { Id = "product:x", Type = NodeType.Product, Label = "X" }],
        };
        var result = GraphEdit.Plan(product, "product:x", StructuralEdit.Op.Delete, null, Reader(Source));

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "not a code node");
    }

    [TestMethod]
    public void PlanningNeverWrites_AndReportsWhatWouldChange()
    {
        var result = GraphEdit.Plan(AddNode, "code:src/Sample.cs#T:C/M:Add", StructuralEdit.Op.Rename,
            null, Reader(Source), renameTo: "Plus");

        Assert.IsTrue(result.Ok);
        Assert.AreEqual(Source, result.Changes[0].OriginalText, "the original must come back untouched");
        Assert.AreNotEqual(Source, result.Changes[0].NewText);
        Assert.IsTrue(result.Changes[0].Hunk.Removed.Any(l => l.Contains("Add")),
            "the hunk should name the line that changed");
    }
}
