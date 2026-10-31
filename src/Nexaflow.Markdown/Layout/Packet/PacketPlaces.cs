using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Packet;

namespace Nexaflow.Markdown.Layout.Packet;

/// <summary>
/// Which bit of a packet a point on its drawing is over — read off what <see cref="PacketBuilder"/> laid, for whatever has to turn
/// a place on the drawing back into a bit: where the end of a field is let go (<c>PacketEdits</c>).
///
/// <para>
/// Beside the builder because it is the reverse of what the builder does: a field's run on a row is its box, its bits across the
/// box in the order the row reads — highest first where <c>bitOrder</c> turns the row round.
/// </para>
/// </summary>
internal static class PacketPlaces
{
    /// <summary>
    /// The bit under <paramref name="at"/>, from <paramref name="piece"/>, the piece drawn there: within the run the point is on, or —
    /// between two runs, or over the numbers — the run nearest across on that row. Null where the point is on no row.
    /// </summary>
    public static long? BitAt(LayoutPiece piece, LayoutPoint at)
    {
        var run = RunAt(piece, at);
        if (!run.Exists || run.Part.TreePart() is not { Node: PacketFieldNode field } part) return null;

        // A field's runs are laid in the order of its rows, and so are its spans.
        var index = 0;
        foreach (var other in Rows(run).SelfAndDescendants())
        {
            if (other.Kind != PacketPiece.Field) continue;
            if (other.At == run.At) break;
            if (ReferenceEquals(other.Part, run.Part)) index++;
        }

        if (index >= field.Spans.Count) return null;

        var span = field.Spans[index];
        var box = run.Children.FirstOrDefault(child => child.Kind == MermaidPiece.Shape).Bounds;
        if (box.Width <= 0) return null;

        var across = (long)(Math.Clamp((at.X - box.Left) / box.Width, 0, 0.999999) * span.Bits);
        var turned = (part.Ancestors().LastOrDefault()?.Node as ConfiguredNode<PacketConfig>)?.Config.BitOrder == PacketBitOrder.Descending;

        return turned ? span.To - across : span.From + across;
    }

    /// <summary>The run of a field the point is on — or, from anywhere else on a row, that row's run nearest across.</summary>
    private static LayoutPiece RunAt(LayoutPiece piece, LayoutPoint at)
    {
        for (var up = piece; up.Exists; up = up.Parent)
        {
            if (up.Kind == PacketPiece.Field) return up;

            if (up.Kind == PacketPiece.Row)
                return up.Children.Where(child => child.Kind == PacketPiece.Field).DefaultIfEmpty()
                         .MinBy(run => run.Exists ? Math.Max(0, Math.Max(run.Bounds.Left - at.X, at.X - run.Bounds.Right)) : double.MaxValue);
        }

        return default;
    }

    /// <summary>The rows a run is laid in, which is the whole packet drawn.</summary>
    private static LayoutPiece Rows(LayoutPiece run)
    {
        for (var up = run; up.Exists; up = up.Parent)
            if (up.Kind == PacketPiece.Rows) return up;

        return run;
    }
}
