using System;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>Where an edit landed: state, layout, and which place the caret sits at.</summary>
/// <param name="State">Source, caret, selection, and the stretch shown as its own characters.</param>
/// <param name="Laid">What the builder made of it — the tree every question about the picture is asked of.</param>
/// <param name="At">The caret's place, or -1 where it is standing at none of them.</param>
public readonly record struct Landing(EditState State, Laid Laid, int At)
{
    /// <summary>True when the caret sits inside whatever ends at its offset, not stepped out past it — a 3 typed just inside the exponent of <c>x^2</c> makes it twenty-three; one mark to the right, it doesn't.</summary>
    public bool Innermost => At < 0 || At == Laid.Root.StopAt(State.Caret);
}
