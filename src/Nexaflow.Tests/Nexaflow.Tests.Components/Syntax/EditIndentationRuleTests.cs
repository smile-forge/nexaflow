using Nexaflow.Syntax;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Syntax;

/// <summary>
/// The indentation rule of a substitution, case by case, each asserted against the whole text the edit leaves. These tests
/// are its specification: the rule has been worked out again more than once, a different way each time, because what it
/// was lived only in the code. A change to how a substitution places whitespace changes a case here, by name.
/// <list type="bullet">
/// <item>In code, the tool handles whitespace: a find matches ignoring indentation, and text written flush-left lands
/// indented for where it goes.</item>
/// <item>A find that touches a string literal spanning lines is matched and written character for character. Whitespace
/// there is the string's value, so there is no right indentation to infer: the find must match it exactly, and the text
/// lands exactly as written. A find that matches only when indentation is ignored is refused, showing the lines with
/// their whitespace made visible.</item>
/// <item>A literal on one line is code: no line can start inside it, so placing lines never touches its value.</item>
/// <item>A file no grammar covers keeps the code rule: text lands at the margin of the line it is found on.</item>
/// <item>An edit of a whole declaration moves every line of it alike, a literal's lines included, so what is inside keeps
/// its shape.</item>
/// </list>
/// </summary>
[TestClass]
[CoversNode("syntax-structural-edit")]
public class EditIndentationRuleTests
{
    private const string Holder = """"
        public class Holder
        {
            public string Text = """
                first
                    second
                third
                """;
        }
        """";

    private static string Substituted(string source, string target, string find, string text, bool regex = false)
    {
        var result = StructuralEdit.Apply("c-sharp", source, target, StructuralEdit.Op.Substitute, text,
                                          new StructuralEdit.Options(Find: find, FindIsRegex: regex));
        Assert.IsTrue(result.Ok, result.Message);
        return Lf(result.NewText!);
    }

    private static string Lf(string text) => text.Replace("\r\n", "\n");

    [TestMethod]
    public void InALiteral_AFindWithItsIndentation_IsReplacedCharacterForCharacter()
    {
        var edited = Substituted(Holder, "T:Holder", "            second", "        second");

        Assert.AreEqual(Lf(""""
            public class Holder
            {
                public string Text = """
                    first
                    second
                    third
                    """;
            }
            """"), edited);
    }

    [TestMethod]
    public void InALiteral_AFindPartWayAlongALine_KeepsWhatIsBeforeIt_AndTheTextLandsAsWritten()
    {
        var edited = Substituted(Holder, "T:Holder", "second", "second\n            fourth");

        Assert.AreEqual(Lf(""""
            public class Holder
            {
                public string Text = """
                    first
                        second
                        fourth
                    third
                    """;
            }
            """"), edited);
    }

    [TestMethod]
    public void InALiteral_AFindThatMatchesOnlyIgnoringIndentation_IsRefused_ShowingTheWhitespace()
    {
        var result = StructuralEdit.Apply("c-sharp", Holder, "T:Holder", StructuralEdit.Op.Substitute, "x",
                                          new StructuralEdit.Options(Find: "second\nthird"));

        Assert.IsFalse(result.Ok, "nothing is guessed where whitespace is the string's value");
        StringAssert.Contains(result.Message, "character for character");
        StringAssert.Contains(result.Message, "············second", "the lines as they are, every space shown");
        StringAssert.Contains(result.Message, "········third");
    }

    [TestMethod]
    public void InAVerbatimLiteral_TheTextLandsAsWritten()
    {
        const string verbatim = """
            public class Holder
            {
                public string Text = @"first
                second";
            }
            """;

        var edited = Substituted(verbatim, "T:Holder", "    second", "  second");

        Assert.AreEqual(Lf("""
            public class Holder
            {
                public string Text = @"first
              second";
            }
            """), edited);
    }

    [TestMethod]
    public void InAnInterpolatedRawLiteral_TheTextLandsAsWritten()
    {
        const string interpolated = """"
            public class Holder
            {
                public string Text(int x) => $"""
                    first {x}
                        second
                    """;
            }
            """";

        var edited = Substituted(interpolated, "T:Holder", "            second", "        second");

        Assert.AreEqual(Lf(""""
            public class Holder
            {
                public string Text(int x) => $"""
                    first {x}
                    second
                    """;
            }
            """"), edited);
    }

    [TestMethod]
    public void InALiteral_ARegexReplacement_IsItsExpansion_AsItStands()
    {
        var edited = Substituted(Holder, "T:Holder", "( +)second", "$1second\n$1fourth", regex: true);

        Assert.AreEqual(Lf(""""
            public class Holder
            {
                public string Text = """
                    first
                        second
                        fourth
                    third
                    """;
            }
            """"), edited);
    }

    [TestMethod]
    public void AFindReachingIntoALiteralFromCode_IsMatchedAndWrittenCharacterForCharacter()
    {
        var edited = Substituted(Holder, "T:Holder", "Text = \"\"\"\n        first", "Text = \"\"\"\n          first");

        Assert.AreEqual(Lf(""""
            public class Holder
            {
                public string Text = """
                      first
                        second
                    third
                    """;
            }
            """"), edited);
    }

    [TestMethod]
    public void InCode_TextWrittenFlushLeft_LandsIndentedForWhereItGoes()
    {
        const string counter = """
            public class Counter
            {
                private int _count;

                public void Add(int n)
                {
                    _count += n;
                }
            }
            """;

        var edited = Substituted(counter, "T:Counter/M:Add", "_count += n;", "_count += n;\n_count *= 2;");

        StringAssert.Contains(edited, "        _count += n;\n        _count *= 2;\n");
    }

    [TestMethod]
    public void ALiteralOnOneLine_IsCode_SoTextWrittenFlushLeftLandsIndented()
    {
        const string greeter = """
            public class Greeter
            {
                public void Run()
                {
                    Say("hi");
                }

                private static void Say(string s) { }
            }
            """;

        var edited = Substituted(greeter, "T:Greeter/M:Run", "Say(\"hi\");", "Say(\"hi\");\nSay(\"there\");");

        StringAssert.Contains(edited, "        Say(\"hi\");\n        Say(\"there\");\n");
    }

    [TestMethod]
    public void InAFileNoGrammarCovers_TextLandsAtTheMarginOfTheLineItIsFoundOn()
    {
        const string markdown = "- item\n\n    ```\n    code line\n    ```\n";

        var result = StructuralEdit.SubstituteInText(markdown, "code line\nsecond line", new StructuralEdit.Options(Find: "code line"));

        Assert.IsTrue(result.Ok, result.Message);
        Assert.AreEqual("- item\n\n    ```\n    code line\n    second line\n    ```\n", Lf(result.NewText!));
    }

    [TestMethod]
    public void AWholeDeclarationEdit_MovesEveryLineAlike_SoALiteralKeepsItsShape()
    {
        const string outer = """"
            public class Outer
            {
                public class Holder
                {
                    public string Text = """
                        first
                        """;
                }
            }
            """";

        var result = StructuralEdit.Apply("c-sharp", outer, "T:Outer/T:Holder", StructuralEdit.Op.Replace, """"
            public class Holder
            {
                public string Text = """
                    changed
                        deeper
                    """;
            }
            """", new StructuralEdit.Options());

        Assert.IsTrue(result.Ok, result.Message);
        Assert.AreEqual(Lf(""""
            public class Outer
            {
                public class Holder
                {
                    public string Text = """
                        changed
                            deeper
                        """;
                }
            }
            """"), Lf(result.NewText!));
    }
}
