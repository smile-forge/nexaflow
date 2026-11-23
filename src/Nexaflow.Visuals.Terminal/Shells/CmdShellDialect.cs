using System.Text.RegularExpressions;
using Nexaflow.IO.Common;

namespace Nexaflow.Visuals.Terminal.Shells;

/// <summary>
/// cmd.exe on this computer: <c>cd /d "…"</c>, <c>set "NAME=value"</c>, Ctrl+End and Ctrl+Home to clear the line, and a prompt ending in
/// <c>&gt;</c> — <c>C:\work&gt;</c>, or <c>PS C:\work&gt;</c> while PowerShell runs inside it — that says it is ready. Its
/// folders are this disk's: one named by a mount (<c>::docs\…</c>) is moved to where the disk holds it.
/// </summary>
/// <param name="files">Resolves a mounted folder to the disk; without it only a path on the disk is reached.</param>
public sealed class CmdShellDialect(IVirtualFileSystem? files = null) : TerminalShellDialect
{
    private static readonly Regex PromptPathRegex = new(@"(?:PS )?[A-Za-z]:\\[^>]*>", RegexOptions.Compiled);

    public override string Description =>
        "the Windows cmd.exe command prompt (use cmd / batch syntax such as `dir`, not PowerShell cmdlets)";

    public override IReadOnlySet<string> Builtins => CommandClassifier.CmdBuiltins;

    public override StringComparison PathComparison => StringComparison.OrdinalIgnoreCase;

    // Ctrl+End then Ctrl+Home: delete to the line's end, then to its start. Not Esc, which a pseudo-console reads as
    // Alt with whatever key follows it.
    public override string ClearInputLine => "\x1b[1;5F\x1b[1;5H";

    public override bool KnowsCommand(string name, IReadOnlySet<string> listed)
        => base.KnowsCommand(name, listed) || CommandClassifier.IsOnPath(name);

    public override string? FormatCdCommand(string folder)
        => OnDisk(folder) is { } local ? $"cd /d \"{local}\"" : null;

    public override string FormatSetCommand(string name, string value) => $"set \"{name}={value}\"";

    /// <summary>A bare prompt is a row ending in <c>&gt;</c>; one with a drive path before that <c>&gt;</c> shows a folder.
    /// Typing starts past a drive-path prompt at the row's start.</summary>
    public override ShellPromptRow? ReadPrompt(string row)
    {
        var text  = row.TrimEnd();
        var match = PromptPathRegex.Match(text);
        bool startsWithPrompt = match.Success && match.Index == 0;
        bool bare             = text.Length > 0 && text[^1] == '>';
        if (!bare && !startsWithPrompt) return null;
        return new ShellPromptRow(startsWithPrompt ? match.Length : 0, bare && match.Success, bare);
    }

    /// <summary>A drive path, which is how this computer names the folder cmd is in.</summary>
    public override string? ToFolder(string hostFolder)
        => hostFolder is [var drive, ':', '\\', ..] && char.IsAsciiLetter(drive) ? hostFolder : null;

    /// <summary>The folder as this disk holds it; null for a mounted folder the disk does not hold whole.</summary>
    private string? OnDisk(string folder)
        => folder.StartsWith(VirtualMount.Prefix, StringComparison.Ordinal) ? files?.ResolveOnDisk(folder) : folder;
}
