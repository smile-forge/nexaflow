using System;
using System.Linq;

namespace Nexaflow.Visuals.Text.Markdown.Graphs;

/// <summary>
/// How big a node has to be to hold its label. Shared by the layout (which reserves the footprint)
/// and the renderer (which draws the text into it), so the two cannot disagree about where the text
/// sits — the same reason <c>ClassBoxMetrics</c> exists for class boxes.
/// <para>
/// The cap is the point of it. Sizing a node to whatever its label measures means one
/// <c>api-ms-win-core-processthreads-l1-1-0.dll</c> sets the width of its entire layer, and a graph
/// of them comes out several thousand pixels wide and a few hundred tall — all the reading happens
/// on one axis while the other sits empty. Past the cap a label wraps instead, which spends the
/// space the diagram actually has.
/// </para>
/// </summary>
public static class NodeLabelMetrics
{
    /// <summary>Approximate advance width of the 12pt body font, per character.</summary>
    public const double CharWidth = 7.5;

    /// <summary>Horizontal padding inside the node, either side of the text.</summary>
    public const double PadX = 24;

    /// <summary>Line height used when a label wraps onto more than one line.</summary>
    public const double LineHeight = 16.0;

    /// <summary>Widest a label is allowed to make its node before it wraps instead.</summary>
    public const double MaxWidth = 260;

    /// <summary>
    /// The width a node needs for <paramref name="label"/>, and how many lines the text will take
    /// once wrapped into it.
    /// </summary>
    public static (double Width, int Lines) Measure(string label, double maxWidth = MaxWidth)
    {
        if (string.IsNullOrEmpty(label)) return (0, 1);

        var sourceLines = label.Split('\n');
        double natural  = sourceLines.Max(l => l.Length * CharWidth) + PadX;
        double width    = maxWidth > 0 ? Math.Min(natural, maxWidth) : natural;

        // Each explicit line wraps into as many rendered lines as it needs at that width.
        double usable = Math.Max(1, width - PadX);
        int lines = sourceLines.Sum(l => Math.Max(1, (int)Math.Ceiling(l.Length * CharWidth / usable)));

        return (width, lines);
    }
}
