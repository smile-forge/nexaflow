using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexaflow.Features.Images.FileActions
{
    /// <summary>
    /// Displays one or more image files in a new ImageViewer tab.
    /// Supports png, jpg/jpeg, gif, bmp, ico, tiff, webp.
    /// </summary>
    public class ShowImageAction : IFileAction
    {
        private static readonly HashSet<string> _exts = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tiff", ".tif", ".webp"
        };

        private readonly ITabOpener _tabOpener;

        public ShowImageAction(ITabOpener tabOpener) => _tabOpener = tabOpener;

        public bool   IsDestructive        => false;
        public bool   SupportsMultipleFiles => true;
        public string Icon                  => "🖼";
        public string DisplayName           => "Show";
        public string SupportedFileTypes    => "*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.ico;*.tiff;*.tif;*.webp";
        public bool   AppliesToFolders      => false;
        public string SupportedFolderNames  => "";
        public bool   AppliesToRoot         => false;
        public bool   AppliesToDrives       => false;
        public bool   RequiresRefresh       => false;
        public bool   CanPerformAction      => true;

        public bool PerformAction(string filePath)
        {
            _tabOpener.OpenImageViewer([filePath]);
            return true;
        }

        public bool PerformAction(IEnumerable<string> filePaths)
        {
            var images = filePaths
                .Where(p => _exts.Contains(Path.GetExtension(p)))
                .ToList();

            if (images.Count == 0) return false;
            _tabOpener.OpenImageViewer(images);
            return true;
        }
    }
}
