using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.WinFileSystem.FileActions
{
    /// <summary>
    /// Opens an HTML or Internet Shortcut (.url) file in a new HTMLView tab
    /// rendered by WebView2.
    /// </summary>
    public class ShowHtmlAction : IFileAction
    {
        private static readonly HashSet<string> _exts = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ".html", ".htm", ".url"
        };

        private readonly ITabOpener _tabOpener;

        public ShowHtmlAction(ITabOpener tabOpener) => _tabOpener = tabOpener;

        public bool   IsDestructive        => false;
        public bool   SupportsMultipleFiles => false;
        public string Icon                  => "🌐";
        public string DisplayName           => "Show";
        public string SupportedFileTypes    => "*.html;*.htm;*.url";
        public bool   AppliesToFolders      => false;
        public string SupportedFolderNames  => "";
        public bool   AppliesToRoot         => false;
        public bool   AppliesToDrives       => false;
        public bool   RequiresRefresh       => false;
        public bool   CanPerformAction      => true;

        public bool PerformAction(string filePath)
        {
            _tabOpener.OpenHtmlViewer(filePath);
            return true;
        }

        public bool PerformAction(IEnumerable<string> filePaths)
        {
            // Single-file action — open the first matching file
            foreach (var path in filePaths)
            {
                if (_exts.Contains(Path.GetExtension(path)))
                {
                    _tabOpener.OpenHtmlViewer(path);
                    return true;
                }
            }
            return false;
        }
    }
}
