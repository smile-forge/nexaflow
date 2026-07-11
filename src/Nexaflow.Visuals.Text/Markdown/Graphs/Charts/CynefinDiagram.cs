namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>The five Cynefin domains.  Screen positions: <see cref="Complex"/> top-left,
/// <see cref="Complicated"/> top-right, <see cref="Clear"/> bottom-right, <see cref="Chaotic"/>
/// bottom-left, and <see cref="Confusion"/> as the central "disorder" ellipse.</summary>
public enum CynefinDomain { Clear, Complicated, Complex, Chaotic, Confusion }

/// <summary>One labelled point placed inside a Cynefin domain (a quoted string in a domain block).</summary>
public sealed class CynefinItem
{
    public required string Text { get; init; }
}

/// <summary>A directed <c>domainA --&gt; domainB</c> movement between two domains, with an optional label.</summary>
public sealed class CynefinTransition
{
    public required CynefinDomain From { get; init; }
    public required CynefinDomain To   { get; init; }
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// Data model for a Mermaid <c>cynefin-beta</c> diagram — the five fixed sense-making domains, the
/// items placed in each, and the transitions between them.  The central <see cref="CynefinDomain.Confusion"/>
/// domain shows at most <see cref="ConfusionMaxBadges"/> items with a <c>+N more</c> overflow badge.
/// Independent of the graph/Sugiyama pipeline (the layout is a fixed 2×2 grid).
/// </summary>
public sealed class CynefinDiagram
{
    /// <summary>Items shown inside the central confusion ellipse before a <c>+N more</c> badge is used.</summary>
    public const int ConfusionMaxBadges = 3;

    public string Title { get; set; } = string.Empty;

    private readonly Dictionary<CynefinDomain, List<CynefinItem>> _items = new()
    {
        [CynefinDomain.Clear]       = [],
        [CynefinDomain.Complicated] = [],
        [CynefinDomain.Complex]     = [],
        [CynefinDomain.Chaotic]     = [],
        [CynefinDomain.Confusion]   = [],
    };

    public List<CynefinTransition> Transitions { get; } = [];
    public CynefinConfig Config { get; set; } = new();

    /// <summary>The items placed in <paramref name="domain"/> (declaration order).</summary>
    public List<CynefinItem> ItemsIn(CynefinDomain domain) => _items[domain];

    /// <summary>The count of confusion items that overflow past <see cref="ConfusionMaxBadges"/> (0 when none).</summary>
    public int ConfusionOverflow => Math.Max(0, _items[CynefinDomain.Confusion].Count - ConfusionMaxBadges);
}
