using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Audio.FileActions;

/// <summary>
/// Opens a directory of audio files as a play queue. Offered for folders where at least 30% of the
/// top-level files are audio (<see cref="AudioFileTypes.Globs"/>). Mirrors the image folder actions.
/// </summary>
public sealed class AudioFolderAction : IFolderAction, ICacheable
{
    private readonly IShellServices _shell;
    private readonly IVirtualFileSystem _vfs;

    public AudioFolderAction(IShellServices shell, IVirtualFileSystem vfs)
    {
        _shell = shell;
        _vfs   = vfs;
    }

    public bool   IsDestructive         => false;
    public bool   SupportsMultipleFiles => false;
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;
    public bool   AppliesToRoot         => true;
    public bool   AppliesToDrives       => false;

    public string[]? ContainsFileGlobs              => AudioFileTypes.Globs;
    public int       MinimumFileGlobMatchPercentage => 30;

    public string Icon        => "🎵";
    public string DisplayName => Str.Get("Audio.Action.PlayFolder");

    public async Task<bool> PerformActionAsync(string folderPath, CancellationToken ct = default)
    {
        var queue = await AudioFileTypes.EnumerateAudioAsync(_vfs, folderPath, ct);
        if (queue.Count == 0) return false;

        _shell.OpenTab("Audio", new Dictionary<string, string>
        {
            ["paths"] = string.Join('|', queue),
            ["index"] = "0",
            ["autoplay"] = "true",   // "Play folder" starts immediately
            ["scope"] = "folder",    // whole-folder queue → breadcrumb reads "all audio files"
        });
        return true;
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> folderPaths, CancellationToken ct = default)
    => folderPaths.FirstOrDefault() is { } first ? PerformActionAsync(first, ct) : Task.FromResult(false);
}
