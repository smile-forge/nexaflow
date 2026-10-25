using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Audio.FileActions;

/// <summary>
/// Opens audio file(s) in the player tab. A single file opens just that track (no playlist); a
/// multi-selection opens exactly those files as the queue. (Opening a whole folder as a queue is the
/// separate "Play folder" folder action.)
/// </summary>
public sealed class ShowAudioAction : IFileAction, ICacheable
{
    private readonly IShellServices _shell;

    public ShowAudioAction(IShellServices shell) => _shell = shell;

    public bool   IsDestructive         => false;
    public bool   SupportsMultipleFiles => true;
    public string Icon                  => "🎵";
    public string DisplayName           => Str.Get("Audio.Action.AsAudio");
    public static string? StaticExperienceId => "/audio";
    public string ExperienceId          => "/audio";
    public string ExperienceDescription => Str.Get("Audio.Action.AsAudioDescription");
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;
    public bool   OpensViewer           => true;

    // A single file opens on its own — no folder queue (use "Play folder" for that). It routes through the
    // selection overload so the two cannot disagree: the file browser picks the overload from how many files
    // are selected, so filtering one and not the other queued a text file from the file list and refused the
    // very same file as a one-item selection.
    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default) => PerformActionAsync([filePath], ct);

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        var audio = filePaths.Where(AudioFileTypes.IsAudio).ToList();
        if (audio.Count == 0) return Task.FromResult(false);
        Open(audio, 0);
        return Task.FromResult(true);
    }

    private void Open(IReadOnlyList<string> queue, int index) =>
        _shell.OpenTab("Audio", new Dictionary<string, string>
        {
            ["paths"] = string.Join('|', queue),
            ["index"] = index.ToString(),
        });
}
