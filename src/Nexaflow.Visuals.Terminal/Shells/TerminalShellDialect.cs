using Nexaflow.IO.Terminal;

namespace Nexaflow.Visuals.Terminal.Shells;

/// <summary>
/// How a terminal speaks to the shell it runs: the commands it phrases for it, the words it knows as its own, how its
/// folders compare, and how it says where it is — in a prompt read off the screen, or as a folder it reports (OSC 7).
/// Chosen with the shell (<see cref="TerminalShellRoute"/>), so one terminal runs cmd on this computer and a location's
/// own shell alike. Folders are named as the file system names them (<c>C:\work</c>, <c>::wsl-ubuntu\home\adam</c>); a
/// dialect turns them into the shell's own.
/// </summary>
public abstract class TerminalShellDialect
{
    /// <summary>What the assistant is told the shell is, so the commands it runs use its syntax.</summary>
    public abstract string Description { get; }

    /// <summary>The shell's built-in command names, which make a typed line a command rather than a question.</summary>
    public abstract IReadOnlySet<string> Builtins { get; }

    /// <summary>How the terminal's folders compare.</summary>
    public abstract StringComparison PathComparison { get; }

    /// <summary>What clears the line typed at the prompt, so words meant for the assistant do not stay on it.</summary>
    public abstract string ClearInputLine { get; }

    /// <summary>The command that moves the shell to <paramref name="folder"/>, or null when the shell cannot reach it.</summary>
    public abstract string? FormatCdCommand(string folder);

    /// <summary>The command that sets a variable in the running shell.</summary>
    public abstract string FormatSetCommand(string name, string value);

    /// <summary>The folder as the assistant reads it: as the shell names it, and as Nexaflow's own tools do where the two
    /// differ.</summary>
    public virtual string DescribeFolder(string folder) => folder;

    /// <summary>The prompt the screen row <paramref name="row"/> starts with, or null when it starts with none. Read for a
    /// shell that does not mark its prompts (OSC 133); one that does is followed by its marks.</summary>
    public virtual ShellPromptRow? ReadPrompt(string row) => null;

    /// <summary>A folder the shell reported (OSC 7), as the file system names it; null when it is none this terminal
    /// reaches.</summary>
    public virtual string? ToFolder(TerminalWorkingFolder reported) => null;
}

/// <summary>A prompt read off a screen row: the column typing starts at, the folder it shows when it shows one, and
/// whether nothing is typed after it yet.</summary>
public readonly record struct ShellPromptRow(int InputColumn, string? Folder, bool IsBare);
