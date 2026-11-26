using Nexaflow.IO.Terminal;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Terminal;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// What Enter does in the console: whatever is typed there is the shell's. All that is decided is whether it is a
/// command at a prompt, which history keeps, or input to a running program, which it must not — that may be a password.
/// </summary>
[TestClass]
[CoversNode("console-enter-classify")]
public class TerminalEnterRoutingTests
{
    [TestMethod]
    public void WhileAProgramIsReadingInput_TheLineIsJustForwarded_AndKeptNowhere()
    {
        var d = TerminalEnterRouting.Decide(null);

        Assert.AreEqual(TerminalEnterAction.ForwardToProgram, d.Action);
        Assert.AreEqual(string.Empty, d.Text);
    }

    [TestMethod]
    [DataRow("dir")]
    [DataRow("apt install nginx")]
    [DataRow("mysql -pHunter2")]
    [DataRow("what is in this folder")]
    [DataRow("why did that build fail")]
    public void AtAPromptEveryLineIsTheShells_WhateverItReadsLike(string typed)
    {
        var d = TerminalEnterRouting.Decide(typed);

        Assert.AreEqual(TerminalEnterAction.RunAsCommand, d.Action, typed);
        Assert.AreEqual(typed, d.Text);
    }

    [TestMethod]
    public void ACommandIsTrimmedBeforeItReachesHistory()
        => Assert.AreEqual("dir", TerminalEnterRouting.Decide("   dir   ").Text, "history should not hold one command at four indents");

    [TestMethod]
    public void ABareEnterAtAPromptIsTheShells_AndNothingEmptyReachesHistory()
    {
        var d = TerminalEnterRouting.Decide("");

        Assert.AreEqual(TerminalEnterAction.RunAsCommand, d.Action);
        Assert.AreEqual("", d.Text);
    }
}
