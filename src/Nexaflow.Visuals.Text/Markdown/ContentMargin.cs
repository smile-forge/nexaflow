using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Something drawn beside a surface's page in step with the lines on it — their numbers, a breakpoint, a fold's handle, a line picked
/// out — one of a stack the surface keeps on each side of the page (<see cref="MarkdownSurface.LeadingMargins"/>,
/// <see cref="MarkdownSurface.TrailingMargins"/>).
///
/// <para>
/// <strong>The lines are the engine's; what stands beside them is the margin's.</strong> Which lines stand on the page, and where their
/// rows are, only what laid them can say (<see cref="MarkdownSurface.LinesShown"/>). Nothing of a margin is in the content, so nothing
/// in it is a place for a caret, a stretch to pick out or copy, or something a search finds. What it draws for a line, and what a
/// press on one means, are its own — and a margin that picks lines out asks the surface to, so what is picked out is the content's.
/// </para>
/// <para>
/// The surface says when the lines on its page may have moved — scrolled, laid again, written in — and the margin is measured and drawn
/// again from where they now stand, each line's top counted from the margin's own.
/// </para>
/// </summary>
public abstract class ContentMargin : FrameworkElement
{
    protected ContentMargin() => ClipToBounds = true;

    /// <summary>The surface it stands beside, or null while it stands beside none.</summary>
    protected MarkdownSurface? Surface { get; private set; }

    /// <summary>Stands beside <paramref name="surface"/>, or beside none.</summary>
    internal void Beside(MarkdownSurface? surface)
    {
        Surface = surface;
        LinesMoved();
    }

    /// <summary>The lines on the page may have moved, or what is written changed: measured and drawn again.</summary>
    internal void LinesMoved()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected sealed override void OnRender(DrawingContext drawingContext)
    {
        if (Surface is not { } surface) return;

        // Where the surface's top stands in this margin's own frame, which is where its lines are counted from.
        var shift = surface.TranslatePoint(default, this).Y;

        Draw(drawingContext, [.. surface.LinesShown().Select(line => line with { Top = line.Top + shift })]);
    }

    /// <summary>What it draws for <paramref name="lines"/>, the lines on the page, each line's top in this margin's own frame.</summary>
    protected abstract void Draw(DrawingContext drawing, IReadOnlyList<ShownLine> lines);
}
