using System.Windows;

namespace Nexaflow.Visuals.Terminal;

/// <summary>
/// Shared helper for turning a file drop into text: quoted, space-joined paths suitable for insertion
/// into the AI bar or onto the console. Used by every terminal feature's chat drop handler and by the
/// console output drop target, so the quoting rule lives in one place.
/// </summary>
public static class TerminalDropLogic
{
    public static string? BuildInsertText(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return null;
        return string.Join(" ", paths.Select(Quote));
    }

    public static string Quote(string path) => path.Contains(' ') ? $"\"{path}\"" : path;
}
