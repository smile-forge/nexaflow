using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.IO;

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
        public string DisplayName            => "Show";
        public static string? StaticExperienceId => "/text/html";
        public string ExperienceId           => "/text/html";
        public string ExperienceDescription  => "Open A browser tab to display HTML content";
        public bool   RequiresRefresh        => false;
        public bool   CanPerformAction       => true;

        public bool PerformAction(string filePath)
        {
            _shellServices.OpenTab("Html", new Dictionary<string, string> { ["path"] = filePath });
            return true;
        }

        public bool PerformAction(IEnumerable<string> filePaths)
        {
            foreach (var path in filePaths)
            {
                if (_exts.Contains(Path.GetExtension(path)))
                {
                    _shellServices.OpenTab("Html", new Dictionary<string, string> { ["path"] = path });
                    return true;
                }
            }
            return false;
        }
    }
}
