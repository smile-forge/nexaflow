using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Radar.Stages;

/// <summary>
/// Works out which axis each of a curve's values is for, and says so where a curve's values do not fit the axes.
///
/// <para>
/// None of it is in the curve's own characters. A number is for the axis written in its place in the order the axes are
/// written — anywhere in the block, above the curve or under it — and a value naming its axis is for that axis only where an
/// axis of that name is written. So it is worked out here and hung under each value as the axis it is for
/// (<see cref="RadarRoles.For"/>), which is all a drawing needs to know where the curve reaches.
/// </para>
/// <para>
/// A curve that does not give every axis one value is a curve Mermaid draws nothing for. That is said here, on its values,
/// and the curve is still drawn as far as it goes — it is what the reader is fixing. Values still being written are not held
/// against it: braces not yet closed, or an axis not yet named.
/// </para>
/// </summary>
public sealed class ResolveCurves : IAstStage
{
    public string Name => "radar:curves";

    public ContentNode Run(ContentNode tree)
    {
        var axes = new List<string>();
        var named = new HashSet<string>(StringComparer.Ordinal);

        // Every axis first, wherever it is written: a curve's values are for the axes of the whole block.
        tree = AstRewrite.Each(tree, node => node.Kind == RadarKinds.Axis ? Axis(node) : node);
        return AstRewrite.Each(tree, node => node.Kind == RadarKinds.Curve ? Curve(node, axes) : node);

        ContentNode Axis(ContentNode axis)
        {
            var id = Id(axis);
            if (id.Length == 0) return axis;

            if (named.Add(id))
            {
                axes.Add(id);
                return axis;
            }

            return Within(axis, MermaidKinds.Name, name => name.Saying($"An axis called {id} is already written."));
        }
    }

    private static ContentNode Curve(ContentNode curve, IReadOnlyList<string> axes)
    {
        if (curve.Children.FirstOrDefault(child => child.Kind == RadarKinds.Values) is not { } values) return curve;

        var entries = values.Children.Where(child => child.Kind == RadarKinds.Entry).ToList();
        var keyed = entries.Count > 0 && Key(entries[0]) is not null;
        var given = new HashSet<string>(StringComparer.Ordinal);
        var place = 0;

        var resolved = values.With([.. values.Children.Select(child => child.Kind == RadarKinds.Entry ? Entry(child) : child)]);

        if (resolved.Trouble is null && axes.Count > 0 && values.Children.Any(child => child.Role == Roles.Close))
        {
            var missing = axes.Where(axis => !given.Contains(axis)).ToList();

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
                return at < axes.Count ? entry.Saying(RadarKinds.Fact, RadarRoles.For, axes[at]) : entry;
            }

            var id = key.Inner(MermaidKinds.Words)?.Text ?? string.Empty;
            if (!axes.Contains(id)) return Within(entry, MermaidKinds.Name, name => name.Saying($"No axis called {id} is written."));
            if (!given.Add(id)) return Within(entry, MermaidKinds.Name, name => name.Saying($"This curve already gives {id} a value."));

            return entry.Saying(RadarKinds.Fact, RadarRoles.For, id);
        }
    }

    /// <summary>What an axis is called, without its quotes — empty where it is still to name.</summary>
    private static string Id(ContentNode axis) =>
        axis.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Inner(MermaidKinds.Words)?.Text ?? string.Empty;

    /// <summary>The axis a value names before its colon, or null for a value that is only a number.</summary>
    private static ContentNode? Key(ContentNode entry) => entry.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);

    /// <summary>The same node with its part of <paramref name="kind"/> made over.</summary>
    private static ContentNode Within(ContentNode node, string kind, Func<ContentNode, ContentNode> made) =>
        node.With([.. node.Children.Select(child => child.Kind == kind ? made(child) : child)]);
}
