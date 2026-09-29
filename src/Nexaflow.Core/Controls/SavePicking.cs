using Nexaflow.Core.Services;
using Nexaflow.IO.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexaflow.Core.Controls;

/// <summary>Where a Save As dialog opens, and what the name typed into it comes to.</summary>
internal static class SavePicking
{
    private static readonly string[] Pictures =
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".ico"];

    /// <summary>The folder to open on: where the caller suggested when a file can land there, else the folder
    /// this kind of file belongs in. A document opened from Program Files would otherwise put the dialog on a
    /// folder nothing can be saved into.</summary>
    internal static string Start(string? near, IReadOnlyList<string>? extensions)
    {
        if (Folder(near) is { } asked && SaveRoom.TakesASave(asked)) return asked;

        var home = Home(extensions);
        return SaveRoom.TakesASave(home) ? home : KnownFolderService.DocumentsPath;
    }

    /// <summary>The file a folder and a typed name come to, or nothing where there is no name to build on. The
    /// extension the caller asked for is added unless the name already carries one it allows, and a reader who
    /// types a path of their own gets it, wherever the tree happens to be standing.</summary>
    internal static string? Named(string? folder, string? typed, IReadOnlyList<string>? extensions)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;

        var name = typed.Trim();
        if (folder is not { Length: > 0 } && !Path.IsPathRooted(name)) return null;

        // Path.Combine hands back a rooted second argument whole, which is what makes a typed path work.
        var joined = folder is { Length: > 0 } ? Path.Combine(folder, name) : name;

        return extensions is { Count: > 0 }
               && !extensions.Any(ext => joined.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                   ? joined + extensions[0]
                   : joined;
    }

    private static string? Folder(string? near)
    {
        if (string.IsNullOrWhiteSpace(near)) return null;

        try
        {
            var full = Path.GetFullPath(near);
            return Directory.Exists(full) ? full : Path.GetDirectoryName(full);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string Home(IReadOnlyList<string>? extensions) =>
        extensions is { Count: > 0 } && Pictures.Contains(extensions[0], StringComparer.OrdinalIgnoreCase)
            ? KnownFolderService.PicturesPath
            : KnownFolderService.DocumentsPath;
}
