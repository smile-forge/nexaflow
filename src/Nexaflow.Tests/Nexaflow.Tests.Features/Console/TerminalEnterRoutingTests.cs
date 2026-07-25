using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Terminal;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// Where a line goes when Enter is pressed.
/// <para>
/// This is the terminal's one genuinely consequential decision and the user never gets to make it
/// explicitly — they type and press Enter, and something decides. Both ways of being wrong are bad in a
/// way no error message covers: English sent to <c>cmd.exe</c> produces a confusing failure, and a command
/// someone meant to run being handed to a language model instead is worse. The third case is the one that
/// is easy to forget entirely — while a program is reading input there is no prompt, and the line belongs
/// to that program whatever it looks like.
/// </para>
/// </summary>
[TestClass]
[CoversNode("console-enter-classify")]
public class TerminalEnterRoutingTests
{
    private static readonly IReadOnlySet<string> Builtins = CommandClassifier.CmdBuiltins;

    private static TerminalEnterDecision AtPrompt(string typed) => TerminalEnterRouting.Decide(typed, Builtins);

    // ── Mid-program ───────────────────────────────────────────────────────────

    [TestMethod]
    public void WhileAProgramIsReadingInput_TheLineIsJustForwarded()
    {
        // No prompt on the cursor row is how "something is running" reaches the decision.
        var d = TerminalEnterRouting.Decide(null, Builtins);

        Assert.AreEqual(TerminalEnterAction.ForwardToProgram, d.Action);
        Assert.IsFalse(d.ClearShellLine, "there is no shell line to clear — the program owns it");
    }

    [TestMethod]
    public void AConfirmationAnswerIsNotClassified()
    {
        // "y" answering a prompt would classify as neither a command nor much of a question; forwarding
        // it unread is the only correct handling, which is exactly what a null input line gets.
        Assert.AreEqual(TerminalEnterAction.ForwardToProgram,
                        TerminalEnterRouting.Decide(null, Builtins).Action);
    }

    // ── At a prompt: commands ─────────────────────────────────────────────────

    // Shell built-ins only: whether a bare program name resolves on PATH depends on the machine, and that
    // half of the question belongs to CommandClassifierTests, not to the routing around it.
    [TestMethod]
    [DataRow("dir")]
    [DataRow("cd ..")]
    [DataRow("echo hello")]
    [DataRow("type notes.txt")]
    public void ARecognisedCommandRuns(string typed)
    {
        var d = AtPrompt(typed);

        Assert.AreEqual(TerminalEnterAction.RunAsCommand, d.Action, typed);
        Assert.AreEqual(typed, d.Text);
        Assert.IsFalse(d.ClearShellLine, "the line is what runs — wiping it would erase the command");
    }

    [TestMethod]
    public void ACommandIsTrimmedBeforeItReachesHistory()
    {
        var d = AtPrompt("   dir   ");

        Assert.AreEqual(TerminalEnterAction.RunAsCommand, d.Action);
        Assert.AreEqual("dir", d.Text, "history should not accumulate the same command at four indents");
    }

    // ── At a prompt: natural language ─────────────────────────────────────────

    [TestMethod]
    [DataRow("what is in this folder")]
    [DataRow("why did that build fail")]
    public void NaturalLanguageGoesToTheAssistant_AndIsWipedOffTheShellLine(string typed)
    {
        var d = AtPrompt(typed);

        Assert.AreEqual(TerminalEnterAction.AskTheAssistant, d.Action, typed);
        Assert.AreEqual(typed, d.Text);
        Assert.IsTrue(d.ClearShellLine,
                      "otherwise the question stays sitting on the prompt, and the next Enter runs it");
    }

    [TestMethod]
    public void ABareEnterAtAPromptGoesToTheShell_NotToTheAssistant()
    {
        // Pressing Enter on an empty prompt is how you get a blank line in a terminal. Treating it as an
        // empty question would send the model nothing and swallow the keystroke.
        var d = AtPrompt("");

        Assert.AreEqual(TerminalEnterAction.RunAsCommand, d.Action);
        Assert.AreEqual("", d.Text, "and nothing empty reaches the history list");
        Assert.IsFalse(d.ClearShellLine);
    }
}
