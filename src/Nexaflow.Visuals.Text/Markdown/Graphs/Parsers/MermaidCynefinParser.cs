using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>cynefin-beta</c> diagrams.
///
/// Syntax:
/// <code>
/// cynefin-beta
///   title Making sense                       (optional diagram title)
///   complex                                   (a fixed domain keyword opens a block)
///     "Investigate root cause"                (indented quoted string → an item in that domain)
///     "Run chaos experiment"
///   clear
///     "Apply best practice"
///   complex --> complicated : "Pattern found" (a transition between two domains, label optional)
/// </code>
/// The only domain keywords recognised are <c>clear</c>, <c>complicated</c>, <c>complex</c>,
/// <c>chaotic</c> and <c>confusion</c>; anything else on an item line is taken as item text for the
/// most-recent domain.  The front-matter <c>config:</c> block is parsed separately by
/// <see cref="CynefinConfigParser"/>.  Never throws; returns a (possibly empty) diagram.
/// </summary>
public sealed class MermaidCynefinParser
{
    public bool CanParse(string language) =>
        language.StartsWith("cynefin", StringComparison.OrdinalIgnoreCase);

    public CynefinDiagram Parse(string source)
    {
        var diagram = new CynefinDiagram();
        try { ParseInto(source, diagram); }
        catch { /* never throw; return partial diagram */ }
        return diagram;
    }

    private static void ParseInto(string source, CynefinDiagram diagram)
    {
        CynefinDomain? current = null;

        foreach (var rawLine in source.Replace("\r\n", "\n").Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;

            string first = line.Split(' ', '\t')[0].ToLowerInvariant();
            if (first is "cynefin-beta" or "cynefin") continue;
            if (first is "acctitle" or "accdescr") continue;

            // title <text>
            if (first == "title" && (line.Length == 5 || char.IsWhiteSpace(line[5])))
            {
                diagram.Title = Dequote(line.Length > 5 ? line[5..].Trim() : string.Empty);
                continue;
            }

            // Transition: domainA --> domainB [: label]
            if (line.Contains("-->", StringComparison.Ordinal) && TryParseTransition(line, diagram))
                continue;

            // A bare domain keyword opens (or re-opens) that domain's block.
            if (TryParseDomain(line, out var domain))
            {
                current = domain;
                continue;
            }

            // Otherwise the line is an item in the current domain.
            if (current is CynefinDomain d)
            {
                string text = Dequote(line);
                if (text.Length > 0) diagram.ItemsIn(d).Add(new CynefinItem { Text = text });
            }
        }
    }

    /// <summary>Parses a <c>domainA --&gt; domainB : label</c> line; false when either end is not a domain.</summary>
    private static bool TryParseTransition(string line, CynefinDiagram diagram)
    {
        int arrow = line.IndexOf("-->", StringComparison.Ordinal);
        string leftText = line[..arrow].Trim();
        string rightAll = line[(arrow + 3)..].Trim();

        string label = string.Empty;
        int colon = rightAll.IndexOf(':');
        if (colon >= 0)
        {
            label    = Dequote(rightAll[(colon + 1)..].Trim());
            rightAll = rightAll[..colon].Trim();
        }

        if (!TryParseDomain(leftText, out var from) || !TryParseDomain(rightAll, out var to))
            return false;

        diagram.Transitions.Add(new CynefinTransition { From = from, To = to, Label = label });
        return true;
    }

    private static bool TryParseDomain(string text, out CynefinDomain domain)
    {
        domain = default;
        switch (text.Trim().ToLowerInvariant())
        {
            case "clear":       domain = CynefinDomain.Clear;       return true;
            case "complicated": domain = CynefinDomain.Complicated; return true;
            case "complex":     domain = CynefinDomain.Complex;     return true;
            case "chaotic":     domain = CynefinDomain.Chaotic;     return true;
            case "confusion":   domain = CynefinDomain.Confusion;   return true;
            default:            return false;
        }
    }

    private static string Dequote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            s = s[1..^1];
        return s.Trim();
    }

    private static string StripComment(string line)
    {
        int idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }
}
