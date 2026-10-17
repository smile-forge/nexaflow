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

    /// <summary>The file a typed name makes in <paramref name="folder"/>: as typed where it ends in one of
    /// <paramref name="extensions"/>, else with <paramref name="chosen"/> — the first of them where none is — added.</summary>
    internal static string? Named(string? folder, string? typed, IReadOnlyList<string>? extensions, string? chosen = null)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;

        var name = typed.Trim();
        if (folder is not { Length: > 0 } && !Path.IsPathRooted(name)) return null;

        // Path.Combine hands back a rooted second argument whole, which is what makes a typed path work.
        var joined = folder is { Length: > 0 } ? Path.Combine(folder, name) : name;

        return extensions is { Count: > 0 }
               && !extensions.Any(ext => joined.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                   ? joined + (chosen ?? extensions[0])
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
