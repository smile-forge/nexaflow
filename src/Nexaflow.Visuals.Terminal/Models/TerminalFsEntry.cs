using System.IO;

namespace Nexaflow.Visuals.Terminal.Models;

/// <summary>One entry in the terminal's Files panel — a folder or file in the current directory. Folders
/// can be double-clicked to navigate; entries are a drag source (their path is dropped onto the bar/console).</summary>
public sealed class TerminalFsEntry
{
    public TerminalFsEntry(string fullPath, bool isDirectory)
    {
        FullPath    = fullPath;
        IsDirectory = isDirectory;
        var name    = Path.GetFileName(fullPath.TrimEnd('\\', '/'));
        Name        = string.IsNullOrEmpty(name) ? fullPath : name;
    }

    public string FullPath    { get; }
    public bool   IsDirectory { get; }
    public string Name        { get; }

    /// <summary>Folder / document glyph (emoji so no icon-font dependency).</summary>
    public string Icon => IsDirectory ? "📁" : "📄";
}
