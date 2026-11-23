using System.Windows;

namespace Nexaflow.Visuals.Terminal;

/// <summary>Files dropped onto the console or the AI bar, as the words of a command line that name them.</summary>
public static class TerminalDropLogic
{
    /// <summary>The dropped files as cmd names them — what the AI bar takes on a page that runs no shell.</summary>
    public static string? BuildInsertText(IDataObject data) => BuildInsertText(data, Quote);

    /// <summary>The dropped files, each made one word of a shell's command line by <paramref name="word"/>; null when
    /// nothing dropped is a file.</summary>
    public static string? BuildInsertText(IDataObject data, Func<string, string> word)
    {
        if (data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return null;
        return string.Join(" ", paths.Select(word));
    }

    /// <summary>A path as one word of a cmd command line.</summary>
    public static string Quote(string path) => path.Contains(' ') ? $"\"{path}\"" : path;
}
