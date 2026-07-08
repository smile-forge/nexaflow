using System;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.Font;

/// <summary>
/// The font-file formats the viewer opens, in one place. Raw sfnt (<c>.ttf/.otf/.ttc</c>) load
/// directly in WPF; <c>.woff</c> is decoded to sfnt first (see <c>Fonts/FontSourceResolver</c>).
/// Mirrors <c>AudioFileTypes</c>. Keep this in sync with the <c>/font</c> block in
/// <c>default-filemap.json</c>.
/// </summary>
public static class FontFileTypes
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ttf", ".otf", ".ttc", ".woff",
    };

    /// <summary>Extensions for the shell file-picker filter.</summary>
    public static readonly string[] PickerExtensions = [".ttf", ".otf", ".ttc", ".woff"];

    public static bool IsFont(string path) => Extensions.Contains(Path.GetExtension(path));
}
