using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Plot.Stages;

/// <summary>
/// Works out what a <c>stats:</c> line reports — how x and y correlate by the method the block names, the square of it, how many
/// rows it stands on and how likely so strong a one is by chance — and hangs it on the block (<see cref="PlotBlockNode"/>).
///
/// <para>
/// Worked out from the numbers written, never from where they land: a log axis moves every point and changes nothing about how
/// two columns go together. Once over every row, and once over the rows of each value the facet column takes, because a plot
/// divided into panels reports each panel's own.
/// </para>
/// <para>
/// Down a matrix nothing is reported: its x and y are the names above and beside a value, not numbers.
/// </para>
/// </summary>
public sealed class ResolveStatistics : IAstStage
{
    public string Name => "plot:statistics";

    public ContentNode Run(ContentNode tree)
    {
        if (tree is not PlotBlockNode { Settings.Stats.Count: > 0, Matrix: false } block) return tree;

        var every = new List<(double X, double Y)>();
        var levels = new Dictionary<string, List<(double X, double Y)>>(StringComparer.Ordinal);

        foreach (var row in tree.Rows())
        {
            if (row.IsHeader()) continue;

            double? x = null, y = null;
            string? facet = null;

            foreach (var cell in row.Cells())
            {
                if (cell is not PlotCellNode read) continue;

                foreach (var channel in read.Feeds)
                    switch (channel)
                    {
                        case PlotAesthetic.X: x = read.Number; break;
                        case PlotAesthetic.Y: y = read.Number; break;
                        case PlotAesthetic.Facet: facet = cell.Says(); break;
                    }
            }

            if (x is not { } across || y is not { } up) continue;

            every.Add((across, up));

            if (facet is null) continue;

            if (!levels.TryGetValue(facet, out var level)) levels[facet] = level = [];
            level.Add((across, up));
        }

        var method = block.Settings.Method;
        var statistics = new Dictionary<string, PlotCorrelation>(StringComparer.Ordinal);

        foreach (var (name, points) in levels)
            if (PlotFits.Of(method, points) is { } said)
                statistics[name] = said;

        return block.Reporting(PlotFits.Of(method, every), statistics);
    }
}
