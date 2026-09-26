using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Settings;

namespace Nexaflow.Markdown.Plot.Stages;

/// <summary>
/// Correlates every numeric column with every other, and hangs the answers on the block (<see cref="PlotBlockNode"/>).
///
/// <para>
/// A correlation matrix is the one plot whose marks nobody wrote: the block holds a table of
/// observations, and what is drawn is a coefficient per pair of columns. Working it out here rather than
/// in the builder keeps the drawing free of statistics, and keeps the answer somewhere the tree can be
/// asked for it.
/// </para>
/// </summary>
public sealed class ResolveCorrelations : IAstStage
{
    public string Name => "plot:correlations";

    public ContentNode Run(ContentNode tree)
    {
        if (tree is not PlotBlockNode { Settings.Geom: PlotGeom.Corr } block) return tree;

        var rows = tree.Rows().Where(row => !row.IsHeader()).ToList();
        if (rows.Count == 0) return tree;

        var order = new List<string>();
        var read = new Dictionary<string, double?[]>(StringComparer.Ordinal);

        for (var at = 0; at < rows.Count; at++)
            foreach (var cell in rows[at].Cells())
            {
                if (cell is not PlotCellNode { Column: { } name } said) continue;

                if (!read.TryGetValue(name, out var down))
                {
                    down = new double?[rows.Count];
                    read[name] = down;
                    order.Add(name);
                }

                down[at] = said.Number;
            }

        // A column is correlated where more of its cells read as numbers than do not, which is the rule
        // the rest of the block tells a number from a name by. A column of names has no coefficient.
        var numeric = order.Where(name => read[name].Count(value => value is not null) * 2 > rows.Count)
                           .ToList();

        if (numeric.Count < 2) return tree;

        var pairs = new List<(string Across, string Down, double R)>();

        foreach (var down in numeric)
            foreach (var across in numeric)
            {
                var points = Paired(read[across], read[down]);

                if (PlotFits.Of(block.Settings.Method, points) is not { } correlation) continue;

                pairs.Add((across, down, correlation.R));
            }

        return pairs.Count == 0 ? tree : block.Correlated(pairs);
    }

    /// <summary>The rows where both columns read as a number — a row missing either says nothing about the pair.</summary>
    private static IReadOnlyList<(double X, double Y)> Paired(double?[] across, double?[] down)
    {
        var points = new List<(double X, double Y)>(across.Length);

        for (var at = 0; at < across.Length; at++)
            if (across[at] is { } x && down[at] is { } y)
                points.Add((x, y));

        return points;
    }
}
