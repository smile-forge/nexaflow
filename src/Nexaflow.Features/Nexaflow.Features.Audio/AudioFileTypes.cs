using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.IO.Common;

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

    /// <summary>Top-level audio files in <paramref name="folderPath"/>, read through <paramref name="vfs"/> so a folder in an
    /// archive or on a location queues like one on disk, ordered by name. Empty on error.</summary>
    public static async Task<List<string>> EnumerateAudioAsync(IVirtualFileSystem vfs, string folderPath, CancellationToken ct = default)
    {
        try
        {
            var names = new List<string>();
            await foreach (var entry in vfs.EnumerateAsync(folderPath, ct))
                if (!entry.IsDirectory && IsAudio(entry.Name)) names.Add(entry.Name);
            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(n => Path.Combine(folderPath, n)).ToList();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return [];
        }
    }
}
