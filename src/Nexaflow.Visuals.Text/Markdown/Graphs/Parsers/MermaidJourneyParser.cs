using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>journey</c> (user journey) diagrams.
///
/// Syntax:
/// <code>
/// journey
///     title My working day            (optional)
///     section Go to work              (optional grouping)
///       Make tea: 5: Me               (task: score 1–5: comma-separated actors)
///       Do work: 1: Me, Cat
///       Sit down: 5                   (a task may have no actors)
/// </code>
/// A score that is missing or not a number reads as 3 (neutral); one outside 1–5 is clamped.
/// Anything after the second colon is the actor list, so a stray colon in an actor's name stays
/// with the actor rather than losing data.  <c>accTitle</c>/<c>accDescr</c> are dropped.  The
/// front-matter <c>config:</c> block is parsed separately by <see cref="JourneyConfigParser"/>.
/// Never throws; returns a (possibly empty) diagram.
/// </summary>
public sealed class MermaidJourneyParser
{
    public bool CanParse(string language) =>
        language.Equals("journey", StringComparison.OrdinalIgnoreCase);

    public JourneyDiagram Parse(string source)
    {
        var diagram = new JourneyDiagram();
        try { ParseInto(source, diagram); }
        catch { /* never throw; return partial diagram */ }
        return diagram;
    }

    private static void ParseInto(string source, JourneyDiagram diagram)
    {
        JourneySection? section = null;

        JourneySection SectionFor()
        {
            if (section is null) { section = new JourneySection { Name = string.Empty }; diagram.Sections.Add(section); }
            return section;
        }

        foreach (var rawLine in source.Replace("\r\n", "\n").Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;

            string first = line.Split(' ', '\t')[0].ToLowerInvariant();
            string rest  = line.Length > first.Length ? line[first.Length..].Trim() : string.Empty;

            if (first == "journey") continue;
            if (first.TrimEnd(':') is "acctitle" or "accdescr") continue;   // accTitle: text
            if (first == "title")   { diagram.Title = rest; continue; }
            if (first == "section")
            {
                section = new JourneySection { Name = rest };
                diagram.Sections.Add(section);
                continue;
            }

            // Task name: score: actor, actor
            var parts = line.Split(':');
            if (parts.Length < 2) continue;                       // not a task line
            string name = parts[0].Trim();
            if (name.Length == 0) continue;

            int score = int.TryParse(parts[1].Trim(), out var s) ? Math.Clamp(s, 1, 5) : 3;
            var task = new JourneyTask { Name = name, Score = score };
            if (parts.Length > 2)
                task.Actors.AddRange(string.Join(':', parts[2..])
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

            SectionFor().Tasks.Add(task);
        }
    }

    private static string StripComment(string line)
    {
        int idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }
}
