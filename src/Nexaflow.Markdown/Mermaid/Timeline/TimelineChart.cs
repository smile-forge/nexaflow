
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Timeline;

/// <summary>Which way a timeline runs: its periods across the page, or down it.</summary>
public enum TimelineWay { LeftToRight, TopDown }

/// <summary>Text somebody wrote: what it says, and the hole standing where it is still to write.</summary>
/// <param name="Part">The piece it was written as — what pressing it means.</param>
public sealed record TimelineText(ContentPart Part, ContentPart Says, ContentPart? Hole);

/// <summary>One event: what it says, written after a colon on its period's line or on a line of its own.</summary>
public sealed record TimelineEvent(TimelineText Says, int Order);

/// <summary>One period: what it is called, and the events written for it in the order they are written.</summary>
/// <param name="Part">The line it was written on — what pressing it means.</param>
public sealed record TimelinePeriod(ContentPart Part, TimelineText Says, IReadOnlyList<TimelineEvent> Events, int Order);

/// <summary>A group of periods: the <c>section</c> line naming it, or nothing for the periods written before any section.</summary>
public sealed record TimelineSection(ContentPart? Part, TimelineText? Name, IReadOnlyList<TimelinePeriod> Periods, int Order);

/// <summary>
/// A <c>timeline</c> block, read: which way it runs, the sections grouping its periods, and each period with its events.
/// Its title is the block's (<see cref="MermaidBlock.Title"/>).
///
/// <para>
/// Periods written before any section are a section of their own with no name, as Mermaid groups them. A line of further
/// events belongs to the period above it, and one written before any period says so where it is written
/// (<see cref="Stages.ResolveSections"/>).
/// </para>
/// </summary>
public sealed class TimelineChart
{
    private TimelineChart(MermaidBlock block, TimelineConfig config) => (Block, Config) = (block, config);

    

    public static TimelineChart Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    public static TimelineChart Of(MermaidBlock block)
    {
        var chart = new TimelineChart(block, TimelineConfig.Read(block.Config));

        var named = new List<(ContentPart Part, TimelineText? Name)>();
        var periods = new List<(ContentPart Part, TimelineText Says, List<TimelineEvent> Events, int Section)>();

        foreach (var part in block.Reading.Root.SelfAndDescendants())
        {
            switch (part.Kind)
            {
                case TimelineKinds.Direction when Running(part) is { } way:
                    chart.Way = way;
                    break;

                case TimelineKinds.Section:
                    named.Add((part, Text(part, TimelineRoles.Name)));
                    break;

                case TimelineKinds.Period when Text(part, TimelineRoles.Says) is { } says:
                    periods.Add((part, says, [.. Events(part, 0)], Number(part.Fact(TimelineRoles.In))));
                    break;

                case TimelineKinds.More when Number(part.Fact(TimelineRoles.Of)) is var of && of >= 0 && of < periods.Count:
                    periods[of].Events.AddRange(Events(part, periods[of].Events.Count));
                    break;
            }
        }

        var sections = new List<TimelineSection>();

        // Periods written before any section are a section of their own, with nothing naming it.
        if (periods.Any(period => period.Section < 0))
            sections.Add(new TimelineSection(null, null, [.. Made(periods, -1)], 0));

        for (var at = 0; at < named.Count; at++)
            sections.Add(new TimelineSection(named[at].Part, named[at].Name, [.. Made(periods, at)], sections.Count));

        chart.Sections = sections;
        chart.Sectioned = named.Count > 0;
        return chart;
    }

    public MermaidBlock Block { get; }

    public TimelineConfig Config { get; }

    /// <summary>Which way it runs — what the last line saying so asks for, or across the page.</summary>
    public TimelineWay Way { get; private set; } = TimelineWay.LeftToRight;

    /// <summary>The sections, in the order they are written.</summary>
    public IReadOnlyList<TimelineSection> Sections { get; private set; } = [];

    /// <summary>Whether anything is written as a section, which is what makes a section's colour its periods'.</summary>
    public bool Sectioned { get; private set; }

    /// <summary>Every period, in the order it is written.</summary>
    public IEnumerable<TimelinePeriod> Periods => Sections.SelectMany(section => section.Periods);

    /// <summary>Whether nothing is written for the diagram to draw.</summary>
    public bool Empty => !Periods.Any();

    /// <summary>The periods of one section, made in the order they are written.</summary>
    private static IEnumerable<TimelinePeriod> Made(
        List<(ContentPart Part, TimelineText Says, List<TimelineEvent> Events, int Section)> periods, int section)
    {
        var order = 0;
        foreach (var period in periods)
            if (period.Section == section)
                yield return new TimelinePeriod(period.Part, period.Says, [.. period.Events], order++);
    }

    /// <summary>The events written on a line, numbered on from <paramref name="from"/> — what says nothing is nothing written.</summary>
    private static IEnumerable<TimelineEvent> Events(ContentPart line, int from)
    {
        foreach (var text in line.Children.Where(child => child.Kind == TimelineKinds.Text && child.Role == TimelineRoles.Event))
        {
            if (text.Words() is not { } says || (says.Length == 0 && text.Hole() is null)) continue;

            yield return new TimelineEvent(new TimelineText(text, says, text.Hole()), from++);
        }
    }

    private static TimelineWay? Running(ContentPart line) =>
        line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Setting) is { Trouble: null } way
            ? way.Text.Trim().ToUpperInvariant() switch { "LR" => TimelineWay.LeftToRight, "TD" or "TB" => TimelineWay.TopDown, _ => null }
            : null;

    /// <summary>The number a stage worked out and hung under a line — which line a period or its events belong to — or -1 for none.</summary>
    private static int Number(string? said) => MermaidNumber.Read(said) is { } number ? (int)number : -1;

    private static TimelineText? Text(ContentPart line, string role) =>
        line.Children.FirstOrDefault(child => child.Kind == TimelineKinds.Text && child.Role == role) is { } text && text.Words() is { } says
            ? new TimelineText(text, says, text.Hole())
            : null;
}
