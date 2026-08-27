using System.Linq;
using Nexaflow.Syntax;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.Editor;

/// <summary>
/// The editing engine itself, on the shapes real code has rather than the tidy method every example uses.
/// Most of what is here was written after driving the tools by hand and finding they did the wrong thing:
/// a property has accessors rather than a body, a Python class body has no closing brace to insert before,
/// an empty type body has no member to copy an indent from, and an import belongs to the file rather than
/// to any declaration in it.
/// </summary>
[TestClass]
[CoversNode("graph-edit")]
public class StructuralEditTests
{
    private const string Widget = """
        using System;

        namespace N;

        public class Widget
        {
            private int _count;

            public int Count
            {
                get => _count;
                set => _count = value;
            }

            public void Add(int n)
            {
                _count += n;
            }
        }
        """;

    private static string Applied(StructuralEdit.Result r)
    {
        Assert.IsTrue(r.Ok, r.Message);
        Assert.IsNotNull(r.NewText);
        return r.NewText!;
    }

    private static void AssertLine(string text, string expected)
    {
        var lines = SourceText.Of(text).Lines;
        Assert.IsTrue(lines.Contains(expected),
            $"expected a line exactly \"{expected}\", got:\n  " + string.Join("\n  ", lines));
    }

    /// <summary>The 0-based index of the line that is exactly <paramref name="line"/>, or -1.</summary>
    private static int LineOf(string text, string line) => SourceText.Of(text).Lines.ToList().IndexOf(line);

    // ── Addressing ──────────────────────────────────────────────────────────

    /// <summary>Without this an editor has nothing to name a declaration with — it has no graph to look
    /// node ids up in.</summary>
    [TestMethod]
    public void Declarations_ListsWhatCanBeAddressed_IncludingOverloadsSeparately()
    {
        const string overloads = """
            class C
            {
                public void M(int a) { }
                public void M(int a, int b) { }
            }
            """;

        var found = StructuralEdit.Declarations("c-sharp", overloads);
        var paths = found.Select(d => d.AstPath).ToList();

        CollectionAssert.Contains(paths, "T:C");
        Assert.AreEqual(2, paths.Count(p => p.StartsWith("T:C/M:M")),
            "two overloads must be addressable separately, or one of them can never be edited");
        Assert.IsTrue(found.SequenceEqual(found.OrderBy(d => d.Line)), "listed in document order");
    }

    [TestMethod]
    public void Declarations_AreEmpty_ForAFileWithNoGrammar()
    {
        Assert.AreEqual(0, StructuralEdit.Declarations("", "whatever").Count);
    }

    // ── Shapes that are not a plain method ──────────────────────────────────

    /// <summary>
    /// A C# property's accessors are an <c>accessor_list</c>, not a <c>body</c> field, so asking the grammar
    /// for "the body" by name comes back empty and every body/signature edit on a property was refused.
    /// </summary>
    [TestMethod]
    public void AProperty_HasAnEditableBody_EvenThoughTheGrammarDoesNotCallItThat()
    {
        var text = Applied(StructuralEdit.Apply("c-sharp", Widget, "T:Widget/P:Count", StructuralEdit.Op.Body,
            "{\n    get => _count;\n    set => _count = value < 0 ? 0 : value;\n}"));

        AssertLine(text, "        set => _count = value < 0 ? 0 : value;");
        AssertLine(text, "    public int Count");
    }

    /// <summary>
    /// The inverse guard: "last named child" would call an interface method's parameter list its body, and
    /// replacing the body would then eat the parameters — wrong, and it would still parse.
    /// </summary>
    [TestMethod]
    public void AnInterfaceMethod_HasNoBody_AndIsRefusedRatherThanLosingItsParameters()
    {
        const string iface = "public interface IThing\n{\n    void Do(int n);\n}\n";

        var result = StructuralEdit.Apply("c-sharp", iface, "T:IThing/M:Do", StructuralEdit.Op.Body, "{ }");
        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "no body");

