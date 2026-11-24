using System.Text;
using System.Text.RegularExpressions;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Visuals.Terminal.Shells;

/// <summary>
/// A POSIX shell on a location — bash or zsh in a WSL distro, a shell over SSH: <c>cd -- '…'</c> and
/// <c>export NAME='…'</c> with every value one single-quoted word, Ctrl+E and Ctrl+U to clear the line, and no prompt to read — the
/// shell marks its prompts (OSC 133) and its host says where it is. The terminal names the location's folders as the
/// file system does (<c>::wsl-ubuntu\home\adam</c>) and hands the shell its own (<c>/home/adam</c>).
/// </summary>
public sealed partial class PosixShellDialect : TerminalShellDialect
{
    // The lines a variable kept for new shells goes between, so it is found and changed again rather than added twice.
    private const string KeptStart = "# >>> Nexaflow: variables set from its Environment panel >>>";
    private const string KeptEnd   = "# <<< Nexaflow <<<";

    private readonly string _root;
    private readonly string _description;
    private readonly StringComparison _comparison;
    private readonly IVirtualFileSystem? _files;

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*\z")]
    private static partial Regex VariableName();

    /// <param name="locationRoot">The location's root as the file system names it (<c>::wsl-ubuntu</c>).</param>
    /// <param name="locationName">The location as people know it, for the assistant.</param>
    /// <param name="pathComparison">How names compare on the location.</param>
    /// <param name="files">Names a file dropped by the Windows folder showing the location as the location does; without it
    /// only the location's own paths are.</param>
    public PosixShellDialect(string locationRoot, string locationName, StringComparison pathComparison, IVirtualFileSystem? files = null)
    {
        _root        = locationRoot.TrimEnd('\\', '/');
        _comparison  = pathComparison;
        _files       = files;
        _description = $"a POSIX shell on {locationName} (sh / bash syntax such as `ls`, `cd`, `export` — not cmd or " +
                       "PowerShell), where paths are the location's own, such as `/home`";
    }

    /// <summary>The dialect of the shell on the location holding <paramref name="folder"/>, named by its label and its
    /// address (<c>Ubuntu (wsl://Ubuntu/)</c>).</summary>
    public static PosixShellDialect ForLocation(IVirtualFileSystem files, string folder)
    {
        var root = files.GetBreadcrumbs(folder)[0].Path;
        return new PosixShellDialect(root, $"{files.GetDisplayName(root)} ({files.GetDisplayPath(root)})",
                                     files.GetNameComparison(root), files);
    }

    public override string Description => _description;

    public override IReadOnlySet<string> Builtins => CommandClassifier.PosixBuiltins;

    public override StringComparison PathComparison => _comparison;

    public override string ClearInputLine => "\x05\x15";   // Ctrl+E then Ctrl+U: to the line's end, then kill to its start

    // Quotes hold a name from the shell, not from the tty: a control character typed is a key — Ctrl+C drops the line and
    // Enter runs what follows — so a folder named with one is never typed.
    public override string? FormatCdCommand(string folder)
        => ShellPath(folder) is { } path && !path.Any(char.IsControl) ? $"cd -- {ExactWord(path)}" : null;

    public override string FormatSetCommand(string name, string value) => $"export {name}={Quote(value)}";

    public override string DescribeFolder(string folder)
        => ShellPath(folder) is { } path ? $"{PosixNames.Shown(path)} ({folder})" : folder;

    /// <summary>The location's own path (<c>/home/adam</c>), a byte that is no part of a character shown as <c>\xHH</c>; a
    /// path on no part of it as it is.</summary>
    public override string ShowPath(string path) => ShellPath(path) is { } shown ? PosixNames.Shown(shown) : path;

    /// <summary>The location's own path for a file on it — named by the location or by the Windows folder showing it — as one
    /// single-quoted word; any other path is quoted as it is.</summary>
    public override string FormatPathWord(string path)
        => ShellPath(_files?.TryToVirtual(path) ?? path) is { } own ? ExactWord(own) : Quote(path);

    /// <summary>A POSIX name: a letter or underscore, then letters, digits and underscores.</summary>
    public override bool NamesVariable(string name) => VariableName().IsMatch(name);

    /// <summary>
    /// The variable as an <c>export</c> line between Nexaflow's own marks at the end of the startup file — changed in place
    /// when it is there already, the marks added when they are not — so the user's own lines are left as written. A value is
    /// kept on one line; one holding a line break is not kept.
    /// </summary>
    public override string? KeepInStartupFile(string text, string name, string value)
    {
        if (!NamesVariable(name) || value.Contains('\n') || value.Contains('\r')) return null;

        var line  = FormatSetCommand(name, value);
        var lines = text.Split('\n').ToList();
        int start = lines.IndexOf(KeptStart);
        int end   = start < 0 ? -1 : lines.IndexOf(KeptEnd, start + 1);
        if (end < 0)
        {
            var kept = new StringBuilder(text);
            if (text.Length > 0) kept.Append(text.EndsWith('\n') ? "\n" : "\n\n");
            return kept.Append(KeptStart).Append('\n').Append(line).Append('\n').Append(KeptEnd).Append('\n').ToString();
        }

        var prefix = $"export {name}=";
        int at = lines.FindIndex(start + 1, end - start - 1, kept => kept.StartsWith(prefix, StringComparison.Ordinal));
        if (at >= 0) lines[at] = line;
        else         lines.Insert(end, line);
        return string.Join('\n', lines);
    }

    /// <summary>The location's own path (<c>/srv/app</c>) as the file system names it.</summary>
    public override string? ToFolder(string hostFolder)
    {
        if (!hostFolder.StartsWith('/')) return null;
        var remainder = LocationPath.ToRemainder(hostFolder);
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

    /// <summary>A location's path as one shell word that is its exact bytes: single-quoted, or — where a name is no UTF-8,
    /// whose stray bytes no keystroke can type — the shell's own <c>printf %b</c> of an all-ASCII escape of them
    /// (<see cref="PosixNames.ForPrintf"/>).</summary>
    public static string ExactWord(string path)
        => PosixNames.HoldsStrayBytes(path) ? $"\"$(printf '%b' {Quote(PosixNames.ForPrintf(path))})\"" : Quote(path);

    /// <summary><paramref name="word"/> as one POSIX shell word: single-quoted, each <c>'</c> in it closed, escaped and
    /// reopened (<c>'\''</c>), so nothing in it is expanded.</summary>
    public static string Quote(string word) => "'" + word.Replace("'", @"'\''") + "'";
}
