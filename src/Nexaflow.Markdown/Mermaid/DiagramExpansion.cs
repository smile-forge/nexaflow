using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>A node's chip: whether what is behind it is shown, and how much is behind it while it is not.</summary>
public readonly record struct DiagramFold(bool Open, int Hidden);

/// <summary>
/// The shape of a diagram as a graph, for working out how much of it to draw: every node's id, and every link between
/// two of them.
/// </summary>
/// <remarks>
/// Deliberately not any diagram's own model. Six diagrams are graph-shaped and none of them agree on what a node is —
/// a state, a class, an entity, a requirement — but all of them agree on this, which is all folding ever needs.
/// </remarks>
public readonly record struct DiagramChart(IReadOnlyList<string> Ids, IReadOnlyList<(string From, string To)> Edges);

/// <summary>
/// Which of a graph-shaped diagram's nodes are drawn, and which of the drawn ones carry a chip saying there is more
/// behind them.
///
/// <para>
/// Worked out while the diagram is being planned, before anything is placed: a node left out is simply never given a
/// cell, so the links to it have nothing to end on and the box that held it sizes itself from what is left. Nothing is
/// taken away afterwards, which would leave a hole where it stood.
/// </para>
/// <para>
/// Nothing here touches the source. What the reader opens is view state, held by the host and handed back on the next
/// laying, so the diagram is always exactly what its author wrote.
/// </para>
/// </summary>
public sealed class DiagramExpansion
{
    private readonly IReadOnlySet<string>? _shown;
    private readonly IReadOnlyDictionary<string, DiagramFold> _folds;

    private DiagramExpansion(IReadOnlySet<string>? shown, IReadOnlyDictionary<string, DiagramFold> folds)
    {
        _shown = shown;
        _folds = folds;
    }

    /// <summary>Everything drawn and nothing folded — what a diagram nobody asked to fold gets.</summary>
    public static DiagramExpansion None { get; } = new(null, new Dictionary<string, DiagramFold>(StringComparer.Ordinal));

    /// <summary>True where this draws the whole diagram, so a builder can skip asking about every node.</summary>
    public bool IsEmpty => _shown is null;

    /// <summary>Whether a node is drawn. Everything is, unless something asked otherwise.</summary>
    public bool Draws(string id) => _shown is null || _shown.Contains(id);

    /// <summary>The chip a node carries, or null where it carries none.</summary>
    public DiagramFold? FoldOf(string id) => _folds.TryGetValue(id, out var fold) ? fold : null;

    /// <summary>
    /// How much of <paramref name="chart"/> to draw under <paramref name="config"/>, with <paramref name="opened"/>
    /// holding whatever the reader has since opened or folded by hand — keyed by
    /// <see cref="NexaflowConfig.KeyFor"/>, never by id.
    /// </summary>
    public static DiagramExpansion Of(NexaflowConfig config, DiagramChart chart,
                                      IReadOnlyDictionary<string, bool>? opened = null)
    {
        if (config.IsEmpty && (opened is null || opened.Count == 0)) return None;

        var children = Children(chart.Edges);
        var roots = Roots(chart);
        var depth = Depths(roots, children);

        bool Told(string id, out bool open)
        {
            open = false;
            return opened is not null && opened.TryGetValue(config.KeyFor(id), out open);
        }

        // The reader's own opening and folding wins over what the source declared; under that, an explicit mark wins
        // over the depth the front matter drew the line at.
        bool IsOpen(string id) =>
            Told(id, out var reader) ? reader
          : !config.Collapsed.ContainsKey(id)
            && (config.Expanded.ContainsKey(id)
                || config.DefaultExpansion is not int levels
                || depth.GetValueOrDefault(id, 0) < levels);

        // Folding is only in play for a node something spoke about — or an ordinary parent in an ordinary flowchart
        // would sprout a chip it never earned.
        bool Governed(string id) =>
            config.DefaultExpansion is not null || config.Collapsed.ContainsKey(id)
            || config.Expanded.ContainsKey(id) || Told(id, out _);

        // From the roots outward, stopping at every node that is folded shut.
        var shown = new HashSet<string>(StringComparer.Ordinal);
        var shut = new HashSet<string>(StringComparer.Ordinal);
        var waiting = new Queue<string>();
        foreach (var root in roots) if (shown.Add(root)) waiting.Enqueue(root);

        while (waiting.Count > 0)
        {
            var id = waiting.Dequeue();
            if (!IsOpen(id)) { shut.Add(id); continue; }

            foreach (var child in children.GetValueOrDefault(id, []))
                if (shown.Add(child)) waiting.Enqueue(child);
        }

        var folds = new Dictionary<string, DiagramFold>(StringComparer.Ordinal);
        foreach (var id in chart.Ids)
        {
            if (!shown.Contains(id) || !Governed(id)) continue;

            var closed = shut.Contains(id);

            // A node with nothing behind it is a leaf, and a leaf has nothing to say.
            if (!closed && children.GetValueOrDefault(id, []).Count == 0) continue;

            folds[id] = new DiagramFold(!closed, closed ? Behind(id, children, shown) : 0);
        }

        return new DiagramExpansion(shown, folds);
    }

    /// <summary>What each node points at, each named once. A link to itself hides nothing, so it is not a child.</summary>
    private static Dictionary<string, List<string>> Children(IReadOnlyList<(string From, string To)> edges)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (from, to) in edges)
        {
            if (string.Equals(from, to, StringComparison.Ordinal)) continue;
            if (!map.TryGetValue(from, out var children)) map[from] = children = [];
            if (!children.Contains(to, StringComparer.Ordinal)) children.Add(to);
        }

        return map;
    }

    /// <summary>
    /// The nodes nothing points at. A diagram that is one whole cycle has none, in which case every node is a root —
    /// a flat diagram being better than an empty one.
    /// </summary>
    private static List<string> Roots(DiagramChart chart)
    {
        var pointedAt = chart.Edges
            .Where(edge => !string.Equals(edge.From, edge.To, StringComparison.Ordinal))
            .Select(edge => edge.To)
            .ToHashSet(StringComparer.Ordinal);

        var roots = chart.Ids.Where(id => !pointedAt.Contains(id)).ToList();
        return roots.Count > 0 ? roots : [.. chart.Ids];
    }

    /// <summary>How far each node is from a root, a node reached several ways taking the shortest of them.</summary>
    private static Dictionary<string, int> Depths(List<string> roots, Dictionary<string, List<string>> children)
    {
        var depth = new Dictionary<string, int>(StringComparer.Ordinal);
        var waiting = new Queue<string>();
        foreach (var root in roots) if (depth.TryAdd(root, 0)) waiting.Enqueue(root);

        while (waiting.Count > 0)
        {
            var id = waiting.Dequeue();
            foreach (var child in children.GetValueOrDefault(id, []))
                if (depth.TryAdd(child, depth[id] + 1)) waiting.Enqueue(child);
        }

        return depth;
    }

    /// <summary>How many nodes sit behind a folded one and are reachable no other way.</summary>
    private static int Behind(string id, Dictionary<string, List<string>> children, HashSet<string> shown)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { id };
        var waiting = new Queue<string>();
        waiting.Enqueue(id);
        var count = 0;

        while (waiting.Count > 0)
            foreach (var child in children.GetValueOrDefault(waiting.Dequeue(), []))
            {
                if (shown.Contains(child) || !seen.Add(child)) continue;
                count++;
                waiting.Enqueue(child);
            }

        return count;
    }
}
