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

    /// <summary>The shell's built-in command names.</summary>
    public abstract IReadOnlySet<string> Builtins { get; }

    /// <summary>Whether the shell runs something called <paramref name="name"/>, which is what lets a line in the AI bar be
    /// taken for a command.</summary>
    /// <param name="name">A bare name, never a path.</param>
    /// <param name="listed">The programs the shell's launcher listed
    /// (<see cref="Nexaflow.IO.Common.Terminal.ITerminalLauncher.ListCommandsAsync"/>); empty until it has, or when it cannot.</param>
    public virtual bool KnowsCommand(string name, IReadOnlySet<string> listed) => Builtins.Contains(name) || listed.Contains(name);

    /// <summary>How the terminal's folders compare.</summary>
    public abstract StringComparison PathComparison { get; }

    /// <summary>The keys that empty the line at the prompt wherever the cursor is in it, sent before a command from the AI
    /// bar, history or the assistant, so it runs as written and never joined onto what was half-typed.</summary>
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
