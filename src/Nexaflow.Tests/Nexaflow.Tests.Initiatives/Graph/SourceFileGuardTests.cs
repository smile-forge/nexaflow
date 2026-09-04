using System;
using System.IO;
using System.Text;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Graph;

/// <summary>
/// The last thing between an edit and the file: whether these bytes are ones anybody could have meant.
/// <para>
/// This is here because it happened. Two NUL bytes went into a committed source file where spaces belonged,
/// put there by a script whose payload a shell had mangled. Everything downstream accepted them — inside a
/// string literal a NUL is valid C#, so it compiled, and it survived review because it is invisible. It was
/// found weeks later because <c>grep</c> said "Binary file matches".
/// </para>
/// <para>
/// Every other check in this path asks whether the edit is the one that was asked for. This one asks a
/// different question, which is why it is worth its own tests: a control character written on purpose is an
/// escape sequence, which is ordinary text, so a raw one arriving in a payload is a mangling and not an
/// intention. The characters here are written as escapes for that exact reason — a test file carrying real
/// ones would be the thing it is testing for.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("A write-path guard for the editing tools — infrastructure, not a product-tree node.")]
public class SourceFileGuardTests
{
    /// <summary>Built from code points rather than written as literals or escapes: a test file holding a
    /// real control character would be an instance of the thing under test.</summary>
    private static readonly string Nul = ((char)0).ToString();
    private static readonly string FormFeed = ((char)12).ToString();

    private string _file = "";

    [TestInitialize]
    public void Setup() =>
        _file = Path.Combine(Path.GetTempPath(), "nfi-guard-" + Guid.NewGuid().ToString("N")[..8] + ".cs");

    [TestCleanup]
    public void Cleanup()
    {
        try { File.Delete(_file); } catch (IOException) { }
    }

    private void Given(string text) => File.WriteAllText(_file, text, new UTF8Encoding(false));

    private string? Write(string from, string to) =>
        SourceFile.WriteIfUnchanged(_file, from, to, new UTF8Encoding(false));

    [TestMethod]
    public void AnOrdinaryEdit_IsWritten()
    {
        Given("var a = 1;\n");

        Assert.IsNull(Write("var a = 1;\n", "var a = 2;\n"));
        Assert.AreEqual("var a = 2;\n", File.ReadAllText(_file));
    }

    /// <summary>The exact shape of the damage: a NUL where a space belonged, inside a string, where it
    /// parses.</summary>
    [TestMethod]
    public void AnEditSmugglingANulByte_IsRefused_AndNothingIsWritten()
    {
        const string before = "var k = $\"{a} {b}\";\n";
        Given(before);

        var refused = Write(before, "var k = $\"{a}" + Nul + "{b}\";\n");

        Assert.IsNotNull(refused);
        StringAssert.Contains(refused!, "NUL");
        Assert.AreEqual(before, File.ReadAllText(_file), "a refused write must leave the file alone");
    }

    /// <summary>The line matters more than the offset: it is what the reader will go and look at.</summary>
    [TestMethod]
    public void TheRefusal_SaysWhichLine()
    {
        const string before = "one\ntwo\nthree\n";
        Given(before);

        var refused = Write(before, "one\ntwo\nthr" + Nul + "ee\n");

        Assert.IsNotNull(refused);
        StringAssert.Contains(refused!, "line 3");
    }

    /// <summary>Judged by kind, not by count — a file already using a character keeps working, which is what
    /// stops this being a rule people have to work around.</summary>
    [TestMethod]
    public void AControlCharacterTheFileAlreadyUses_IsLeftAlone()
    {
        var before = "page one" + FormFeed + "page two\n";
        Given(before);

        Assert.IsNull(Write(before, before + "page three" + FormFeed + "\n"));
    }

    /// <summary>Tabs, newlines and carriage returns are how text is shaped, not smuggled bytes — refusing
    /// them would refuse every edit there is.</summary>
    [TestMethod]
    public void TabsAndNewlines_AreNotSuspect()
    {
        const string before = "a\n";
        Given(before);

        Assert.IsNull(Write(before, "if (x)\r\n{\r\n\tGo();\r\n}\r\n"));
    }
}
