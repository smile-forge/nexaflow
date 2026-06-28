using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexaflow.Features.Audio;

/// <summary>
/// The audio formats the player understands, in one place: the extension set used to filter a
/// selection / build a folder queue, and the matching glob list used by the folder action's content
/// check. Mirrors <c>ImageFileTypes</c>.
/// </summary>
public static class AudioFileTypes
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma", ".ogg", ".opus",
    };

    /// <summary>Globs (one per extension) for <see cref="Common.IFolderAction.ContainsFileGlobs"/>.</summary>
    public static readonly string[] Globs = Extensions.Select(e => "*" + e).ToArray();

    public static bool IsAudio(string path) => Extensions.Contains(Path.GetExtension(path));

    /// <summary>Top-level audio files in <paramref name="folderPath"/>, ordered by name. Empty on error.</summary>
    public static List<string> EnumerateAudio(string folderPath)
    {
        try
        {
            return Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
                .Where(IsAudio)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
