using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// What the picture tools make for one picture's recipe — its restoration, its removed background, its erasing — made in the
/// background as each is asked for, the restoration following the erasing it is made from. The viewer's own follow the
/// picture on screen and say what they are doing; a layered picture's other picture layers each have one of these, quietly.
/// </summary>
internal sealed class PictureToolEdits : IDisposable
{
    public PictureToolEdits(ImageEditingServices services, IPictureTools? pictures, IPictureSelection? selection, IPictureErasing? erasing,
                        Func<Action, Task> runOnUi, Action made)
    {
        Restorer = new PhotoRestorer(services, pictures, runOnUi);
        Remover = new BackgroundRemover(services, selection, runOnUi);
        Eraser = new PhotoEraser(services, erasing, selection, runOnUi);
        Restorer.Made += made;
        Remover.Made += made;
        // The restoration is of the picture as erased: it follows each erasing as it is made.
        Eraser.Made += () =>
        {
            Restorer.Follow(Followed);
            made();
        };
    }

    public PhotoRestorer Restorer { get; }

    public BackgroundRemover Remover { get; }

    public PhotoEraser Eraser { get; }

    /// <summary>The picture followed; null while none is.</summary>
    public PictureRendering? Followed { get; private set; }

    /// <summary>The picture to follow — called again whenever its recipe moves.</summary>
    public void Follow(PictureRendering? rendering)
    {
        Followed = rendering;
        Eraser.Follow(rendering);
        Restorer.Follow(rendering);
        Remover.Follow(rendering);
    }

    /// <summary>Whether what the recipe asks of them is all here.</summary>
    public bool IsPending => Restorer.IsPending || Remover.IsPending || Eraser.IsPending;

    public void Dispose()
    {
        Restorer.Dispose();
        Remover.Dispose();
        Eraser.Dispose();
    }
}
