using Nexaflow.Features.Images.Editing.Recipe;

namespace Nexaflow.Features.Images.Editing.Kept;

/// <summary>
/// What the picture tools made for a photo's recipe, by file: the restoration it asks for, and the mask it is cut out of its
/// background by. Either is null where the recipe asks for none, or it is not made yet.
/// </summary>
public sealed record MadeByTools(string? Restored = null, string? CutOut = null)
{
    public static MadeByTools None { get; } = new();

    /// <summary>Everything <paramref name="recipe"/> asks of the tools is here.</summary>
    public bool Serves(ImageRecipe recipe) => (recipe.Restore is null || Restored is not null) && (recipe.CutOut is null || CutOut is not null);
}
