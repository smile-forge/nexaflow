using System.Collections.Immutable;
using Nexaflow.Features.Images.Editing.Recipe;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing;

/// <summary>
/// The annotations the Markup tool works on — a photo's or a picture layer's, as its recipe keeps them, or a markup layer's own —
/// and how a change to them is made and gathered into undo steps.
/// </summary>
public interface IMarkupEditing
{
    ImmutableArray<MarkupElement> Markup { get; }

    /// <summary>Makes <paramref name="change"/> of the annotations — one undo step, or part of the open gesture's.</summary>
    void CommitMarkup(Func<ImmutableArray<MarkupElement>, ImmutableArray<MarkupElement>> change);

    /// <summary>What annotation sizes are measured against — the picture's size, or the canvas's — in the annotations' own
    /// coordinates; null while it is not known.</summary>
    SKSize? Extent { get; }

    /// <summary>A continuous change begins — a drag — and lasts as one undo step until <see cref="EndGesture"/>.</summary>
    void BeginGesture();

    void EndGesture();

    /// <summary>Abandons the open gesture, restoring what it began from.</summary>
    void CancelGesture();

    bool InGesture { get; }
}
