using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexaflow.Features.Images;

/// <summary>
/// The image formats the viewer understands, in one place: the extension set used to filter a
/// selection and the matching glob list used by the folder actions' content check.
/// </summary>
public static class ImageFileTypes
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tiff", ".tif", ".webp"
    };

    /// <summary>Globs (one per extension) for <see cref="Common.IFolderAction.ContainsFileGlobs"/>.</summary>
    public static readonly string[] Globs = Extensions.Select(e => "*" + e).ToArray();

    public static bool IsImage(string path) => Extensions.Contains(Path.GetExtension(path));

    /// <summary>Top-level image files in <paramref name="folderPath"/>, ordered by name. Empty on error.</summary>
    public static List<string> EnumerateImages(string folderPath)
    {
        try
        {
            return Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
                .Where(IsImage)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
