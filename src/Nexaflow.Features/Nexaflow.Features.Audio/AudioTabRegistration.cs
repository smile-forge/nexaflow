using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Features.Audio.ViewModels;
using Nexaflow.Features.Audio.Views;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Drafts;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Audio;

/// <summary>
/// Registers the audio player page with <see cref="FeatureManager"/>. Accepts a pipe-separated
/// <c>"paths"</c> queue and an <c>"index"</c> selecting the starting track; a <c>"draft"</c> marks one recording made for
/// the user and not saved yet, naming the folder its Save as starts in. Disposal of the
/// view-model (and its playback engine) is wired to <see cref="Page.Closed"/> — NOT the view's
/// Unloaded — so playback survives tab switches and only stops when the tab is closed.
/// </summary>
public sealed class AudioTabRegistration : IPageRegistration
{
    private readonly IShellServices _shell;
    private readonly AudioConfig _config;

    public AudioTabRegistration(IShellServices shell, AudioConfig config)
    {
        _shell = shell;
        _config = config;
    }

    public static string StaticPageKind => "Audio";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("paths", "Pipe-separated audio file paths forming the play queue.", OnDisk: true),
        new("index", "Zero-based index of the track to start on.", Required: false),
        new("autoplay", "When \"true\", playback starts immediately (used by \"Play folder\").", Required: false),
        new("scope", "\"folder\" when the queue is a whole folder (used by \"Play folder\").", Required: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var paths = pageParams?.GetValueOrDefault("paths")?
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .ToList() ?? [];

        int index = 0;
        if (pageParams?.GetValueOrDefault("index") is { } raw && int.TryParse(raw, out var i))
            index = i;

        bool autoPlay = string.Equals(pageParams?.GetValueOrDefault("autoplay"), "true",
            StringComparison.OrdinalIgnoreCase);

        // "folder" = the whole-folder "Play folder" action; otherwise it's a single file or explicit selection.
        var wholeFolder = pageParams?.GetValueOrDefault("scope") == "folder";

        var currentPath = paths.Count > 0 ? paths[Math.Clamp(index, 0, paths.Count - 1)] : string.Empty;
        // A whole-folder queue is named for the folder — stable regardless of which track is playing; a single
        // file or an explicit multi-selection is named for the starting track (falls back to it if the folder
        // name can't be derived, e.g. a drive root).
        var title = paths.Count > 0 ? Path.GetFileName(currentPath) : Str.Get("Audio.Page.Title");
        if (wholeFolder && Path.GetDirectoryName(currentPath) is { Length: > 0 } dir)
        {
            var folderName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(folderName)) title = folderName;
        }

        var page = new Page
        {
            Title = title,
            Icon = "🎵",
        };
        // Single track → "folder › track"; a folder queue → "folder › all audio files"; a selection → "folder › N tracks".
        if (paths.Count > 1)
            page.SetMultiFileBreadcrumbs(paths, wholeFolder ? Str.Get("Audio.Page.AllAudioFiles") : Str.Format("Audio.Page.TrackCountFormat", paths.Count));
        else
            page.SetFileBreadcrumbs(currentPath, title);

        var draft = paths.Count == 1 && pageParams?.TryGetValue(DraftFile.PageParameterName, out var offered) == true
            ? new DraftSaving(paths[0], offered is { Length: > 0 } ? offered : null,
                              (name, extensions, folder) => _shell.PickSaveFileAsync(name, extensions, folder), _shell.ShowError)
            : null;
        // A draft kept somewhere is that file from then on: it opens there, and the draft's tab goes.
        if (draft is not null)
            draft.Kept += kept =>
            {
                _shell.OpenTab(StaticPageKind, new Dictionary<string, string> { ["paths"] = kept });
                _shell.CloseTab(page);
            };

        page.ContentFactory = () =>
        {
            var vm = new AudioViewModel(paths, index, _shell, _config, autoPlay) { Draft = draft };
            page.Closed += (_, _) => vm.Dispose();
            return new AudioView(vm);
        };

        return page;
    }
}
