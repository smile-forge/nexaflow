using System;
using System.Collections.Generic;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// What <c>config: nexaflow:</c> says about how much of a graph-shaped diagram is drawn — the flowchart family, and
/// state, class, ER, requirement and C4 with it.
///
/// <para>
/// Namespaced under <c>nexaflow</c> so it can never collide with a key Mermaid itself reads, and read with the same
/// lenient reader as every other diagram's config: a block carrying it still draws in stock Mermaid, just without the
/// folding. One reader for the whole family rather than one per diagram, because what it says means the same thing
/// wherever it is written.
/// </para>
/// <code>
/// ---
/// config:
///   nexaflow:
///     defaultExpansion: 2     # draw this many levels down from the roots; deeper nodes fold behind a [+]
///     maxFanOut: 24           # more siblings than this fold behind one "+N more" (0 = off)
///     collapsed:              # ids that own a subtree the source does not carry — a list, or id → the host's key
///       n3: KERNEL32.dll
///     expanded:               # ids already open
///       n0: app.exe
/// ---
/// </code>
/// </summary>
/// <param name="DefaultExpansion">
/// How many levels below the roots are drawn. Null is all of them, and only an explicit <paramref name="Collapsed"/>
/// mark folds anything away.
/// </param>
/// <param name="MaxFanOut">
/// Above this many children, the surplus siblings fold behind one stand-in. Nought leaves fan-out alone — a diagram
/// never hides what its author wrote unless it asks to.
/// </param>
/// <param name="Collapsed">Ids that own a folded subtree: id → the producer's own name for it.</param>
/// <param name="Expanded">Ids already open: id → the producer's own name for it.</param>
public sealed record NexaflowConfig(
    int? DefaultExpansion,
    int MaxFanOut,
    IReadOnlyDictionary<string, string> Collapsed,
    IReadOnlyDictionary<string, string> Expanded)
{
    /// <summary>The section it is written under.</summary>
    private const string Name = "nexaflow";

    /// <summary>What is written where nothing asks for any of this, which is nearly every diagram there is.</summary>
    public static NexaflowConfig None { get; } =
        new(null, 0, new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>What a block's front matter says, or <see cref="None"/>.</summary>
    public static NexaflowConfig Read(string? yaml) => Of(MermaidConfig.Read(yaml));

    /// <summary>The same, from a config already read.</summary>
    public static NexaflowConfig Of(MermaidConfig config)
    {
        var said = config.Diagram(Name);

        // expandDepth is what every diagram the Executable feature has ever written says, and means the same thing.
        var depth = Levels(said, "defaultExpansion") ?? Levels(said, "expandDepth");
        var fan = Levels(said, "maxFanOut") ?? 0;
        var collapsed = Named(said, "collapsed");
        var expanded = Named(said, "expanded");

        return depth is null && fan <= 0 && collapsed.Count == 0 && expanded.Count == 0
            ? None
            : new NexaflowConfig(depth, fan, collapsed, expanded);
    }

    /// <summary>True where nothing here asks for folding, so the diagram draws exactly as it would without it.</summary>
    public bool IsEmpty => DefaultExpansion is null && MaxFanOut <= 0 && Collapsed.Count == 0 && Expanded.Count == 0;

    /// <summary>
    /// The producer's own name for a node, or the id where it declared none.
    ///
    /// <para>
    /// What the reader opens is remembered under this rather than the id, because an id is positional: a host that
    /// re-emits the diagram with one more node in it renumbers every one of them, and an opening remembered by id
    /// would land on whatever moved into that slot.
    /// </para>
    /// </summary>
    public string KeyFor(string id) =>
        Collapsed.TryGetValue(id, out var folded) && folded.Length > 0 ? folded
      : Expanded.TryGetValue(id, out var open) && open.Length > 0 ? open
      : id;

    /// <summary>A whole number of levels, where the key says one that is not negative.</summary>
    private static int? Levels(MermaidConfig said, string key) =>
        said.Number(key) is { } number && number >= 0 ? (int)number : null;

    /// <summary>
    /// The ids under a key, each with the producer's name for it: <c>n3: KERNEL32.dll</c> names one, and a plain list
    /// (<c>- n3</c>, or <c>[n1, n2]</c>) names each id after itself.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Named(MermaidConfig said, string key)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var id in said.List(key))
            if (id.Length > 0) found[id] = id;

        if (said.Section(key) is { } map)
            foreach (var (id, name) in map.Values)
                if (id.Length > 0) found[id] = name;

        return found;
    }
}
