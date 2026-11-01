using Nexaflow.Features.Images.Editing.Recipe;
using SkiaSharp;
using System.Collections.Immutable;

namespace Nexaflow.Features.Images.Editing;

/// <summary>
/// The picture the editor's tools work on — a photo in its own session, or a picture layer of a layered picture: how it is
/// drawn, its recipe, and how an edit to the recipe is made and gathered into undo steps. Its annotations are its recipe's.
/// </summary>
public interface IRecipeEditing : IMarkupEditing
{
    PictureRendering Rendering { get; }

    ImageRecipe Recipe { get; }

    /// <summary>Makes <paramref name="next"/> the recipe — one undo step, or part of the open gesture's.</summary>
    void Commit(ImageRecipe next);

    ImmutableArray<MarkupElement> IMarkupEditing.Markup => Recipe.Markup;

    void IMarkupEditing.CommitMarkup(Func<ImmutableArray<MarkupElement>, ImmutableArray<MarkupElement>> change)
        => Commit(Recipe with { Markup = change(Recipe.Markup) });

    SKSize? IMarkupEditing.Extent => Rendering.Proxy?.SourceSize;
}
