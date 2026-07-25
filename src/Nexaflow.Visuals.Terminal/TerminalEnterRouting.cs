namespace Nexaflow.Visuals.Terminal;

/// <summary>What pressing Enter in a terminal does with the line that was typed.</summary>
public enum TerminalEnterAction
{
    /// <summary>The cursor row isn't a prompt — a program is reading input, so Enter is just forwarded.</summary>
    ForwardToProgram,

    /// <summary>A recognised shell command: run it in the shell and record it in history.</summary>
    RunAsCommand,

    /// <summary>Natural language: wipe it off the shell line and hand it to the assistant.</summary>
    AskTheAssistant,
}

/// <summary>
/// The decision Enter makes, and what it implies. <see cref="Text"/> is the trimmed line the action
/// applies to; <see cref="ClearShellLine"/> asks the caller to send an Esc first, so the words the user
/// typed at the assistant don't stay sitting on the shell's input line.
/// </summary>
public readonly record struct TerminalEnterDecision(TerminalEnterAction Action, string Text, bool ClearShellLine);

/// <summary>
/// Where a typed line goes when Enter is pressed — the terminal's single most consequential decision, and
/// the one the user never explicitly makes. Getting it wrong either sends English to cmd.exe or quietly
/// hands a command someone meant to run to a language model.
/// <para>
/// Split out of the view-model because the inputs are simple (is the cursor on a prompt, what was typed,
/// what does this shell call a built-in) but reaching them through the view-model means owning a live
/// pseudo-console. The classification heuristic itself lives in <see cref="CommandClassifier"/>; this is
/// the routing around it.
/// </para>
/// </summary>
public static class TerminalEnterRouting
{
    /// <param name="promptInput">The text typed after the prompt, or <c>null</c> when the cursor row is
    /// not a prompt at all — which is how "a program is running" reaches here.</param>
    public static TerminalEnterDecision Decide(string? promptInput, IReadOnlySet<string> shellBuiltins)
    {
        // Mid-program: the line belongs to whatever is reading stdin. Classifying it would be actively
        // wrong — "y" to a confirmation prompt is not a shell command, and neither is a password.
        if (promptInput is null)
            return new(TerminalEnterAction.ForwardToProgram, string.Empty, ClearShellLine: false);

        var trimmed = promptInput.Trim();

        if (CommandClassifier.IsCommand(promptInput, shellBuiltins))
            return new(TerminalEnterAction.RunAsCommand, trimmed, ClearShellLine: false);

        // An empty line at a prompt is a bare Enter: nothing to clear, nothing worth asking about.
        return new(TerminalEnterAction.AskTheAssistant, trimmed, ClearShellLine: promptInput.Length > 0);
    }
}
