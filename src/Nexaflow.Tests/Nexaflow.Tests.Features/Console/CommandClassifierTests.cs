using System.Collections.Generic;
using Nexaflow.Visuals.Terminal;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// The Enter-time heuristic that splits "run this in the shell" from "ask the model". First token is a
/// built-in / PATH executable / existing file → command; otherwise → natural-language request.
/// </summary>
[TestClass]
public class CommandClassifierTests
{
    private static readonly IReadOnlySet<string> Builtins = CommandClassifier.CmdBuiltins;

    [DataTestMethod]
    [DataRow("echo hello")]
    [DataRow("dir")]
    [DataRow("cd ..")]
    [DataRow("set PATH")]
    [DataRow("EXIT")]            // case-insensitive
    [DataRow("git status")]     // first token resolves on PATH in this repo's environment
    public void Commands_AreClassifiedAsCommands(string line)
        => Assert.IsTrue(CommandClassifier.IsCommand(line, Builtins), $"'{line}' should be a command");

    [DataTestMethod]
    [DataRow("summarise the errors in this log")]
    [DataRow("explain what just happened")]
    [DataRow("qwzzx the temp folder")]   // first token is clearly not a program
    public void NaturalLanguage_IsNotACommand(string line)
        => Assert.IsFalse(CommandClassifier.IsCommand(line, Builtins), $"'{line}' should go to the model");

    [TestMethod]
    public void BlankLine_GoesToTheShell()
        => Assert.IsTrue(CommandClassifier.IsCommand("   ", Builtins));

    [TestMethod]
    public void NonexistentExplicitPath_IsNotACommand()
        => Assert.IsFalse(CommandClassifier.IsCommand(@"C:\does\not\exist\nope.exe arg", Builtins));
}
