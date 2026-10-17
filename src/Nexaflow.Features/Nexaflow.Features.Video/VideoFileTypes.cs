using System;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.Video;

/// <summary>
/// The video formats the player is handed as files of its own — what the generation tools make, and the containers libvlc
/// plays that the file-type map routes here. Mirrors <c>ImageFileTypes</c> and <c>AudioFileTypes</c>.
/// </summary>
public static class VideoFileTypes
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".webm", ".mkv", ".mov", ".avi", ".wmv", ".mpg", ".mpeg", ".ts", ".flv",
    };

    public static bool IsVideo(string path) => Extensions.Contains(Path.GetExtension(path));
}
