using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Web.FileActions
{
    /// <summary>
    /// Opens an HTML or Internet Shortcut (.url) file in a new HTMLView tab
    /// rendered by WebView2.
    /// </summary>
    public class ShowHtmlAction : IFileAction, ICacheable
    {
        private static readonly HashSet<string> _exts = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ".html", ".htm", ".url"
        };

        private readonly IShellServices _shellServices;

        public ShowHtmlAction(IShellServices shellServices) => _shellServices = shellServices;

        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => false;
        public string Icon                   => "🌐";
        public string DisplayName            => Str.Get("Web.Action.Browse");
        public static string? StaticExperienceId => "/text/html";
        public string ExperienceId           => "/text/html";
        public string ExperienceDescription  => Str.Get("Web.Action.BrowseDescription");
        public bool   RequiresRefresh        => false;
        public bool   CanPerformAction       => true;
        public bool   OpensViewer            => true;

        // Routed through the selection overload so both filter alike — opening one file and opening a
        // one-item selection of it are the same user intent, and only one of them used to check the type.
        public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default) => PerformActionAsync([filePath], ct);

        public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
        {
            foreach (var path in filePaths)
            {
                if (_exts.Contains(Path.GetExtension(path)))
                {
                    _shellServices.OpenTab("Html", new Dictionary<string, string> { ["path"] = path });
                    return Task.FromResult(true);
                }
            }
            return Task.FromResult(false);
        }
    }
}
