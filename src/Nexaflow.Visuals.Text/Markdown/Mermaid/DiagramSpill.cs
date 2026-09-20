using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// The nodes that offer what is left of an over-wide set of children — <c>+7 more</c> hanging off the node whose
/// children they are.
///
/// <para>
/// Depth and breadth are the two ways a diagram is too big, and they are answered differently: a node too deep is
/// folded behind the chip on what holds it, and a node with hundreds of children draws the first of them and one node
/// offering the rest, because a hundred boxes in a row is a wall rather than a picture.
/// </para>
/// <para>
/// A layout-only thing. Nobody wrote it, so it stands for no part of the source and appears in no diagram's model —
/// it is a cell, a line and a press, and the press means the same <see cref="LayoutVerbs.Expand"/> every chip means,
/// answered by the same handler.
/// </para>
/// </summary>
internal sealed class DiagramSpill
{
    /// <summary>One node's leftovers: what the node offering them says, the cell it was given, and the line to it.</summary>
    private sealed record Over(string Id, DiagramWords Says, DiagramCell Cell, DiagramJoin Join);

    private readonly IReadOnlyList<Over> _over;

    private DiagramSpill(IReadOnlyList<Over> over) => _over = over;

    /// <summary>Nothing left over anywhere, which is nearly every diagram.</summary>
    public static DiagramSpill None { get; } = new([]);

    /// <summary>
    /// A cell and a line for each node with children left over.
    /// </summary>
    /// <param name="cells">The cell a node was given, or null for one this diagram did not lay out.</param>
    /// <param name="says">What a node offering <c>n</c> children says.</param>
    /// <param name="pad">The room its words keep inside it.</param>
    public static DiagramSpill Of(DiagramExpansion folding, Func<string, DiagramCell?> cells,
                                  Func<int, DiagramWords> says, double pad)
    {
        var over = new List<Over>();

        foreach (var id in folding.Offering)
        {
            if (cells(id) is not { } from) continue;

            var said = says(folding.MoreBehind(id));

            // Inside whatever holds the node it hangs from, and in the same lane, so it is not laid out somewhere
            // its own parent is not.
            var cell = new DiagramCell(new Size(said.Width + (pad * 2), said.Height + pad))
            {
                Inside = from.Inside,
                Lane = from.Lane,
                Shape = DiagramShape.Rounded,
            };

            over.Add(new Over(id, said, cell, new DiagramJoin(from, cell)));
        }

        return over.Count == 0 ? None : new DiagramSpill(over);
    }

    /// <summary>The cells to lay out with the rest.</summary>
    public IEnumerable<DiagramCell> Cells => _over.Select(one => one.Cell);

    /// <summary>The lines to lay out with the rest, so what they join is held apart.</summary>
    public IEnumerable<DiagramJoin> Joins => _over.Select(one => one.Join);

    /// <summary>Whether anything is offering anything, so a diagram that is not pays for none of this.</summary>
    public bool Any => _over.Count > 0;

    /// <summary>Draws each of them: the line from the node they hang off, and the shape that offers them.</summary>
    public void Draw(LayoutBuilder build, DiagramRoom room, Brush fill, DiagramStroke stroke)
    {
        // No piece of its own round the lot of them: each is drawn where the diagram already has its nodes open, and one
        // more piece of the same kind holding them would be a second thing of that kind for every press to walk past.
        foreach (var one in _over)
        {
            if (DiagramConnector.Trimmed(one.Join).Select(room.At).ToList() is { Count: > 1 } along)
                DiagramConnector.Draw(build, MermaidPiece.Line, part: null, along, stroke, DiagramHead.None, DiagramHead.Arrow);

            DiagramShapes.Draw(build, MermaidPiece.More, part: null, DiagramShape.Rounded, room.At(one.Cell.Bounds),
                               fill, stroke, one.Says, MermaidPiece.Words,
                               acts: new LayoutActions
                               {
                                   Click = new LayoutIntent(LayoutVerbs.Expand, NexaflowConfig.More + one.Id, one.Says.Says),
                               });
        }
    }
}
