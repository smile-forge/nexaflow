using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Images.FileActions;

/// <summary>Opens the folder's images in the one-at-a-time slideshow viewer.</summary>
public sealed class SlideshowFolderAction : ImageFolderAction
{
    public SlideshowFolderAction(IShellServices shell, IVirtualFileSystem vfs) : base(shell, vfs) { }

    protected override string ViewMode => "slideshow";
    public override IconRef Icon       => IconRef.Emoji("🖼");
    public override string DisplayName => Str.Get("Images.Action.Slideshow");
}
