using System.IO;
using System.Security;
using System.Windows.Media;
using Microsoft.Win32;

namespace Nexaflow.Visuals.Common.Shell;

/// <summary>Where <see cref="ShellFileIcons"/> gets its icons and its decisions from — the shell, or a test's stand-in.</summary>
internal interface IShellIconSource
{
    /// <summary>The icon every file ending <paramref name="extension"/> shares, or every plain folder's.</summary>
    ImageSource? TypeIcon(string extension, bool isFolder, ShellIconSize size);

    /// <summary>The icon of the one file or folder at <paramref name="path"/>. Reads the disk.</summary>
    ImageSource? PathIcon(string path, ShellIconSize size);

    /// <summary>Whether each file ending <paramref name="extension"/> draws an icon of its own — a program, a shortcut, an icon file.</summary>
    bool DrawsItsOwnIcon(string extension);

    /// <summary>Whether the folder at <paramref name="path"/> is customised (a desktop.ini), so it may wear an icon of its own. Reads the disk.</summary>
    bool IsCustomizedFolder(string path);
}

/// <summary>The shell itself, and the registry it decides from.</summary>
internal sealed class SystemShellIconSource : IShellIconSource
{
    public ImageSource? TypeIcon(string extension, bool isFolder, ShellIconSize size) => ShellInterop.TypeIcon(extension, isFolder, size);

    public ImageSource? PathIcon(string path, ShellIconSize size) => ShellInterop.PathIcon(path, size);

    /// <summary>
    /// As Explorer decides it: the extension's class names the file itself as its icon (<c>DefaultIcon</c> of <c>%1</c>), or
    /// registers an icon handler that draws each file — on the extension, its ProgID, or its system file association.
    /// </summary>
    public bool DrawsItsOwnIcon(string extension)
    {
        if (extension.Length == 0) return false;
        try
        {
            return DrawsEachItem(extension)
                || (ProgIdOf(extension) is { Length: > 0 } progId && DrawsEachItem(progId))
                || HasIconHandler($@"SystemFileAssociations\{extension}");
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>A desktop.ini only takes effect on a folder marked read-only or system, which is how Explorer tells them apart.</summary>
    public bool IsCustomizedFolder(string path)
    {
        try { return (File.GetAttributes(path) & (FileAttributes.ReadOnly | FileAttributes.System)) != 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool DrawsEachItem(string className) => DefaultIconIsTheFile(className) || HasIconHandler(className);

    private static bool DefaultIconIsTheFile(string className)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(className + @"\DefaultIcon");
        return key?.GetValue(null) is string icon && icon.Trim().Trim('"') == "%1";
    }

    private static bool HasIconHandler(string className)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(className + @"\shellex\IconHandler");
        return key is not null;
    }

    private static string? ProgIdOf(string extension)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(extension);
        return key?.GetValue(null) as string;
    }
}
