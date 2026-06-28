using System.Collections.Generic;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Video.FileActions;

/// <summary>
/// Opens a video file in the embedded Video viewer (libvlc playback + codec info panel + scene strip +
/// fullscreen). Matches the "/video" experience, which the file-type map routes common video extensions
/// (and the shell's "Video" perceived type) to.
/// </summary>
public sealed class ShowVideoAction : IFileAction, ICacheable
{
    private readonly IShellServices _shellServices;

    public ShowVideoAction(IShellServices shellServices) => _shellServices = shellServices;

    public bool   IsDestructive         => false;
    public bool   SupportsMultipleFiles => false;
    public string Icon                  => "🎬";
    public string DisplayName           => "Play Video";
    public static string? StaticExperienceId => "/video";
    public string ExperienceId          => "/video";
    public string ExperienceDescription => "Embedded video player: playback, codec/metadata panel, a scene-strip of keyframes, and fullscreen";
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;
    public bool   OpensViewer           => true;

    public bool PerformAction(string filePath)
    {
        _shellServices.OpenTab("Video", new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("Video", new Dictionary<string, string> { ["path"] = path });
            return true;
        }
        return false;
    }
}
