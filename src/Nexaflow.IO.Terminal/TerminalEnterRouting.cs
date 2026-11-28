namespace Nexaflow.IO.Terminal;

/// <summary>What pressing Enter in a terminal does with the line that was typed. Either way the Enter is the shell's.</summary>
public enum TerminalEnterAction
{
    /// <summary>The cursor isn't at a prompt — a program is reading input, so Enter is just forwarded.</summary>
    ForwardToProgram,

    /// <summary>At a prompt: the shell runs the line, and it is kept in history.</summary>
    RunAsCommand,
}

/// <summary>The decision Enter makes: what it is, and the trimmed line that goes into history.</summary>
public readonly record struct TerminalEnterDecision(TerminalEnterAction Action, string Text);

/// <summary>
/// What Enter does in a terminal. What is typed in the console is the console's, whatever it reads like: no line is
/// judged here, taken off the prompt or shown to a model — that is the AI bar's (<see cref="CommandClassifier"/>). All
/// Enter decides is whether the line is a command at a prompt, kept in history, or a running program's input, which is
/// not: a "y" to a confirmation is no command, and neither is a password.
/// <para>
/// Split out of the view-model because the input is simple (what was typed at a prompt, if at one) but reaching it
/// through the view-model means owning a live pseudo-console.
/// </para>
/// </summary>
public static class TerminalEnterRouting
{
    /// <param name="promptInput">The text typed after the prompt, or <c>null</c> when the cursor is not at a prompt —
    /// which is how "a program is running" reaches here.</param>
    public static TerminalEnterDecision Decide(string? promptInput)
        => promptInput is null
            ? new(TerminalEnterAction.ForwardToProgram, string.Empty)
            : new(TerminalEnterAction.RunAsCommand, promptInput.Trim());
}
