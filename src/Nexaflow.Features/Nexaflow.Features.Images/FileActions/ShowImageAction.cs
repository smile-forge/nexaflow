using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Features.Images.FileActions
{
    /// <summary>
    /// Displays one or more image files in a new ImageViewer tab.
    /// Supports png, jpg/jpeg, gif, bmp, ico, tiff, webp.
    /// </summary>
    public class ShowImageAction : IFileAction, ICacheable
    {
        private readonly IShellServices _shellServices;

        public ShowImageAction(IShellServices shellServices) => _shellServices = shellServices;

        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => true;
        public string Icon                   => "🖼";
        public string DisplayName            => "As Image";
        public static string? StaticExperienceId => "/image";
        public string ExperienceId           => "/image";
        public string ExperienceDescription  => "Image Viewer";
        public bool   RequiresRefresh        => false;
        public bool   CanPerformAction       => true;
        public bool   OpensViewer            => true;

        // Routed through the selection overload so both filter alike — opening one file and opening a
        // one-item selection of it are the same user intent, and only one of them used to check the type.
        public bool PerformAction(string filePath) => PerformAction([filePath]);

        public bool PerformAction(IEnumerable<string> filePaths)
        {
            var images = filePaths
                .Where(ImageFileTypes.IsImage)
                .ToList();

            if (images.Count == 0) return false;
            _shellServices.OpenTab("Images", new Dictionary<string, string> { ["paths"] = string.Join('|', images) });
            return true;
        }
    }
}
