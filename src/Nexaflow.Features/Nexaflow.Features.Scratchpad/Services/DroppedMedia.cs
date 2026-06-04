using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Scratchpad.Services;

/// <summary>
/// Shared helpers for turning dropped/pasted content into post-it media and markdown. Used by both the
/// canvas (which creates new notes) and an existing note (which inserts blocks), so the image-detection,
/// attachment-copying and file-link formatting live in one place.
/// </summary>
public static class DroppedMedia
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".ico"
    };

    /// <summary>True when the dropped/pasted data is a single bare http(s) URL (e.g. dragged or copied
    /// from a browser address bar), returning it in <paramref name="url"/>.</summary>
    public static bool TryGetSingleUrl(IDataObject data, out string url)
    {
        url = string.Empty;
        var text = (data.GetDataPresent(DataFormats.UnicodeText) ? data.GetData(DataFormats.UnicodeText)
                  : data.GetDataPresent(DataFormats.Text)        ? data.GetData(DataFormats.Text)
                  : null) as string;
        if (string.IsNullOrWhiteSpace(text)) return false;

        text = text.Trim();
        if (text.Any(char.IsWhiteSpace)) return false;   // multiple tokens → not a bare URL
        if (!Uri.TryCreate(text, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;

        url = text;
        return true;
    }

    /// <summary>True when <paramref name="path"/> has an image extension we embed rather than link.</summary>
    public static bool IsImageFile(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Copies an image into <paramref name="dir"/> under a unique name and returns that file name
    /// (relative, for <c>![](name)</c>), or null on failure. The unique suffix avoids collisions when
    /// several images land in the same note's attachment folder.
    /// </summary>
    public static string? CopyImageInto(string sourcePath, string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var name = Path.GetFileNameWithoutExtension(sourcePath) + "_" + UniqueSuffix() + Path.GetExtension(sourcePath);
            File.Copy(sourcePath, Path.Combine(dir, name), overwrite: false);
            return name;
        }
        catch { return null; }
    }

    /// <summary>Encodes a bitmap (e.g. a browser image drag) as PNG into <paramref name="dir"/>;
    /// returns the file name or null on failure.</summary>
    public static string? SaveBitmapInto(BitmapSource bitmap, string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var name = "image_" + UniqueSuffix() + ".png";
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var fs = File.Create(Path.Combine(dir, name));
            encoder.Save(fs);
            return name;
        }
        catch { return null; }
    }

    /// <summary>A clickable markdown link to a file or folder: 📄/📁 + name → its <c>file:</c> URI.</summary>
    public static string FileLinkMarkdown(string path)
    {
        var icon = Directory.Exists(path) ? "📁" : "📄";
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name)) name = path;
        return $"[{icon} {name}]({new Uri(path).AbsoluteUri})";
    }

    private static string UniqueSuffix() => Guid.NewGuid().ToString("N")[..6];
}
