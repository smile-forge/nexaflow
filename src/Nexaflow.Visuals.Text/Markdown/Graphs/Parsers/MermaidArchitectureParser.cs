using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>architecture-beta</c> diagrams.
///
/// Syntax:
/// <code>
/// architecture-beta
///   group api(cloud)[API]                       (group id, optional (icon), optional [title])
///   group db(database)[Storage] in api          (nested group via "in {parent}")
///   service server(server)[Server] in api       (service id, optional (icon)/[title], optional group)
///   junction j1 in api                          (a 4-way routing node)
///   db:R -- L:server                            (edge: {id}{group}?:SIDE {&lt;}?--{&gt;}? SIDE:{id}{group}?)
///   server{group}:B --> T:db{group}             (cross-group edge via the {group} suffix)
///   align row server db                          (same y); align column server db (same x)
/// </code>
/// Sides are <c>T</c>/<c>B</c>/<c>L</c>/<c>R</c>; arrows carry an optional <c>&lt;</c>/<c>&gt;</c> head at
/// either end.  The front-matter <c>config:</c> block is parsed by <see cref="ArchitectureConfigParser"/>.
/// Never throws; returns a (possibly empty) diagram.
/// </summary>
public sealed class MermaidArchitectureParser
{
    public bool CanParse(string language) =>
        language.StartsWith("architecture", StringComparison.OrdinalIgnoreCase);

    public ArchitectureDiagram Parse(string source)
    {
        var diagram = new ArchitectureDiagram();
        try { ParseInto(source, diagram); }
        catch { /* never throw; return partial diagram */ }
        return diagram;
    }

    // {id}{group}?:SIDE  {<}?--{>}?  SIDE:{id}{group}?
    private static readonly Regex RxEdge = new(
        @"^(?<lid>[A-Za-z0-9_\-]+)(?<lg>\{group\})?\s*:\s*(?<ls>[TBLR])\s*(?<lt><)?--(?<gt>>)?\s*(?<rs>[TBLR])\s*:\s*(?<rid>[A-Za-z0-9_\-]+)(?<rg>\{group\})?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RxIcon  = new(@"\(([^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex RxTitle = new(@"\[([^\]]*)\]", RegexOptions.Compiled);

    private static void ParseInto(string source, ArchitectureDiagram diagram)
    {
        foreach (var rawLine in source.Replace("\r\n", "\n").Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;

            string first = line.Split(' ', '\t')[0].ToLowerInvariant();
            if (first is "architecture-beta" or "architecture") continue;
            if (first is "acctitle" or "accdescr") continue;

            if (first == "group")
            {
                var (id, icon, title, parent) = ParseHead(line[5..]);
                if (id.Length == 0) continue;
                var g = diagram.FindGroup(id) ?? Add(diagram, id);
                g.Icon = icon; g.Title = title; g.ParentId = parent;
                continue;
            }

            if (first == "service")
            {
                var (id, icon, title, parent) = ParseHead(line[7..]);
                if (id.Length == 0) continue;
                var s = GetOrAddService(diagram, id);
                s.Icon = icon; s.Title = title; s.GroupId = parent;
                continue;
            }

            if (first == "junction")
            {
                var (id, _, _, parent) = ParseHead(line[8..]);
                if (id.Length == 0) continue;
                var s = GetOrAddService(diagram, id);
                s.IsJunction = true; s.GroupId = parent;
                continue;
            }

            if (first == "align")
            {
                ParseAlign(line[5..].Trim(), diagram);
                continue;
            }

            var em = RxEdge.Match(line);
            if (em.Success) { AddEdge(em, diagram); continue; }
        }
    }

    private static ArchGroup Add(ArchitectureDiagram d, string id)
    {
        var g = new ArchGroup { Id = id };
        d.Groups.Add(g);
        return g;
    }

    private static ArchService GetOrAddService(ArchitectureDiagram d, string id)
    {
        var s = d.FindService(id);
        if (s is null) { s = new ArchService { Id = id }; d.Services.Add(s); }
        return s;
    }

    /// <summary>Splits a group/service head into (id, icon, title, parent): peels <c>(icon)</c> and
    /// <c>[title]</c>, then reads the leading id and an optional <c>in {parent}</c> from the remainder.</summary>
    private static (string id, string? icon, string title, string? parent) ParseHead(string rest)
    {
        rest = rest.Trim();

        string? icon = null;
        var im = RxIcon.Match(rest);
        if (im.Success) { icon = im.Groups[1].Value.Trim(); rest = rest.Remove(im.Index, im.Length); }

        string title = string.Empty;
        var tm = RxTitle.Match(rest);
        if (tm.Success) { title = tm.Groups[1].Value.Trim(); rest = rest.Remove(tm.Index, tm.Length); }

        var tokens = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string id = tokens.Length > 0 ? tokens[0] : string.Empty;
        string? parent = null;
        for (int i = 1; i < tokens.Length - 1; i++)
            if (tokens[i].Equals("in", StringComparison.OrdinalIgnoreCase)) { parent = tokens[i + 1]; break; }

        return (id, string.IsNullOrEmpty(icon) ? null : icon, title, parent);
    }

    private static void AddEdge(Match m, ArchitectureDiagram d)
    {
        var edge = new ArchEdge
        {
            FromId      = m.Groups["lid"].Value,
            FromSide    = Side(m.Groups["ls"].Value),
            FromIsGroup = m.Groups["lg"].Success,
            ToId        = m.Groups["rid"].Value,
            ToSide      = Side(m.Groups["rs"].Value),
            ToIsGroup   = m.Groups["rg"].Success,
            StartArrow  = m.Groups["lt"].Success,
            EndArrow    = m.Groups["gt"].Success,
        };
        // Auto-create referenced services (a group endpoint is validated against declared groups only).
        if (!edge.FromIsGroup) GetOrAddService(d, edge.FromId);
        if (!edge.ToIsGroup)   GetOrAddService(d, edge.ToId);
        d.Edges.Add(edge);
    }

    private static void ParseAlign(string rest, ArchitectureDiagram d)
    {
        var tokens = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return;
        bool isRow = tokens[0].Equals("row", StringComparison.OrdinalIgnoreCase);
        bool isCol = tokens[0].Equals("column", StringComparison.OrdinalIgnoreCase);
        if (!isRow && !isCol) return;

        var a = new ArchAlignment { IsRow = isRow };
        for (int i = 1; i < tokens.Length; i++) a.Ids.Add(tokens[i]);
        if (a.Ids.Count > 0) d.Alignments.Add(a);
    }

    private static ArchSide Side(string s) => s.ToUpperInvariant() switch
    {
        "L" => ArchSide.Left,
        "R" => ArchSide.Right,
        "T" => ArchSide.Top,
        _   => ArchSide.Bottom,   // "B"
    };

    private static string StripComment(string line)
    {
        int idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }
}
