using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>timeline</c> diagrams.
///
/// Syntax:
/// <code>
/// timeline
///     title History of Social Media Platform   (optional)
///     direction TD                             (optional; LR is the default)
///     section Early days                       (optional grouping; colours follow the section)
///     2002 : LinkedIn
///     2004 : Facebook : Google                 (several events on one line …)
///          : Orkut                             (… or on continuation lines starting with ':')
///     2005 : YouTube&lt;br&gt;launched               (&lt;br&gt; is a line break inside a label)
/// </code>
/// Every colon splits, exactly as in Mermaid, so a literal colon is written <c>#colon;</c>.
/// <c>accTitle</c>/<c>accDescr</c> are dropped as accessibility metadata.  The front-matter
/// <c>config:</c> block is parsed separately by <see cref="TimelineConfigParser"/>.  Never throws;
/// returns a (possibly empty) diagram.
/// </summary>
public sealed class MermaidTimelineParser
{
    private static readonly Regex RxBr = new(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool CanParse(string language) =>
        language.Equals("timeline", StringComparison.OrdinalIgnoreCase);

    public TimelineDiagram Parse(string source)
    {
        var diagram = new TimelineDiagram();
        try { ParseInto(source, diagram); }
        catch { /* never throw; return partial diagram */ }
        return diagram;
    }

    private static void ParseInto(string source, TimelineDiagram diagram)
    {
        TimelineSection? section = null;
        TimelinePeriod?  last    = null;

        TimelineSection SectionFor()
        {
            if (section is null) { section = new TimelineSection { Name = string.Empty }; diagram.Sections.Add(section); }
            return section;
        }

        foreach (var rawLine in source.Replace("\r\n", "\n").Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;

            // Continuation: more events for the most recent period.
            if (line[0] == ':')
            {
                if (last is not null) last.Events.AddRange(SplitEvents(line));
                continue;
            }

            string first = line.Split(' ', '\t')[0].ToLowerInvariant();
            string rest  = line.Length > first.Length ? line[first.Length..].Trim() : string.Empty;

            if (first == "timeline")
            {
                if (TryParseDirection(rest, out var headerDir)) diagram.Direction = headerDir;
                continue;
            }
            if (first.TrimEnd(':') is "acctitle" or "accdescr") continue;   // accTitle: text
            if (first == "title")     { diagram.Title = Unescape(rest); continue; }
            if (first == "direction") { if (TryParseDirection(rest, out var d)) diagram.Direction = d; continue; }
            if (first == "section")
            {
                section = new TimelineSection { Name = Unescape(rest) };
                diagram.Sections.Add(section);
                continue;
            }

            // <period> [: event]* — a bare line is a period with no events.
            int colon = line.IndexOf(':');
            string title = Unescape(colon < 0 ? line : line[..colon]);
            if (title.Length == 0) continue;

            var period = new TimelinePeriod { Title = title };
            if (colon >= 0) period.Events.AddRange(SplitEvents(line[colon..]));
            SectionFor().Periods.Add(period);
            last = period;
        }
    }

    private static bool TryParseDirection(string text, out TimelineDirection direction)
    {
        switch (text.Trim().ToUpperInvariant())
        {
            case "LR": direction = TimelineDirection.LeftToRight; return true;
            case "TD":
            case "TB": direction = TimelineDirection.TopDown;     return true;
            default:   direction = default;                       return false;
        }
    }

    /// <summary>Splits the <c>: a : b</c> tail of a line into events, dropping empties.</summary>
    private static IEnumerable<string> SplitEvents(string tail) =>
        tail.Split(':').Select(Unescape).Where(e => e.Length > 0);

    /// <summary>Turns <c>&lt;br&gt;</c> into a line break and decodes Mermaid's <c>#colon;</c> entity.</summary>
    private static string Unescape(string text) =>
        RxBr.Replace(text, "\n").Replace("#colon;", ":").Trim();

    private static string StripComment(string line)
    {
        int idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }
}
