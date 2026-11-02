using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Icons;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Images.FileActions;

/// <summary>
/// "New → Layered picture (.ora)": a blank canvas, its size and background set up in the picker
/// (<see cref="NewLayeredPictureOptions"/>), written into the folder as OpenRaster and opened in the editor.
/// </summary>
public sealed class LayeredPictureCreateAction(IShellServices shell, IVirtualFileSystem vfs) : IFileCreateWithOptions, ICacheable, IDisposable
{
    private readonly ImageEditingServices _editing = new(vfs);

    public IconRef Icon         => IconRef.Emoji("🗂");
    public string DisplayName   => Str.Get("Images.Create.LayeredPicture");
    public string FileExtension => ".ora";

    public bool IsOffered => true;

    public IFileCreateOptions OpenOptions(string folderPath) => new NewLayeredPictureOptions(shell, _editing, folderPath);

    public void Dispose() => _editing.Dispose();
}
