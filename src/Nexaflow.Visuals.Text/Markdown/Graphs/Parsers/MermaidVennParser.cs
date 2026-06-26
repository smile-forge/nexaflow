using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>venn-beta</c> diagrams.
///
/// Syntax:
/// <code>
/// venn-beta
///   title "…"
///   set A["Alpha"]:20          (a circle; optional label + area-weight size)
///   set B["Beta"]
///     text B1["item"]          (indented → an item inside the most recent set/union)
///   union A,B["AB"]:3          (comma = intersection of 2+ sets)
///   text A,B AB1["item"]       (explicit region for an item)
///   style A fill:#ff6b6b       (fill / color / stroke / stroke-width / fill-opacity)
/// </code>
/// Comma is the only intersection operator; spaces in a name need quotes.  The front-matter
/// <c>config:</c> block is parsed separately by <see cref="VennConfigParser"/>.
/// </summary>
public sealed class MermaidVennParser
{
    public bool CanParse(string language) =>
        language.StartsWith("venn", StringComparison.OrdinalIgnoreCase);

    public VennDiagram Parse(string source)
    {
        var diagram = new VennDiagram();
        try { ParseInto(source, diagram); }
        catch { /* never throw; return partial diagram */ }
        return diagram;
    }

    private static readonly Regex RxSize  = new(@":(?<n>[+-]?(?:\d+(?:\.\d+)?|\.\d+))\s*$", RegexOptions.Compiled);
    private static readonly Regex RxLabel = new(@"\[(?<l>.*)\]\s*$", RegexOptions.Compiled);

    private static void ParseInto(string source, VennDiagram diagram)
    {
        List<VennItem>? current = null;   // Items of the most recent set/union (for indented text)

        foreach (var rawLine in source.Replace("\r\n", "\n").Split('\n'))
        {
            int indent = LeadingWhitespace(rawLine);
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;

            string first = line.Split(' ', '\t')[0].ToLowerInvariant();
            if (first is "venn-beta" or "venn") continue;
            if (line.StartsWith("title", StringComparison.OrdinalIgnoreCase) && (line.Length == 5 || char.IsWhiteSpace(line[5])))
            { diagram.Title = Dequote(line.Length > 5 ? line[5..].Trim() : string.Empty); continue; }
            if (first is "acctitle" or "accdescr") continue;

            if (first == "set")
            {
                var (idPart, label, size) = ParseHead(line[3..]);
                var set = FindOrCreateSet(diagram, Dequote(idPart));
                if (label is not null) set.Label = label;
                if (size is not null) set.Size = size;
                current = set.Items;
                continue;
            }

            if (first == "union")
            {
                var (idPart, label, size) = ParseHead(line[5..]);
                var setIds = SplitIds(idPart);
                var union = FindOrCreateUnion(diagram, setIds);
                if (label is not null) union.Label = label;
                if (size is not null) union.Size = size;
                current = union.Items;
                continue;
            }

            if (first == "text")
            {
                string rest = line[4..].Trim();
                var items = current;

                if (indent == 0)
                {
                    int sp = rest.IndexOf(' ');
                    if (sp > 0)
                    {
                        string idList = rest[..sp].Trim();
                        items = ResolveRegion(diagram, idList) ?? current;
                        rest = rest[(sp + 1)..].Trim();
                    }
                }

                if (items is not null)
                {
                    var (id, lbl) = ParseTextNode(rest);
                    if (id.Length > 0) items.Add(new VennItem { Id = id, Label = lbl });
                }
                continue;
            }

            if (first == "style") { ParseStyle(line[5..].Trim(), diagram); continue; }
        }
    }

    // ── Regions ─────────────────────────────────────────────────────────────────

    private static VennSet FindOrCreateSet(VennDiagram d, string id)
    {
        var s = d.FindSet(id);
        if (s is null) { s = new VennSet { Id = id }; d.Sets.Add(s); }
        return s;
    }

    private static VennUnion FindOrCreateUnion(VennDiagram d, List<string> setIds)
    {
        var u = FindUnion(d, setIds);
        if (u is null)
        {
            foreach (var id in setIds) FindOrCreateSet(d, id);   // a union implies its sets
            u = new VennUnion { SetIds = setIds };
            d.Unions.Add(u);
        }
        return u;
    }

