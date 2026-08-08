using System.Collections.Generic;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>
/// The <c>config: nexaflow:</c> front-matter block for the graph-family diagrams (flowchart, state,
/// class, ER, requirement).
/// <para>
/// Namespaced under <c>nexaflow</c> so it can never collide with a real mermaid config key, and read
/// with the same lenient reader as every other diagram config — an unknown key is ignored, so a
/// diagram carrying this block still renders in stock mermaid, just without the expansion behaviour.
/// </para>
/// <code>
/// ---
/// config:
///   nexaflow:
///     expandDepth: 2          # auto-open this many levels from the roots; deeper nodes get a [+]
///     maxFanOut: 24           # more siblings than this collapse behind a "+N more" chip (0 = off)
///     collapsed:              # ids that own a hidden subtree — a list, or id → the host's own key
///       n3: KERNEL32.dll
///     expanded:               # ids already opened
///       n0: app.exe
/// ---
/// </code>
/// </summary>
public sealed class NexaflowGraphConfig
{
    /// <summary>How many levels below the roots are opened automatically. Null → no depth limit
    /// (only the explicit <see cref="Collapsed"/> marks decide what is hidden).</summary>
    public int? ExpandDepth { get; set; }

    /// <summary>Above this many visible children, the surplus siblings are replaced by one synthetic
    /// "+N more" node that expands like any other. 0 (the default) leaves fan-out alone — a diagram
    /// never hides content its author wrote unless it asks to.</summary>
    public int MaxFanOut { get; set; }

    /// <summary>Node ids that own a subtree which is not in the source: id → the producer's own key
    /// for it (falling back to the id). These get a <c>[+]</c> chip.</summary>
    public Dictionary<string, string> Collapsed { get; } = new(StringComparer.Ordinal);

    /// <summary>Node ids already opened: id → the producer's own key. These get a <c>[−]</c> chip.</summary>
    public Dictionary<string, string> Expanded { get; } = new(StringComparer.Ordinal);

    /// <summary>True when nothing in this block asks for expansion behaviour, so the graph renders
    /// exactly as it would have without it.</summary>
    public bool IsEmpty =>
        ExpandDepth is null && MaxFanOut <= 0 && Collapsed.Count == 0 && Expanded.Count == 0;

    /// <summary>The producer's key for a node, or the id when none was declared.</summary>
    public string KeyFor(string id) =>
        Collapsed.TryGetValue(id, out var c) && c.Length > 0 ? c
      : Expanded.TryGetValue(id, out var e) && e.Length > 0 ? e
      : id;
}
