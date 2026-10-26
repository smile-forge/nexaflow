using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

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
    public string DisplayName           => Str.Get("Video.Action.Play");
    public static string? StaticExperienceId => "/video";
    public string ExperienceId          => "/video";
    public string ExperienceDescription => Str.Get("Video.Action.ExperienceDescription");
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;
    public bool   OpensViewer           => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        _shellServices.OpenTab("Video", new Dictionary<string, string> { ["path"] = filePath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("Video", new Dictionary<string, string> { ["path"] = path });
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}