    private static VennUnion? FindUnion(VennDiagram d, List<string> setIds) =>
        d.Unions.FirstOrDefault(u => u.SetIds.Count == setIds.Count && u.SetIds.SequenceEqual(setIds, StringComparer.Ordinal));

    /// <summary>Resolves the Items list a <c>text</c> node targets: a union region (comma list) or a set.</summary>
    private static List<VennItem>? ResolveRegion(VennDiagram d, string idList)
    {
        if (idList.Contains(','))
            return FindOrCreateUnion(d, SplitIds(idList)).Items;
        return FindOrCreateSet(d, Dequote(idList)).Items;
    }

    // ── Styling ──────────────────────────────────────────────────────────────────

    private static void ParseStyle(string rest, VennDiagram d)
    {
        int sp = rest.IndexOf(' ');
        if (sp < 0) return;
        string idList = rest[..sp].Trim();
        var (fill, color, stroke, fillOpacity) = ReadProps(rest[(sp + 1)..]);

        if (idList.Contains(','))
        {
            if (FindUnion(d, SplitIds(idList)) is { } u)
            {
                if (fill  is not null) u.Fill = fill;
                if (color is not null) u.TextColor = color;
            }
            return;
        }

        string id = Dequote(idList);
        if (d.FindSet(id) is { } set)
        {
            if (fill        is not null) set.Fill = fill;
            if (stroke      is not null) set.Stroke = stroke;
            if (color       is not null) set.TextColor = color;
            if (fillOpacity is not null) set.FillOpacity = fillOpacity;
            return;
        }
        if (FindItem(d, id) is { } item && color is not null) item.TextColor = color;
    }

    private static (string? fill, string? color, string? stroke, double? fillOpacity) ReadProps(string props)
    {
        string? fill = null, color = null, stroke = null;
        double? fillOpacity = null;
        foreach (var prop in props.Split(','))
        {
            int c = prop.IndexOf(':');
            if (c < 0) continue;
            string k = prop[..c].Trim().ToLowerInvariant();
            string v = prop[(c + 1)..].Trim();
            switch (k)
            {
                case "fill":         fill = v; break;
                case "color":        color = v; break;
                case "stroke":       stroke = v; break;
                case "fill-opacity": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var o)) fillOpacity = o; break;
            }
        }
        return (fill, color, stroke, fillOpacity);
    }

    private static VennItem? FindItem(VennDiagram d, string id)
    {
        foreach (var s in d.Sets) if (s.Items.FirstOrDefault(i => i.Id == id) is { } it) return it;
        foreach (var u in d.Unions) if (u.Items.FirstOrDefault(i => i.Id == id) is { } it) return it;
        return null;
    }

    // ── Head / token parsing ──────────────────────────────────────────────────────

    /// <summary>Splits a set/union head into (idPart, label, size), peeling a trailing <c>:size</c> then <c>[label]</c>.</summary>
    private static (string idPart, string? label, double? size) ParseHead(string rest)
    {
        rest = rest.Trim();
        double? size = null;
        var sm = RxSize.Match(rest);
        if (sm.Success) { size = double.Parse(sm.Groups["n"].Value, CultureInfo.InvariantCulture); rest = rest[..sm.Index].Trim(); }

        string? label = null;
        var lm = RxLabel.Match(rest);
        if (lm.Success) { label = lm.Groups["l"].Value.Trim().Trim('"'); rest = rest[..lm.Index].Trim(); }

        return (rest.Trim(), label, size);
    }

    private static (string id, string label) ParseTextNode(string rest)
    {
        rest = rest.Trim();
        var lm = RxLabel.Match(rest);
        string label = lm.Success ? lm.Groups["l"].Value.Trim().Trim('"') : string.Empty;
        string id = (lm.Success ? rest[..lm.Index] : rest).Trim().Trim('"');
        return (id, label);
    }

    private static List<string> SplitIds(string idList) =>
        idList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
              .Select(Dequote)
              .OrderBy(s => s, StringComparer.Ordinal)   // sets are compared order-independently
              .ToList();

    private static string Dequote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
        return s.Trim();
    }

    private static int LeadingWhitespace(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return i;
    }

    private static string StripComment(string line)
    {
        int idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }
}
