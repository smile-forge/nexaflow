using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Terminal;

namespace Nexaflow.Visuals.Terminal.Shells;

/// <summary>
/// A POSIX shell on a location — bash or zsh in a WSL distro, a shell over SSH: <c>cd -- '…'</c> and
/// <c>export NAME='…'</c> with every value one single-quoted word, Ctrl+U to clear the line, and no prompt to read — the
/// shell reports its folder (OSC 7) and marks its prompts (OSC 133). The terminal names the location's folders as the
/// file system does (<c>::wsl-ubuntu\home\adam</c>) and hands the shell its own (<c>/home/adam</c>).
/// </summary>
public sealed class PosixShellDialect : TerminalShellDialect
{
    private readonly string _root;
    private readonly string _description;
    private readonly StringComparison _comparison;

    /// <param name="locationRoot">The location's root as the file system names it (<c>::wsl-ubuntu</c>).</param>
    /// <param name="locationName">The location as people know it, for the assistant.</param>
    /// <param name="pathComparison">How names compare on the location.</param>
    public PosixShellDialect(string locationRoot, string locationName, StringComparison pathComparison)
    {
        _root        = locationRoot.TrimEnd('\\', '/');
        _comparison  = pathComparison;
        _description = $"a POSIX shell on {locationName} (sh / bash syntax such as `ls`, `cd`, `export` — not cmd or " +
                       "PowerShell), where paths are the location's own, such as `/home`";
    }

    /// <summary>The dialect of the shell on the location holding <paramref name="folder"/>, named by its label and its
    /// address (<c>Ubuntu (wsl://Ubuntu/)</c>).</summary>
    public static PosixShellDialect ForLocation(IVirtualFileSystem files, string folder)
    {
        var root = files.GetBreadcrumbs(folder)[0].Path;
        return new PosixShellDialect(root, $"{files.GetDisplayName(root)} ({files.GetDisplayPath(root)})",
                                     files.GetNameComparison(root));
    }

    public override string Description => _description;

    public override IReadOnlySet<string> Builtins => CommandClassifier.PosixBuiltins;

    public override StringComparison PathComparison => _comparison;

    public override string ClearInputLine => "\x15";   // Ctrl+U: readline's and zle's kill to the line's start

    public override string? FormatCdCommand(string folder)
        => ShellPath(folder) is { } path ? $"cd -- {Quote(path)}" : null;

    public override string FormatSetCommand(string name, string value) => $"export {name}={Quote(value)}";

    public override string DescribeFolder(string folder)
        => ShellPath(folder) is { } path ? $"{path} ({folder})" : folder;

    /// <summary>The reported path on the location, whatever host the shell names — the shell runs there.</summary>
    public override string? ToFolder(TerminalWorkingFolder reported)
    {
        if (!reported.Path.StartsWith('/')) return null;
        var remainder = LocationPath.ToRemainder(reported.Path);
        return remainder.Length == 0 ? _root : $"{_root}\\{remainder}";
    }

    /// <summary>The location's own path for <paramref name="folder"/> (<c>/home/adam</c>), or null when the folder is not
    /// on this location.</summary>
    public string? ShellPath(string folder)
    {
        // The root names the location by its id, which is matched as the file system matches ids.
        if (!folder.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) return null;
        if (folder.Length == _root.Length) return "/";
        return folder[_root.Length] is '\\' or '/' ? LocationPath.ToProviderPath(folder[(_root.Length + 1)..]) : null;
    }

    /// <summary><paramref name="word"/> as one POSIX shell word: single-quoted, each <c>'</c> in it closed, escaped and
    /// reopened (<c>'\''</c>), so nothing in it is expanded.</summary>
    public static string Quote(string word) => "'" + word.Replace("'", @"'\''") + "'";
}
