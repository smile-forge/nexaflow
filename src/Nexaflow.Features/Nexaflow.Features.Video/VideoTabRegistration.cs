using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Video.ViewModels;
using Nexaflow.Features.Video.Views;
using Nexaflow.Visuals.Common.Drafts;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Video;

/// <summary>Registers the "Video" page: embedded libvlc playback with a codec/metadata info panel, a
/// scene-strip of keyframes, and fullscreen. Discovered by <c>FeatureManager</c> via <see cref="StaticPageKind"/>.</summary>
public sealed class VideoTabRegistration : IPageRegistration
{
    private readonly IShellServices _shell;
    private readonly VideoConfig _config;

    public VideoTabRegistration(VideoConfig config, IShellServices shell)
    {
        _config = config;
        _shell  = shell;
    }

    public static string StaticPageKind => "Video";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Path to the video file to open.", Required: true, OnDisk: true),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path  = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? Str.Get("Video.Page.Title") : Path.GetFileName(path);

        var page = new Page
        {
            Title       = title,
            Icon        = "🎬",
        };
        page.SetFileBreadcrumbs(path, title);

        // A clip made for the user and not saved yet carries the folder its Save as starts in.
        var draft = path.Length > 0 && pageParams?.TryGetValue(DraftFile.PageParameterName, out var offered) == true
            ? new DraftSaving(path, offered is { Length: > 0 } ? offered : null,
                              (name, extensions, folder) => _shell.PickSaveFileAsync(name, extensions, folder), _shell.ShowError)
            : null;
        // A draft kept somewhere is that file from then on: it opens there, and the draft's tab goes.
        if (draft is not null)
            draft.Kept += kept =>
            {
                _shell.OpenTab(StaticPageKind, new Dictionary<string, string> { ["path"] = kept });
                _shell.CloseTab(page);
            };

        // Build the engine lazily on first activation; dispose its native resources when the tab closes.
        page.ContentFactory = () =>
        {
            var vm = new VideoViewModel(path, _shell, _config.EnableHardwareDecoding) { Draft = draft };
            page.Closed += (_, _) => vm.Dispose();

            // A fatal engine error closes the tab with a notice rather than leaving a broken player open.
            vm.FatalError += message =>
            {
                _shell.ShowNotification(Str.Format("Video.Notice.PlayFailedFormat", title, message));
                _shell.CloseTab(page);
            };

            return new VideoView(vm);
        };

        return page;
    }
}