        // Replacing the whole declaration is the way to change it, and that still works.
        AssertLine(Applied(StructuralEdit.Apply("c-sharp", iface, "T:IThing/M:Do",
            StructuralEdit.Op.Replace, "void Do(long n);")), "    void Do(long n);");
    }

    // ── Append, where the brace assumption breaks ───────────────────────────

    /// <summary>
    /// Python's class body ends with its last statement, not with a brace. "Insert before the line the body
    /// closes on" put the new method INSIDE the previous one — it parsed, so nothing caught it but reading.
    /// </summary>
    [TestMethod]
    public void Append_InAnIndentationDelimitedLanguage_GoesAfterTheLastMember()
    {
        const string py = "class A:\n    def f(self):\n        return 1\n\n    def g(self):\n        return 2\n";

        var text = Applied(StructuralEdit.Apply("python", py, "T:A", StructuralEdit.Op.Append,
            "def h(self):\n    return 3"));

        Assert.IsTrue(LineOf(text, "    def h(self):") > LineOf(text, "        return 2"),
            "the appended method must come after the previous one, not inside it");
        AssertLine(text, "    def h(self):");
        AssertLine(text, "        return 3");
    }

    /// <summary>An empty body has no member to copy an indent from, and the closing brace is not one —
    /// reading it as one is what put an appended member flush-left.</summary>
    [TestMethod]
    public void Append_IntoAnEmptyBody_IsStillIndented_AndAddsNoBlankLine()
    {
        var text = Applied(StructuralEdit.Apply("c-sharp", "class C\n{\n}\n", "T:C",
            StructuralEdit.Op.Append, "public int X;"));

        Assert.AreEqual("class C\n{\n    public int X;\n}\n", text);
    }

    [TestMethod]
    public void Append_MatchesTheIndentTheTypeAlreadyUses()
    {
        const string twoSpace = "class C\n{\n  void M() { }\n}\n";
        AssertLine(Applied(StructuralEdit.Apply("c-sharp", twoSpace, "T:C", StructuralEdit.Op.Append,
            "void N() { }")), "  void N() { }");
    }

    // ── Imports ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reaching this through insert-before on the first declaration was possible and wrong: with a
    /// file-scoped namespace it put the <c>using</c> underneath the <c>namespace</c>, which compiles and
    /// reads as a mistake.
    /// </summary>
    [TestMethod]
    public void AddImport_JoinsTheExistingBlock_NotTheFirstDeclaration()
    {
        var text = Applied(StructuralEdit.AddImport("c-sharp", Widget, "using System.Linq;"));

        Assert.AreEqual(1, LineOf(text, "using System.Linq;"), "it belongs directly under the last using");
        Assert.IsTrue(LineOf(text, "using System.Linq;") < LineOf(text, "namespace N;"),
            "a using must not land below the namespace it applies to");
    }

    [TestMethod]
    public void AddImport_StaysBelowAHeaderComment_WhenThereAreNoImportsYet()
    {
        const string header = "// Copyright (c) Smile-Forge.\n// Licensed under MIT.\n\nnamespace N;\n\nclass W { }\n";

        var text = Applied(StructuralEdit.AddImport("c-sharp", header, "using System;"));

        Assert.IsTrue(LineOf(text, "using System;") > LineOf(text, "// Licensed under MIT."),
            "a licence header stays at the top of the file");
        Assert.IsTrue(LineOf(text, "using System;") < LineOf(text, "namespace N;"));
    }

    [TestMethod]
    public void AddImport_RefusesOneThatIsAlreadyThere()
    {
        var result = StructuralEdit.AddImport("c-sharp", Widget, "using System;");
        Assert.IsFalse(result.Ok, "adding a duplicate using is never what was meant");
        StringAssert.Contains(result.Message, "already imported");
    }

    [TestMethod]
    [DataRow("python",     "import os\nimport sys\n\ndef f():\n    return 1\n",  "import json")]
    [DataRow("rust",       "use std::fmt;\n\nfn main() { }\n",                    "use std::io;")]
    [DataRow("typescript", "import { a } from \"./a\";\n\nexport class S { }\n",  "import { b } from \"./b\";")]
    public void AddImport_WorksWhereverAGrammarHasImports(string grammar, string src, string import)
    {
        // These fixtures open with their import block, so its length is the leading run of non-blank lines —
        // and the new import belongs directly under it, whether that block holds one line or three.
        var existing = SourceText.Of(src).Lines.TakeWhile(l => l.Trim().Length > 0).Count();
        var added    = LineOf(Applied(StructuralEdit.AddImport(grammar, src, import)), import);

        Assert.AreNotEqual(-1, added, "the import should have been added");
        Assert.AreEqual(existing, added,
            $"it belongs directly under the {existing} import(s) already there, not at line {added + 1}");
    }

    // ── Substitute: finding text you copied from somewhere else ─────────────

    private const string Guarded = """
        public class W
        {
            public void Add(int n)
            {
                if (n < 0)
                {
                    throw new System.ArgumentOutOfRangeException(nameof(n));
                }

                _count += n;
            }

            public void Reset()
            {
                _count = 0;
            }

            private int _count;
        }
        """;

    private static StructuralEdit.Result Sub(string src, string path, string find, string replace,
                                             bool all = false) =>
        StructuralEdit.Apply("c-sharp", src, path, StructuralEdit.Op.Substitute, replace,
                             new StructuralEdit.Options(Find: find, AllOccurrences: all));

    /// <summary>
    /// Everywhere else this tool promises the caller does not handle whitespace — text written flush-left
    /// lands indented. <c>find</c> demanded it byte-for-byte, which made a fragment copied out of a listing
    /// fail for a reason that has nothing to do with the code.
    /// </summary>
    [TestMethod]
    public void Substitute_FindsAMultiLineFragmentWrittenFlushLeft()
    {
        var result = Sub(Guarded, "T:W/M:Add",
            "if (n < 0)\n{\n    throw new System.ArgumentOutOfRangeException(nameof(n));\n}",
            "if (n < 0) throw new System.ArgumentOutOfRangeException(nameof(n));");

        Assert.IsTrue(result.Ok, result.Message);
        AssertLine(result.NewText!, "        if (n < 0) throw new System.ArgumentOutOfRangeException(nameof(n));");
        CollectionAssert.Contains(result.Notes.ToList(), "matched ignoring indentation",
            "the caller should be told the match was not literal");
    }

    [TestMethod]
    public void Substitute_FindsAFragmentCopiedAtTheWrongDepth()
    {
        var result = Sub(Guarded, "T:W/M:Add",
            "  if (n < 0)\n  {\n      throw new System.ArgumentOutOfRangeException(nameof(n));\n  }",
            "if (n < 0) return;");

        Assert.IsTrue(result.Ok, result.Message);
        AssertLine(result.NewText!, "        if (n < 0) return;");
    }

    /// <summary>Exact still wins, so nothing that worked before behaves differently — and a literal match
    /// stays character-granular rather than becoming line-granular.</summary>
    [TestMethod]
    public void Substitute_PrefersAnExactMatch_AndSaysNothingAboutIndentation()
    {
        var result = Sub(Guarded, "T:W/M:Add", "        _count += n;", "        _count += n * 2;");

        Assert.IsTrue(result.Ok, result.Message);
        Assert.AreEqual(0, result.Notes.Count, "a byte-for-byte match needs no explanation");
        AssertLine(result.NewText!, "        _count += n * 2;");
    }

    [TestMethod]
    public void Substitute_StillRefusesAnAmbiguousLooseMatch()
    {
        const string twice = """
            public class W
            {
                public void M(bool b)
                {
                    if (b)
                    {
                        Log();
                    }

                    if (b)
                    {
                        Log();
                    }
                }
            }
            """;

        // Flush-left, so it cannot match literally — the two candidates are found only by ignoring indentation.
        var result = Sub(twice, "T:W/M:M", "if (b)\n{\n    Log();\n}", "if (b) Log();");

        Assert.IsFalse(result.Ok, "ignoring indentation must not also mean guessing which one was meant");
        StringAssert.Contains(result.Message, "occurs 2 times");
    }

    /// <summary>
    /// The usual cause of a miss is the right fragment and the wrong declaration, so saying which one holds
    /// it turns a dead end into the next call.
    /// </summary>
    [TestMethod]
    public void Substitute_WhenTextIsInAnotherDeclaration_NamesTheInnermostOne()
    {
        var result = Sub(Guarded, "T:W/M:Add", "_count = 0;", "_count = 1;");

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "'Reset'");
        StringAssert.Contains(result.Message, "T:W/M:Reset");
        Assert.IsFalse(result.Message.Contains("'W'"),
            "every member is also inside its type; naming the type is not an answer");
    }

    [TestMethod]
    public void Substitute_WhenTextIsNowhere_SaysIndentationIsNotTheProblem()
    {
        var result = Sub(Guarded, "T:W/M:Add", "_total += n;", "x");

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "Indentation is ignored");
    }

    [TestMethod]
    public void Substitute_ReportsHowManyItReplaced()
    {
        const string thrice = """
            public class W
            {
                public void M()
                {
                    Log();
                    Log();
                    Log();
                }
            }
            """;

        var result = Sub(thrice, "T:W/M:M", "Log();", "Trace();", all: true);

        Assert.IsTrue(result.Ok, result.Message);
        Assert.IsTrue(result.Notes.Any(n => n.Contains("3 occurrences")),
            "'replace all' should say how many it touched, not just that it worked");
    }

    // ── The splice an editor applies ────────────────────────────────────────

    /// <summary>
    /// An editor applies <see cref="StructuralEdit.TextChange"/> rather than assigning the whole document,
    /// so the two must agree exactly — a splice that does not reproduce NewText would corrupt the buffer
    /// while every other test still passed.
    /// </summary>
    [TestMethod]
    [DataRow("replace")]
    [DataRow("delete")]
    [DataRow("rename")]
    [DataRow("signature")]
    [DataRow("body")]
    [DataRow("append")]
    [DataRow("insert_after")]
    [DataRow("doc")]
    [DataRow("substitute")]
    public void TheSplice_ReproducesTheNewTextExactly(string op)
    {
        (StructuralEdit.Op Kind, string Path, string? Text, StructuralEdit.Options? Options, string? To) plan = op switch
        {
            "replace"      => (StructuralEdit.Op.Replace,     "T:Widget/M:Add", "public void Add(int n)\n{\n    _count += n;\n}", null, null),
            "delete"       => (StructuralEdit.Op.Delete,      "T:Widget/M:Add", null, null, null),
            "rename"       => (StructuralEdit.Op.Rename,      "T:Widget/M:Add", null, null, "Plus"),
            "signature"    => (StructuralEdit.Op.Signature,   "T:Widget/M:Add", "public void Add(long n)", null, null),
            "body"         => (StructuralEdit.Op.Body,        "T:Widget/M:Add", "{\n    _count += n;\n}", null, null),
            "append"       => (StructuralEdit.Op.Append,      "T:Widget",       "public void Reset() { }", null, null),
            "insert_after" => (StructuralEdit.Op.InsertAfter, "T:Widget/M:Add", "public int Get() => _count;", null, null),
            "doc"          => (StructuralEdit.Op.Doc,         "T:Widget/M:Add", "/// <summary>Adds.</summary>", null, null),
            _              => (StructuralEdit.Op.Substitute,  "T:Widget/M:Add", "_count += n * 2;",
                               new StructuralEdit.Options(Find: "_count += n;"), null),
        };

        var result = StructuralEdit.Apply("c-sharp", Widget, plan.Path, plan.Kind, plan.Text,
                                          plan.Options, plan.To);
        Assert.IsTrue(result.Ok, result.Message);
        Assert.IsNotNull(result.Change);

        var c       = result.Change!;
        var spliced = Widget[..c.Offset] + c.Inserted + Widget[(c.Offset + c.Length)..];
        Assert.AreEqual(result.NewText, spliced, $"the {op} splice does not reproduce the edited text");
    }

    [TestMethod]
    public void TheSplice_IsMinimal_SoAnEditorsUndoAndCaretSurvive()
    {
        var result = StructuralEdit.Apply("c-sharp", Widget, "T:Widget/M:Add", StructuralEdit.Op.Rename,
                                          null, null, "Plus");

        Assert.IsTrue(result.Ok, result.Message);
        Assert.AreEqual(3, result.Change!.Length, "renaming Add should remove exactly 'Add'");
        Assert.AreEqual("Plus", result.Change.Inserted);
    }

    // ── Addressing by path alone ────────────────────────────────────────────

    [TestMethod]
    public void APathThatNamesNothing_IsRefusedWithSomethingActionable()
    {
        var result = StructuralEdit.Apply("c-sharp", Widget, "T:Widget/M:Nope", StructuralEdit.Op.Delete, null);

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "does not name a declaration");
    }
}
