using Nexaflow.Features.Images.Editing.Recipe;

namespace Nexaflow.Features.Images.Editing;

/// <summary>
/// The picture the editor's tools work on — a photo in its own session, or a picture layer of a layered picture: how it is
/// drawn, its recipe, and how an edit to the recipe is made and gathered into undo steps.
/// </summary>
public interface IRecipeEditing
{
    PictureRendering Rendering { get; }

    ImageRecipe Recipe { get; }

    /// <summary>Makes <paramref name="next"/> the recipe — one undo step, or part of the open gesture's.</summary>
    void Commit(ImageRecipe next);

    /// <summary>A continuous change begins — a drag — and lasts as one undo step until <see cref="EndGesture"/>.</summary>
    void BeginGesture();

    void EndGesture();

    /// <summary>Abandons the open gesture, restoring the recipe it began from.</summary>
    void CancelGesture();

    bool InGesture { get; }
}
