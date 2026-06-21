using Nexaflow.Features.Common;

namespace Nexaflow.Features.Images.FileActions;

/// <summary>Opens the folder's images as a scrolling grid of thumbnails (album view).</summary>
public sealed class AlbumFolderAction : ImageFolderAction
{
    public AlbumFolderAction(IShellServices shell) : base(shell) { }

    protected override string ViewMode => "album";
    public override string Icon        => "▦";
    public override string DisplayName => "Album";
}
