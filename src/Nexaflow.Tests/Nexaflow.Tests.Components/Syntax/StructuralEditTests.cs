using System.Linq;
using Nexaflow.Syntax;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Syntax;

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

    // ── Several edits to one file, in a row ─────────────────────────────────

    private const string Overloaded = """
        public class W
        {
            public void Add(int a)
            {
                Log("ONE");
            }

            public void Add(int a, int b)
            {
                Log("TWO");
            }

            public void Add(int a, int b, int c)
            {
                Log("THREE");
            }
        }
        """;

    /// <summary>
    /// Deleting one of three overloads renumbers the survivors, so the path that named the deleted one
    /// resolves again immediately — and the guard that asked "does this path still resolve" called a correct
    /// delete half-applied. What has to be true is that one fewer declaration carries the name.
    /// </summary>
    [TestMethod]
    public void DeletingAnOverload_IsNotMistakenForAFailedDelete()
    {
        var result = StructuralEdit.Apply("c-sharp", Overloaded, "T:W/M:Add#1", StructuralEdit.Op.Delete, null);

        Assert.IsTrue(result.Ok, result.Message);
        Assert.IsFalse(result.NewText!.Contains("\"TWO\""), "the two-arg overload should be gone");
        StringAssert.Contains(result.NewText, "\"ONE\"");
        StringAssert.Contains(result.NewText, "\"THREE\"");
    }

    /// <summary>
    /// The one way a sequence of edits to one file goes wrong without anything refusing: `#N` is a position,
    /// so after a delete a caller reusing its earlier listing aims at a different declaration — and the name
    /// check still passes, because the overloads share a name.
    /// </summary>
    [TestMethod]
    public void DeletingAnOverload_WarnsThatTheOtherPathsHaveMoved()
    {
        var result = StructuralEdit.Apply("c-sharp", Overloaded, "T:W/M:Add#1", StructuralEdit.Op.Delete, null);

        Assert.IsTrue(result.Ok, result.Message);
        Assert.IsTrue(result.Notes.Any(n => n.Contains("renumbers")),
            "a caller reusing its earlier listing has to be told the positions moved");
    }

    [TestMethod]
    public void AStalePathAfterARename_IsRefusedRatherThanGuessedAt()
    {
        var renamed = StructuralEdit.Apply("c-sharp", Widget, "T:Widget/M:Add", StructuralEdit.Op.Rename,
                                           null, null, "Plus");
        Assert.IsTrue(renamed.Ok, renamed.Message);

        // Recovery is by NAME, and the name is exactly what a rename changed — so there is nothing to
        // recover to, and the old path must not quietly find something else.
        var stale = StructuralEdit.Apply("c-sharp", renamed.NewText!, "T:Widget/M:Add",
                                         StructuralEdit.Op.Delete, null);
        Assert.IsFalse(stale.Ok, "the old path must not silently find something else");
        StringAssert.Contains(stale.Message, "Nothing named 'Add'");
    }

    /// <summary>`expect` is the hard guard for the renumbering case — the one thing that still refuses when
    /// the path resolves to the wrong declaration.</summary>
    [TestMethod]
    public void Expect_CatchesAPathThatNowMeansADifferentOverload()
    {
        var afterDelete = StructuralEdit.Apply("c-sharp", Overloaded, "T:W/M:Add#1",
                                               StructuralEdit.Op.Delete, null).NewText!;

        // 'Add#1' now names the three-arg overload. Without expect this succeeds against the wrong one.
        var unpinned = StructuralEdit.Apply("c-sharp", afterDelete, "T:W/M:Add#1", StructuralEdit.Op.Substitute,
            "Log(\"EDITED\");", new StructuralEdit.Options(Find: "Log(\"THREE\");"));
        Assert.IsTrue(unpinned.Ok, "nothing can detect this from the path alone — hence the warning note");

        var pinned = StructuralEdit.Apply("c-sharp", afterDelete, "T:W/M:Add#1", StructuralEdit.Op.Substitute,
            "Log(\"EDITED\");", new StructuralEdit.Options(Find: "Log(\"THREE\");", Expect: "int a, int b)"));
        Assert.IsFalse(pinned.Ok, "pinning to what the caller believed it was editing must refuse");
    }

    /// <summary>Unrelated declarations keep their paths, so a run of edits from one listing is fine as long
    /// as nothing overloaded is added or removed.</summary>
    [TestMethod]
    public void SeveralEditsInARow_EachLandWhereTheCallerMeant()
    {
        var text = Widget;

        foreach (var step in new Func<string, StructuralEdit.Result>[]
                 {
                     s => StructuralEdit.Apply("c-sharp", s, "T:Widget/M:Add", StructuralEdit.Op.Rename, null, null, "Plus"),
                     s => StructuralEdit.Apply("c-sharp", s, "T:Widget", StructuralEdit.Op.Append, "public int Total;"),
                     s => StructuralEdit.Apply("c-sharp", s, "T:Widget/P:Count", StructuralEdit.Op.Doc, "/// <summary>How many.</summary>"),
                     s => StructuralEdit.AddImport("c-sharp", s, "using System.Linq;"),
                 })
        {
            var result = step(text);
            Assert.IsTrue(result.Ok, result.Message);
            text = result.NewText!;
        }

        AssertLine(text, "    public void Plus(int n)");
        AssertLine(text, "    public int Total;");
        AssertLine(text, "    /// <summary>How many.</summary>");
        AssertLine(text, "using System.Linq;");
        Assert.IsTrue(new DeclarationAnchors().ParsesCleanly("c-sharp", text), "and the file still parses");
    }

    // ── A record that has fallen behind the file ────────────────────────────

    private const string Nested = """
        public class Outer
        {
            public class Inner
            {
                public void Work()
                {
                    Log("here");
                }
            }
        }
        """;

    private static StructuralEdit.Result Edit(string src, string path, string name) =>
        StructuralEdit.Apply("c-sharp", src, path, name, StructuralEdit.Op.Substitute, "Log(\"EDITED\");",
                             new StructuralEdit.Options(Find: "Log(\"here\");"));

    /// <summary>
    /// The record an edit arrives with was built from a checkout that is not this working tree, and
    /// refreshing it takes about a minute and a half. Refusing on a moved path would make every caller
    /// believe the tool cannot be trusted between rebuilds. The NAME is the durable half, so the declaration
    /// is re-found by that and the edit goes ahead.
    /// </summary>
    [TestMethod]
    [DataRow("T:Outer/M:Work",  "a declaration since nested inside another type")]
    [DataRow("T:Widget/M:Work", "a container since renamed")]
    public void AStalePath_IsRecoveredByName_RatherThanRefused(string stalePath, string why)
    {
        var result = Edit(Nested, stalePath, "Work");

        Assert.IsTrue(result.Ok, $"{why}: {result.Message}");
        StringAssert.Contains(result.NewText!, "Log(\"EDITED\");");
        Assert.IsTrue(result.Notes.Any(n => n.Contains("re-found")),
            "the caller should be told the path it gave was not the one used");
    }

    [TestMethod]
    public void RecoveryStops_WhenTheNameIsGoneToo()
    {
        var result = Edit(Nested, "T:Outer/M:Gone", "Gone");

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "Nothing named 'Gone'");
    }

    /// <summary>Recovering by name must never become guessing between same-named declarations.</summary>
    [TestMethod]
    public void RecoveryRefusesToChooseBetweenOverloads()
    {
        const string overloaded = """
            public class C
            {
                public void Work(int a) { Log("one"); }
                public void Work(int a, int b) { Log("two"); }
            }
            """;

        var result = StructuralEdit.Apply("c-sharp", overloaded, "T:Gone/M:Work", "Work",
                                          StructuralEdit.Op.Delete, null);

        Assert.IsFalse(result.Ok, "two candidates is a question for the caller, not a coin toss");
        StringAssert.Contains(result.Message, "T:C/M:Work#0");
        StringAssert.Contains(result.Message, "T:C/M:Work#1");
    }

    /// <summary>The editor's form has no name to fall back on, so it takes one from the path's last segment
    /// — which is what the caller meant even when the rest of the path has moved on.</summary>
    [TestMethod]
    public void APathOnlyCaller_AlsoRecoversFromItsOwnStaleListing()
    {
        var result = StructuralEdit.Apply("c-sharp", Nested, "T:Outer/M:Work", StructuralEdit.Op.Substitute,
            "Log(\"EDITED\");", new StructuralEdit.Options(Find: "Log(\"here\");"));

        Assert.IsTrue(result.Ok, result.Message);
        StringAssert.Contains(result.NewText!, "Log(\"EDITED\");");
    }

    // ── File scope, for what is not inside a declaration ────────────────────

    /// <summary>
    /// A namespace statement is in no declaration, so declaration scope has nothing to offer it — and the
    /// answer to "rename the namespace this file declares" should not be a hand edit.
    /// </summary>
    [TestMethod]
    public void SubstituteInFile_ReachesWhatIsNotInsideADeclaration()
    {
        const string file = "namespace Old.Name;\n\npublic class C\n{\n    public void M() { }\n}\n";

        var result = StructuralEdit.SubstituteInFile("c-sharp", file, "namespace New.Name;",
                                                     new StructuralEdit.Options(Find: "namespace Old.Name;"));

        Assert.IsTrue(result.Ok, result.Message);
        AssertLine(result.NewText!, "namespace New.Name;");
        AssertLine(result.NewText!, "public class C");
    }

    [TestMethod]
    public void SubstituteInFile_KeepsTheSameGuarantees()
    {
        const string file = "namespace N;\n\npublic class C\n{\n    void A() { Log(); }\n    void B() { Log(); }\n}\n";

        var ambiguous = StructuralEdit.SubstituteInFile("c-sharp", file, "Trace();",
                                                        new StructuralEdit.Options(Find: "Log();"));
        Assert.IsFalse(ambiguous.Ok, "wider scope must not mean looser rules");
        StringAssert.Contains(ambiguous.Message, "occurs 2 times");

        var unparseable = StructuralEdit.SubstituteInFile("c-sharp", file, "public class C {",
                                                          new StructuralEdit.Options(Find: "public class C"));
        Assert.IsFalse(unparseable.Ok, "the result still has to parse");
    }

    // ── Delete leaves the spacing it found ──────────────────────────────────

    private const string ThreeMembers = """
        public class W
        {
            public void A() { }

            public void B() { }

            public void C() { }
        }
        """;

    /// <summary>
    /// A deletion takes one adjacent blank line with it, or every one widens the gap it leaves. The blank
    /// above is the separator the declaration owns; the fallback to the one below is what the first member
    /// of a body needs, where there is nothing above it but the brace.
    /// </summary>
    [TestMethod]
    [DataRow("T:W/M:A", "A")]
    [DataRow("T:W/M:B", "B")]
    [DataRow("T:W/M:C", "C")]
    public void Delete_LeavesExactlyOneBlankLineBetweenTheSurvivors(string path, string gone)
    {
        var result = StructuralEdit.Apply("c-sharp", ThreeMembers, path, StructuralEdit.Op.Delete, null);
        Assert.IsTrue(result.Ok, result.Message);

        var lines = SourceText.Of(result.NewText!).Lines;
        Assert.IsFalse(lines.Any(l => l.Contains($"void {gone}(")), $"{gone} should be gone");

        Assert.AreEqual(1, lines.Count(l => l.Trim().Length == 0),
            "two members left means exactly one blank line between them — none stranded against a brace:\n  "
            + string.Join("\n  ", lines));
    }

    [TestMethod]
    public void Delete_OfTheOnlyMember_LeavesAnEmptyBody()
    {
        var result = StructuralEdit.Apply("c-sharp", "public class W\n{\n    public void A() { }\n}\n",
                                          "T:W/M:A", StructuralEdit.Op.Delete, null);

        Assert.IsTrue(result.Ok, result.Message);
        Assert.AreEqual("public class W\n{\n}\n", result.NewText);
    }

    // ── Reported from a real editing session ────────────────────────────────

    private const string Documented = """
        public class Work
        {
            /// <summary>The original doc.</summary>
            public void Run(int n)
            {
                Log(n);
            }

            public void Other()
            {
                Log(0);
            }
        }
        """;

    /// <summary>
    /// The tool's one promise about whitespace is that the caller does not handle it. A literal substitution
    /// used to be an undocumented exception, inserting the replacement byte-for-byte — so a fragment written
    /// flush-left, exactly as every other verb asks for, produced flush-left code inside an indented body.
    /// It compiles, so only reading the file afterwards catches it.
    /// </summary>
    [TestMethod]
    public void Substitute_IndentsItsReplacementLikeEveryOtherVerb()
    {
        var text = Applied(StructuralEdit.Apply("c-sharp", Documented, "T:Work/M:Run",
            StructuralEdit.Op.Substitute, "if (n > 0)\n{\n    Paint(n);\n}",
            new StructuralEdit.Options(Find: "Log(n);")));

        AssertLine(text, "        if (n > 0)");
        AssertLine(text, "        {");
        AssertLine(text, "            Paint(n);");
        AssertLine(text, "        }");
    }

    /// <summary>When the search text swallowed the line's indentation, the replacement has to supply it
    /// again — otherwise the same fix would land the statement at column 0.</summary>
    [TestMethod]
    public void Substitute_IndentsWhenTheSearchTextIncludedTheIndentation()
    {
        var text = Applied(StructuralEdit.Apply("c-sharp", Documented, "T:Work/M:Run",
            StructuralEdit.Op.Substitute, "Paint(n);",
            new StructuralEdit.Options(Find: "        Log(n);")));

        AssertLine(text, "        Paint(n);");
    }

    /// <summary>
    /// "Keeps its doc comment" means you needn't supply one, not that you mustn't. A replacement that opens
    /// with its own doc used to be added below the old one, leaving two — which compiles, so it survives
    /// until someone reads it.
    /// </summary>
    [TestMethod]
    public void Replace_UsesTheReplacementsOwnDocInsteadOfKeepingBoth()
    {
        var result = StructuralEdit.Apply("c-sharp", Documented, "T:Work/M:Run", StructuralEdit.Op.Replace,
            "/// <summary>A new doc.</summary>\npublic void Run(int n)\n{\n    Log(n);\n}");

        var text = Applied(result);
        AssertLine(text, "    /// <summary>A new doc.</summary>");
        Assert.IsFalse(text.Contains("The original doc."), "the old doc should have been replaced, not kept");
        Assert.AreEqual(1, SourceText.Of(text).Lines.Count(l => l.Contains("<summary>")),
            "exactly one doc comment");
        Assert.IsTrue(result.Notes.Any(n => n.Contains("replaced the existing doc")));
    }

    [TestMethod]
    public void Replace_StillKeepsTheDocWhenTheReplacementHasNone()
    {
        var text = Applied(StructuralEdit.Apply("c-sharp", Documented, "T:Work/M:Run",
            StructuralEdit.Op.Replace, "public void Run(long n)\n{\n    Log(n);\n}"));

        AssertLine(text, "    /// <summary>The original doc.</summary>");
        AssertLine(text, "    public void Run(long n)");
    }

    /// <summary>
    /// A rename used to cost a full graph build before the renamed thing could be edited again. The edit
    /// re-resolves against the text in hand, so the new name is addressable immediately.
    /// </summary>
    [TestMethod]
    public void ARenamedDeclaration_IsEditableImmediatelyUnderItsNewName()
    {
        var renamed = StructuralEdit.Apply("c-sharp", Documented, "T:Work", StructuralEdit.Op.Rename,
                                           null, null, "Job");
        Assert.IsTrue(renamed.Ok, renamed.Message);

        var next = StructuralEdit.Apply("c-sharp", renamed.NewText!, "T:Job/M:Run",
            StructuralEdit.Op.Substitute, "Log(n * 2);", new StructuralEdit.Options(Find: "Log(n);"));

        Assert.IsTrue(next.Ok, $"editing the renamed type should not need a rebuild: {next.Message}");
        AssertLine(next.NewText!, "        Log(n * 2);");
    }

    /// <summary>
    /// The refusal used to name the very node the caller had passed — "it does occur in X" where X was X —
    /// because it ruled the searched declaration out by range, and a declaration's line span starts before
    /// its own anchor offset, so it never contained itself.
    /// </summary>
    [TestMethod]
    public void AMissNeverPointsBackAtTheDeclarationYouAsked()
    {
        var result = StructuralEdit.Apply("c-sharp", Documented, "T:Work/M:Run", StructuralEdit.Op.Substitute,
            "x", new StructuralEdit.Options(Find: "NotPresentAnywhere();"));

        Assert.IsFalse(result.Ok);
        Assert.IsFalse(result.Message.Contains("T:Work/M:Run"),
            $"it must not tell the caller to edit what they just named: {result.Message}");
    }

    [TestMethod]
    public void AMissStillPointsAtTheSiblingThatDoesHaveIt()
    {
        var result = StructuralEdit.Apply("c-sharp", Documented, "T:Work/M:Run", StructuralEdit.Op.Substitute,
            "x", new StructuralEdit.Options(Find: "Log(0);"));

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "T:Work/M:Other");
    }

    /// <summary>An AST path names a constructor after its type; <c>.ctor</c> is an IL habit worth one line
    /// of help rather than a bare "not declared".</summary>
    [TestMethod]
    public void GuessingTheIlNameForAConstructor_IsAnsweredWithTheRealOne()
    {
        const string withCtor = "public class Work\n{\n    public Work(int n) { }\n}\n";

        var wrong = StructuralEdit.Apply("c-sharp", withCtor, "T:Work/M:.ctor", StructuralEdit.Op.Delete, null);
        Assert.IsFalse(wrong.Ok);
        StringAssert.Contains(wrong.Message, "M:TypeName");

        Assert.IsTrue(StructuralEdit.Apply("c-sharp", withCtor, "T:Work/M:Work",
                                           StructuralEdit.Op.Delete, null).Ok,
            "and the real path works");
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
        StringAssert.Contains(result.Message, "Nothing named 'Nope'");
        StringAssert.Contains(result.Message, "List the declarations");
    }

    // ── Names that are not unique ───────────────────────────────────────────

    /// <summary>
    /// A constructor carries its type's name, so the after-the-edit check — which re-found the declaration by
    /// name — kept finding the CLASS and comparing its body against the constructor's. Both halves of the
    /// signature/body pair refused a correct edit, each blaming the other half, and the only way through was
    /// substitute. Overloads collide for the same reason, all of them answering to one name.
    /// </summary>
    private const string Colliding = """
        public class Thing
        {
            // a plain comment above the constructor
            public Thing(int n)
            {
                Count = n;
            }

            public int Add(int a) => a;

            public int Add(int a, int b) => a + b;

            public int Count { get; }
        }
        """;

    [TestMethod]
    public void Signature_OfAConstructor_IsNotRefusedBecauseItSharesItsTypesName()
    {
        var text = Applied(StructuralEdit.Apply("c-sharp", Colliding, "T:Thing/M:Thing",
                                                StructuralEdit.Op.Signature, "public Thing(long n)"));

        AssertLine(text, "    public Thing(long n)");
        AssertLine(text, "        Count = n;");
    }

    [TestMethod]
    public void Body_OfAConstructor_IsNotRefusedBecauseItSharesItsTypesName()
    {
        var text = Applied(StructuralEdit.Apply("c-sharp", Colliding, "T:Thing/M:Thing",
                                                StructuralEdit.Op.Body, "{\n    Count = n + 1;\n}"));

        AssertLine(text, "    public Thing(int n)");
        AssertLine(text, "        Count = n + 1;");
    }

    /// <summary>The overload actually named, not whichever one the name found first.</summary>
    [TestMethod]
    public void Signature_OfAnOverload_ChangesThatOverloadAndLeavesItsSiblingAlone()
    {
        var text = Applied(StructuralEdit.Apply("c-sharp", Colliding, "T:Thing/M:Add#1",
                                                StructuralEdit.Op.Signature, "public long Add(int a, int b)"));

        AssertLine(text, "    public long Add(int a, int b) => a + b;");
        AssertLine(text, "    public int Add(int a) => a;");
    }
}
