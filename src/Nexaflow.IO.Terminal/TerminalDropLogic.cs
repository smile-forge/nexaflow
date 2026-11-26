using Nexaflow.Interaction;
using System.Buffers;

namespace Nexaflow.IO.Terminal;

/// <summary>
/// Shared helper for turning a file drop into the words of a command line: each path one word, space-joined, suitable for
/// insertion into the AI bar or onto a terminal — quoted for cmd by default (<see cref="Quote"/>), or as a terminal's shell
/// quotes it. Used by every terminal feature's chat drop handler and by the terminal's own drop target, so the rule lives
/// in one place.
/// <para>
/// A name is the dragging application's choice and a web page can make it, so it is never left to mean anything to
/// the shell: not a second command, not a variable, not Enter.
/// </para>
/// </summary>
public static class TerminalDropLogic
{
    /// <summary>What <c>cmd /?</c> lists as needing quotes, and what redirects or expands.</summary>
    private static readonly SearchValues<char> CmdSpecials = SearchValues.Create("&()[]{}^=;!'+,`~<>|%@");

    /// <summary>The dropped files as cmd names them — what the AI bar takes on a page that runs no shell.</summary>
    public static string? BuildInsertText(IDragPayload payload) => BuildInsertText(payload, Quote);

    /// <summary>The dropped files, each made one word of a shell's command line by <paramref name="word"/>; null when
    /// nothing dropped is a file that can be typed.</summary>
    public static string? BuildInsertText(IDragPayload payload, Func<string, string> word)
    {
        var paths = payload.FilesOnDisk().Where(CanBeTyped).ToList();
        return paths.Count == 0 ? null : string.Join(" ", paths.Select(word));
    }

    public static string Quote(string path)
    {
        if (!path.Any(char.IsWhiteSpace) && !path.AsSpan().ContainsAny(CmdSpecials)) return path;

        // Quotes do not stop cmd expanding %NAME%, so each percent of a possible pair is stepped out of them and escaped.
        var inside = path.IndexOf('%') != path.LastIndexOf('%') ? path.Replace("%", "\"^%\"") : path;
        return $"\"{inside}\"";
    }

    /// <summary>No Windows path holds a quote or a control character, and typed, either would be the shell's.</summary>
    private static bool CanBeTyped(string path) => !path.Any(c => c == '"' || char.IsControl(c));
}
