using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Radar.Stages;

/// <summary>
/// Makes the block say what its options set — the scale, the rings and whether there is a legend — the last one written
/// winning and Mermaid's own where none is, and marks the option a ring stands for.
///
/// <para>
/// The rim is <c>max</c> where it is written and otherwise the greatest value any curve gives, which nothing on a curve's own
/// line says — so this runs after <see cref="ResolveCurves"/> has worked out what each curve gives.
/// </para>
/// </summary>
/// <param name="config">What the front matter asks for, which the block carries for its builder.</param>
public sealed class ResolveOptions(RadarConfig config) : IAstStage
{
    /// <summary>How many rings a graticule has where <c>ticks</c> says nothing.</summary>
    private const int Rings = 5;

    public string Name => "radar:options";

    public ContentNode Run(ContentNode tree)
    {
        var options = new Dictionary<string, ContentNode>(StringComparer.OrdinalIgnoreCase);
        double? greatest = null;

        foreach (var node in tree.SelfAndDescendants())
        {
            if (node is RadarCurveNode curve)
            {
                foreach (var point in curve.Points)
                    if (point is { } value && (greatest is null || value > greatest)) greatest = value;
            }

            // The last one written wins.
            else if (node.Kind == RadarKinds.Option && node.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Key) is { } word)
                options[word.Text] = node;
        }

        var min = options.GetValueOrDefault(RadarGrammar.Min).Number() ?? 0;
        var max = options.GetValueOrDefault(RadarGrammar.Max).Number() ?? greatest ?? min;

        if ((options.GetValueOrDefault(RadarGrammar.Graticule) ?? options.GetValueOrDefault(RadarGrammar.Ticks)) is { } shaping)
            tree = AstRewrite.Each(tree, node => ReferenceEquals(node, shaping) ? new RadarShapingNode(node) : node);

        return new RadarBlockNode(tree, config)
        {
            Min = min,
            Max = max > min ? max : min + 1,
            Ticks = options.GetValueOrDefault(RadarGrammar.Ticks).Number() is { } ticks ? (int)ticks : Rings,
            Graticule = string.Equals(Setting(options.GetValueOrDefault(RadarGrammar.Graticule)), RadarGrammar.Polygon, StringComparison.OrdinalIgnoreCase)
                ? RadarGraticule.Polygon
                : RadarGraticule.Circle,
            ShowsLegend = !string.Equals(Setting(options.GetValueOrDefault(RadarGrammar.ShowLegend)), "false", StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>What an option is set to, where it is set to anything that is not wrong.</summary>
    private static string? Setting(ContentNode? option) =>
        option.Inner(MermaidKinds.Setting) is { Trouble: null, Width: > 0 } setting ? setting.Text : null;
}
