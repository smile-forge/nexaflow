using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Nexaflow.Interaction;
using Nexaflow.IO.Common;
using Nexaflow.Media;
using Nexaflow.Visuals.Common.Previews;

namespace Nexaflow.Features.Scratchpad.Services;

/// <summary>
/// Shared helpers for turning dropped/pasted content into post-it media and markdown. Used by both the
/// canvas (which creates new notes) and an existing note (which inserts blocks), so the image-detection,
/// attachment-copying and file-link formatting live in one place.
/// </summary>
[LocalDiskOnly("a picture or file dropped from Windows, which is on this disk, kept in the note's attachments in the app's config folder")]
public static partial class DroppedMedia
{
    private static readonly HashSet<string> ImageExtensions = new(DecodablePictures.Extensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>What a board and a note on it take.</summary>
    public static IReadOnlyList<string> Kinds { get; } = DragKinds.Shared;

    /// <summary>True when the dropped/pasted data is a single bare http(s) URL (e.g. dragged or copied
    /// from a browser address bar), returning it in <paramref name="url"/>.</summary>
    public static bool TryGetSingleUrl(IDragPayload data, out string url)
    {
        url = data.Link() ?? WebLinks.SingleAddress(data.Text()) ?? string.Empty;
        return url.Length > 0;
    }

    /// <summary>
    /// Copies a file or folder that arrived as content into <paramref name="dir"/> — it has no place of its own to be
    /// linked to — and returns where it is kept, or null on failure.
    /// </summary>
    public static string? KeepIn(string path, string dir)
    {
        try
        {
            var folder = Path.Combine(dir, UniqueSuffix());
            Directory.CreateDirectory(folder);
            var kept = Path.Combine(folder, Path.GetFileName(path));
            if (File.Exists(path))
            {
                File.Copy(path, kept);
                return kept;
            }

            Directory.CreateDirectory(kept);
            foreach (var sub in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(kept, Path.GetRelativePath(path, sub)));
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(kept, Path.GetRelativePath(path, file)));
            return kept;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { return null; }
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

    /// <summary>Writes a picture's PNG bytes (e.g. a browser image drag) into <paramref name="dir"/>;
    /// returns the file name or null on failure.</summary>
    public static string? SaveImageInto(byte[] png, string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var name = "image_" + UniqueSuffix() + ".png";
            File.WriteAllBytes(Path.Combine(dir, name), png);
            return name;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>A clickable markdown link to a file or folder: 📄/📁 + name → its <c>file:</c> URI.</summary>
    public static string FileLinkMarkdown(string path)
    {
        var icon = Directory.Exists(path) ? "📁" : "📄";
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name)) name = path;
        // A name is the dragging application's choice, so it may neither end the label nor the address.
        var label = LabelBreakers().Replace(name, @"\$0");
        var address = new Uri(path).AbsoluteUri.Replace("(", "%28").Replace(")", "%29");
        return $"[{icon} {label}]({address})";
    }

    /// <summary>A picture kept in the note's folder, embedded by its name: escaped, as the reader of it unescapes.</summary>
    public static string EmbedMarkdown(string fileName) => $"![]({Uri.EscapeDataString(fileName)})";

    [GeneratedRegex(@"[\\\[\]`<>]")]
    private static partial Regex LabelBreakers();

    private static string UniqueSuffix() => Guid.NewGuid().ToString("N")[..6];
}
