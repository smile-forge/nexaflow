using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
        public IconRef Icon                  => IconRef.Emoji("🖼");
        public string DisplayName            => Str.Get("Images.Action.AsImage");
        public static string? StaticExperienceId => "/image";
        public string ExperienceId           => "/image";
        public string ExperienceDescription  => Str.Get("Images.Action.ViewerDescription");
        public bool   RequiresRefresh        => false;
        public bool   CanPerformAction       => true;
        public bool   OpensViewer            => true;

        // Routed through the selection overload so both filter alike — opening one file and opening a
        // one-item selection of it are the same user intent, and only one of them used to check the type.
        public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default) => PerformActionAsync([filePath], ct);

        public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
        {
            var images = filePaths
                .Where(ImageFileTypes.IsImage)
                .ToList();

            if (images.Count == 0) return Task.FromResult(false);
            _shellServices.OpenTab("Images", new Dictionary<string, string> { ["paths"] = string.Join('|', images) });
            return Task.FromResult(true);
        }
    }
}
