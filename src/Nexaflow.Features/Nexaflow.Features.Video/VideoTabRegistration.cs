using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Video.ViewModels;
using Nexaflow.Features.Video.Views;

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
        new("path", "Path to the video file to open.", Required: true),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path  = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? "Video" : Path.GetFileName(path);

        var page = new Page
        {
            Title       = title,
            Icon        = "🎬",
            Breadcrumbs = { new BreadcrumbSegment { Label = title } },
        };

        // Build the engine lazily on first activation; dispose its native resources when the tab closes.
        page.ContentFactory = () =>
        {
            var vm = new VideoViewModel(path, _shell, _config.EnableHardwareDecoding);
            page.Closed += (_, _) => vm.Dispose();

            // A fatal engine error closes the tab with a notice rather than leaving a broken player open.
            vm.FatalError += message =>
            {
                _shell.ShowNotification($"Couldn't play \"{title}\": {message}");
                _shell.CloseTab(page);
            };

            return new VideoView(vm);
        };

        return page;
    }
}
