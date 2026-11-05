using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// What the picture tools make for one picture's recipe — its restoration, its removed background, its erasing, its scene
/// depth and the outline its lens blur is focused on — made in the background as each is asked for, the restoration and the scene depth following the erasing they are
/// made from. The viewer's own follow the picture on screen and say what they are doing; a layered picture's other picture
/// layers each have one of these, quietly.
/// </summary>
internal sealed class PictureToolEdits : IDisposable
{
    public PictureToolEdits(ImageEditingServices services, IPictureTools? pictures, IPictureSelection? selection, IPictureErasing? erasing,
                        PictureModels models, Func<Action, Task> runOnUi, Action made)
    {
        Restorer = new PhotoRestorer(services, pictures, runOnUi, models);
        Remover = new BackgroundRemover(services, selection, runOnUi, models);
        Eraser = new PhotoEraser(services, erasing, selection, runOnUi, models);
        SceneDepth = new PhotoSceneDepth(services, services.SceneDepth, runOnUi, models);
        FocusedSubject = new FocusedSubject(services, selection, runOnUi, models);
        Restorer.Made += made;
        Remover.Made += made;
        SceneDepth.Made += made;
        FocusedSubject.Made += made;
        // The restoration and the scene depth are of the picture as erased: they follow each erasing as it is made.
        Eraser.Made += () =>
        {
            Restorer.Follow(Followed);
            SceneDepth.Follow(Followed);
            made();
        };
    }

    public PhotoRestorer Restorer { get; }

    public BackgroundRemover Remover { get; }

    public PhotoEraser Eraser { get; }

    public PhotoSceneDepth SceneDepth { get; }

    public FocusedSubject FocusedSubject { get; }

    /// <summary>The picture followed; null while none is.</summary>
    public PictureRendering? Followed { get; private set; }

    /// <summary>The picture to follow — called again whenever its recipe moves.</summary>
    public void Follow(PictureRendering? rendering)
    {
        Followed = rendering;
        Eraser.Follow(rendering);
        Restorer.Follow(rendering);
        Remover.Follow(rendering);
        SceneDepth.Follow(rendering);
        FocusedSubject.Follow(rendering);
    }

    /// <summary>Whether what the recipe asks of them is all here.</summary>
    public bool IsPending => Restorer.IsPending || Remover.IsPending || Eraser.IsPending || SceneDepth.IsPending || FocusedSubject.IsPending;

    public void Dispose()
    {
        Restorer.Dispose();
        Remover.Dispose();
        Eraser.Dispose();
        SceneDepth.Dispose();
        FocusedSubject.Dispose();
    }
}
