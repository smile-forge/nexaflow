using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Radar.Stages;

/// <summary>
/// Works out which axis each of a curve's values is for, and what that makes of the chart: which axes are spokes, and how far
/// each curve reaches along each of them — and says so where a curve's values do not fit the axes.
///
/// <para>
/// None of it is in the curve's own characters. A number is for the axis written in its place in the order the axes are
/// written — anywhere in the block, above the curve or under it — and a value naming its axis is for that axis only where an
/// axis of that name is written. So each axis with a spoke is made a <see cref="RadarAxisNode"/>, and each curve a
/// <see cref="RadarCurveNode"/> holding its value for every spoke, which is all a drawing needs to know where it reaches.
/// </para>
/// <para>
/// A curve that does not give every axis one value is a curve Mermaid draws nothing for. That is said here, on its values,
/// and the curve is still drawn as far as it goes — it is what the reader is fixing. Values still being written are not held
/// against it: braces not yet closed, or an axis not yet named.
/// </para>
/// </summary>
/// <param name="config">What the front matter asks for, which names the colour written for each curve's place.</param>
/// <param name="writing">
/// Whether somebody is writing in the chart: then an axis or a curve still to name has a spoke or a legend row to name it in,
/// and a curve still waiting for its values has its row — a piece that went away would take the caret with it. A chart only
/// being read draws what there is.
/// </param>
public sealed class ResolveCurves(RadarConfig config, bool writing) : IAstStage
{
    public string Name => "radar:curves";

    public ContentNode Run(ContentNode tree)
    {
        var axes = new List<string>();
        var named = new HashSet<string>(StringComparer.Ordinal);
        var spokes = new List<string>();
        var order = 0;

        // Every axis first, wherever it is written: a curve's values are for the axes of the whole block.
        tree = AstRewrite.Each(tree, node => node.Kind == RadarKinds.Axis ? Axis(node) : node);
        return AstRewrite.Each(tree, node => node.Kind == RadarKinds.Curve ? Curve(node, order++) : node);

        ContentNode Axis(ContentNode axis)
        {
            if (Named(axis) is not { } words) return axis;

            var id = words.Text;
            if (id.Length > 0 && !named.Add(id))
                axis = Within(axis, MermaidKinds.Name, name => name.Saying($"An axis called {id} is already written."));
            else if (id.Length > 0)
                axes.Add(id);
            else if (!writing)
                return axis;

            spokes.Add(id);
            return new RadarAxisNode(axis);
        }

        ContentNode Curve(ContentNode curve, int place)
        {
            var given = new Dictionary<string, double?>(StringComparer.Ordinal);
            curve = Checked(curve, axes, given);
            if (Named(curve) is null) return curve;

            var points = new double?[spokes.Count];
            var drawn = false;

            for (var at = 0; at < points.Length; at++)
            {
                points[at] = spokes[at].Length == 0 ? null : given.GetValueOrDefault(spokes[at]);
                drawn |= points[at] is not null;
            }

            return new RadarCurveNode(curve)
            {
                Points = points,
                Order = place,
                Colour = config.Swatches.GetValueOrDefault(place % RadarConfig.PaletteSize),
                Drawn = drawn,
                Listed = drawn || writing,
            };
        }
    }

    /// <summary>
    /// The curve, saying what is wrong with its values where they do not fit the axes — and, into <paramref name="given"/>, the
    /// value it gives each axis a value is for.
    /// </summary>
    private static ContentNode Checked(ContentNode curve, IReadOnlyList<string> axes, Dictionary<string, double?> given)
    {
        if (curve.Children.FirstOrDefault(child => child.Kind == RadarKinds.Values) is not { } values) return curve;

        var entries = values.Children.Where(child => child.Kind == RadarKinds.Entry).ToList();
        var keyed = entries.Count > 0 && Key(entries[0]) is not null;
        var place = 0;

        var resolved = values.With([.. values.Children.Select(child => child.Kind == RadarKinds.Entry ? Entry(child) : child)]);

        if (resolved.Trouble is null && axes.Count > 0 && values.Children.Any(child => child.Role == Roles.Close))
        {
            var missing = axes.Where(axis => !given.ContainsKey(axis)).ToList();

            resolved = resolved.Saying(
                keyed
                    ? missing.Count > 0 ? $"This curve gives no value for {string.Join(", ", missing)}." : null
                    : place != axes.Count ? $"This curve gives {place} {(place == 1 ? "value" : "values")} for {axes.Count} {(axes.Count == 1 ? "axis" : "axes")}: one for each, in the order the axes are written." : null);
        }

        return curve.With([.. curve.Children.Select(child => ReferenceEquals(child, values) ? resolved : child)]);

        ContentNode Entry(ContentNode entry)
        {
            var key = Key(entry);

            if ((key is not null) != keyed)
                return entry.Saying(keyed
                    ? "Every value of this curve names the axis it is for: m: 85."
                    : "This curve's values are in the order of the axes, so none names its axis.");

            if (key is null)
            {
                var at = place++;
                if (at < axes.Count) given.TryAdd(axes[at], entry.Number());
                return entry;
            }

            var id = key.Inner(MermaidKinds.Words)?.Text ?? string.Empty;
            if (!axes.Contains(id)) return Within(entry, MermaidKinds.Name, name => name.Saying($"No axis called {id} is written."));
            if (!given.TryAdd(id, entry.Number())) return Within(entry, MermaidKinds.Name, name => name.Saying($"This curve already gives {id} a value."));

            return entry;
        }
    }

    /// <summary>What an axis or a curve is called — its name's words, empty where it is still to name — or null for one whose name could not be read.</summary>
    private static ContentNode? Named(ContentNode item) =>
        item.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Words();

    /// <summary>The axis a value names before its colon, or null for a value that is only a number.</summary>
    private static ContentNode? Key(ContentNode entry) => entry.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);

    /// <summary>The same node with its part of <paramref name="kind"/> made over.</summary>
    private static ContentNode Within(ContentNode node, string kind, Func<ContentNode, ContentNode> made) =>
        node.With([.. node.Children.Select(child => child.Kind == kind ? made(child) : child)]);
}
