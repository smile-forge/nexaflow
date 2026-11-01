using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Images.FileActions;

/// <summary>Opens the folder's images as a scrolling grid of thumbnails (album view).</summary>
public sealed class AlbumFolderAction : ImageFolderAction
{
    public AlbumFolderAction(IShellServices shell, IVirtualFileSystem vfs) : base(shell, vfs) { }

    protected override string ViewMode => "album";
    public override IconRef Icon       => IconRef.Emoji("▦");
    public override string DisplayName => Str.Get("Images.Action.Album");
}
