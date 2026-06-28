using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Audio.FileActions;

/// <summary>
/// Opens a directory of audio files as a play queue. Offered for folders where at least 30% of the
/// top-level files are audio (<see cref="AudioFileTypes.Globs"/>). Mirrors the image folder actions.
/// </summary>
public sealed class AudioFolderAction : IFolderAction, ICacheable
{
    private readonly IShellServices _shell;

    public AudioFolderAction(IShellServices shell) => _shell = shell;

    public bool   IsDestructive         => false;
    public bool   SupportsMultipleFiles => false;
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;
    public bool   AppliesToRoot         => true;
    public bool   AppliesToDrives       => false;

    public string[]? ContainsFileGlobs              => AudioFileTypes.Globs;
    public int       MinimumFileGlobMatchPercentage => 30;

    public string Icon        => "🎵";
    public string DisplayName => "Play Folder";

    public bool PerformAction(string folderPath)
    {
        var queue = AudioFileTypes.EnumerateAudio(folderPath);
        if (queue.Count == 0) return false;

        _shell.OpenTab("Audio", new Dictionary<string, string>
        {
            ["paths"] = string.Join('|', queue),
            ["index"] = "0",
            ["autoplay"] = "true",   // "Play folder" starts immediately
        });
        return true;
    }

    public bool PerformAction(IEnumerable<string> folderPaths)
    {
        var first = folderPaths.FirstOrDefault();
        return first is not null && PerformAction(first);
    }
}
