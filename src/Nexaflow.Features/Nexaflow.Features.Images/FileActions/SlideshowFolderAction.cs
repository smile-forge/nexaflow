using Nexaflow.Features.Common;

namespace Nexaflow.Features.Images.FileActions;

/// <summary>Opens the folder's images in the one-at-a-time slideshow viewer.</summary>
public sealed class SlideshowFolderAction : ImageFolderAction
{
    public SlideshowFolderAction(IShellServices shell) : base(shell) { }

    protected override string ViewMode => "slideshow";
    public override string Icon        => "🖼";
    public override string DisplayName => "Slideshow";
}
