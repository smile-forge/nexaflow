namespace Nexaflow.IO.Terminal;

/// <summary>Where a shell is in its prompt cycle, as it marks it (OSC 133).</summary>
public enum ShellMarkKind
{
    /// <summary><c>A</c>: the prompt starts here.</summary>
    PromptStart,

    /// <summary><c>B</c>: the prompt ends and what is typed starts here.</summary>
    InputStart,

    /// <summary><c>C</c>: the command was read, and its output starts here.</summary>
    OutputStart,

    /// <summary><c>D</c>: the command ended, with its exit code when the shell gave one.</summary>
    CommandEnd,
}

/// <summary>
/// A mark a shell put in its output (OSC 133), and where the cursor stood when it came. <see cref="Line"/> counts every
/// line the screen has shown from 0 — <see cref="TerminalScreen.TopLine"/> is screen row 0's — so it stays true as lines
/// scroll off the top.
/// </summary>
public readonly record struct ShellMark(ShellMarkKind Kind, int? ExitCode, long Line, int Column);
