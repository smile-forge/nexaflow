namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>swimlane-beta</c> diagrams into a <see cref="Graph"/>.
///
/// Swimlane syntax is flowchart syntax where every top-level <c>subgraph … end</c> is a lane, so this
/// parser rewrites the <c>swimlane-beta [DIR]</c> header to an equivalent <c>flowchart [DIR]</c> header
/// (dropping <c>accTitle</c>/<c>accDescr</c> accessibility lines) and delegates the heavy lifting —
/// node shapes, edge styles, subgraph nesting — to <see cref="MermaidParser"/>.  The resulting graph's
/// top-level subgraphs (<see cref="Subgraph.ParentId"/> is null) are the lanes.  Never throws.
/// </summary>
public sealed class MermaidSwimlaneParser
{
    private static readonly MermaidParser Flow = new();

    public bool CanParse(string language) =>
        language.StartsWith("swimlane", StringComparison.OrdinalIgnoreCase);

    public Graph Parse(string source)
    {
        try { return ParseInternal(source); }
        catch { return new Graph(); }
    }

    private static Graph ParseInternal(string source)
    {
        var dir = GraphDirection.TopDown;
        var body = new List<string>();
        bool headerConsumed = false;

        foreach (var raw in source.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = raw.Trim();

            if (!headerConsumed)
            {
                if (trimmed.Length == 0 || trimmed.StartsWith("%%")) { body.Add(raw); continue; }
                string first = trimmed.Split(' ', '\t')[0].ToLowerInvariant();
                if (first.StartsWith("swimlane"))
                {
                    string rest = trimmed[first.Length..].Trim();
                    if (rest.Length > 0) dir = ParseDirection(rest.Split(' ', '\t')[0]);
                    headerConsumed = true;
                    continue;   // drop the swimlane header; a flowchart header is prepended below
                }
            }

            string low = trimmed.ToLowerInvariant();
            if (low.StartsWith("acctitle") || low.StartsWith("accdescr")) continue;   // accessibility metadata
            body.Add(raw);
        }

        string rewritten = $"flowchart {DirToken(dir)}\n{string.Join('\n', body)}";
        var graph = Flow.Parse(rewritten);
        graph.Direction = dir;
        return graph;
    }

    private static GraphDirection ParseDirection(string d) => d.ToUpperInvariant() switch
    {
        "LR"         => GraphDirection.LeftRight,
        "RL"         => GraphDirection.RightLeft,
        "BT"         => GraphDirection.BottomUp,
        "TD" or "TB" => GraphDirection.TopDown,
        _            => GraphDirection.TopDown,
    };

    private static string DirToken(GraphDirection d) => d switch
    {
        GraphDirection.LeftRight => "LR",
        GraphDirection.RightLeft => "RL",
        GraphDirection.BottomUp  => "BT",
        _                        => "TB",
    };
}
