using System.IO;

namespace Nexaflow.Visuals.Terminal.Models;

/// <summary>One entry in the terminal's Files panel — a folder or file in the shell's folder. Folders can be double-clicked
/// to navigate; entries are a drag source (their path is dropped onto the bar/console).</summary>
/// <param name="fullPath">The entry's path, as the file system names it.</param>
/// <param name="name">Its name, as its folder holds it.</param>
/// <param name="shownPath">Its path as the shell names it, for the tooltip.</param>
public sealed class TerminalFsEntry(string fullPath, string name, string shownPath, bool isDirectory)
{
    public string FullPath    { get; } = fullPath;
    public bool   IsDirectory { get; } = isDirectory;
    public string Name        { get; } = name;
    public string ShownPath   { get; } = shownPath;

    /// <summary>Folder / document glyph (emoji so no icon-font dependency).</summary>
    public string Icon => IsDirectory ? "📁" : "📄";
}
