using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common;

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
    public string DisplayName           => "As Audio";
    public static string? StaticExperienceId => "/audio";
    public string ExperienceId          => "/audio";
    public string ExperienceDescription => "Audio player: playback, spectrum + waveform, lyrics, tag editor";
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;
    public bool   OpensViewer           => true;

    public bool PerformAction(string filePath)
    {
        // A single file opens on its own — no folder queue (use "Play folder" for that).
        Open([filePath], 0);
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        var audio = filePaths.Where(AudioFileTypes.IsAudio).ToList();
        if (audio.Count == 0) return false;
        Open(audio, 0);
        return true;
    }

    private void Open(IReadOnlyList<string> queue, int index) =>
        _shell.OpenTab("Audio", new Dictionary<string, string>
        {
            ["paths"] = string.Join('|', queue),
            ["index"] = index.ToString(),
        });
}
