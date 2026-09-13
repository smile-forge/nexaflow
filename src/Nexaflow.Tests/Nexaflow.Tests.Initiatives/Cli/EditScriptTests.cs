using System;
using System.IO;
using System.Linq;
using Nexaflow.Services.Initiatives.Cli;
using Nexaflow.Services.Initiatives.Cli.Daemon;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Cli;

/// <summary>
/// The script form of <c>graph edit</c>: a command per line with its text in blocks beneath. A block is the reason the
/// format exists — text that never passes through a shell — so the tests are mostly about where one starts and stops.
/// </summary>
[TestClass]
[CoversNode("graph-edit-plan")]
public class EditScriptTests
{
    private static EditScript.Command[] Parse(string script)
    {
        Assert.IsTrue(EditScript.TryParse(script, out var commands, out var error), error);
        return [.. commands];
    }

    private static string Refusal(string script)
    {
        Assert.IsFalse(EditScript.TryParse(script, out _, out var error));
        return error;
    }

    [TestMethod]
    public void ABlockBecomesTheCommandsText_WithNothingInItInterpreted()
    {
        var commands = Parse(
            "# comment\n" +
            "substitute code:src/A.cs#T:A/M:Run\n" +
            "<<< find\n" +
            "    Say(\"it's \\n\");\n" +
            ">>>\n" +
            "<<< text\n" +
            "// a comment\n" +
            "\n" +
            "Say($\"{x}\");\n" +
            ">>>\n" +
            "rename code:src/A.cs#T:A/M:Old --to New\n");

        Assert.AreEqual(2, commands.Length);
        Assert.AreEqual(2, commands[0].Line);
        CollectionAssert.AreEqual(
            new[] { "substitute", "code:src/A.cs#T:A/M:Run", "--find", "    Say(\"it's \\n\");", "--text",
                    "// a comment\n\nSay($\"{x}\");" },
            commands[0].Args);
        CollectionAssert.AreEqual(new[] { "rename", "code:src/A.cs#T:A/M:Old", "--to", "New" }, commands[1].Args);
    }

    [TestMethod]
    public void ABlockHoldingTheCloser_ClosesAtAWordOfItsOwn()
    {
        var commands = Parse("create notes.md\n<<< text END\nquoted\n>>>\nstill quoted\nEND\n");

        Assert.AreEqual("quoted\n>>>\nstill quoted", commands.Single().Args[^1]);
    }

    [TestMethod]
    public void CrlfScripts_ReadTheSame()
    {
        var commands = Parse("create a.md\r\n<<<\r\none\r\ntwo\r\n>>>\r\n");

        Assert.AreEqual("one\ntwo", commands.Single().Args[^1]);
    }

    [TestMethod]
    public void AnUnclosedBlock_IsRefusedNamingTheCommand() =>
        StringAssert.Contains(Refusal("delete code:a#T:A\ncreate b.md\n<<<\nnever closed\n"), "line 2");

    [TestMethod]
    public void ABlockWithNoCommandAboveIt_IsRefused() =>
        StringAssert.Contains(Refusal("<<<\norphan\n>>>\n"), "no");

    [TestMethod]
    public void TextGivenTwice_IsRefused() =>
        StringAssert.Contains(Refusal("replace code:a#T:A --text x\n<<<\ny\n>>>\n"), "twice");

    [TestMethod]
    public void AnEmptyScript_IsRefused() => StringAssert.Contains(Refusal("# nothing\n\n"), "no commands");

    /// <summary>End to end through the verb: every command is checked before any file is written.</summary>
    [TestMethod]
    public void AScriptThatFailsPartWay_WritesNothing()
    {
        var root = Directory.CreateTempSubdirectory("nexa-script-").FullName;
        try
        {
            new ProductStore(root).Initialize("P");
            var script = Path.Combine(root, "plan.edits");
            File.WriteAllText(script, "create first.md\n<<<\none\n>>>\ncreate first.md\n<<<\nagain\n>>>\n");

            using var scope = RequestScope.Begin(new StringWriter(), new StringWriter(), new RequestContext(root));
            var code = Program.Execute(["graph", "edit", "script", "--file", script, root]);

            Assert.AreNotEqual(0, code, "the second create finds the first one's file and refuses");
            Assert.IsFalse(File.Exists(Path.Combine(root, "first.md")), "and the first is not written either");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [TestMethod]
    public void ARunWideSwitchOnOneCommand_IsRefused()
    {
        var root = Directory.CreateTempSubdirectory("nexa-script-").FullName;
        try
        {
            new ProductStore(root).Initialize("P");
            var script = Path.Combine(root, "plan.edits");
            File.WriteAllText(script, "create a.md --dry-run\n<<<\nx\n>>>\n");

            // Refused at parse, before the create is planned - so with nothing else wrong, nothing is written.
            using var scope = RequestScope.Begin(new StringWriter(), new StringWriter(), new RequestContext(root));
            Assert.AreNotEqual(0, Program.Execute(["graph", "edit", "script", "--file", script, root]));
            Assert.IsFalse(File.Exists(Path.Combine(root, "a.md")));
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }
}
