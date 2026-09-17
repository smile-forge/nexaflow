using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Gantt.Stages;

/// <summary>
/// Says what is wrong with a gantt chart's schedule that only the whole chart shows: a start or an end that is no date in the
/// chart's <c>dateFormat</c> (and no length of time, for an end); an id that <c>after</c>, <c>until</c> or <c>click</c> names and
/// no task has; a day <c>excludes</c> or <c>includes</c> names that is none; and a first task that says nothing of when it starts,
/// which has no task before it to start after.
/// </summary>
public sealed class ResolveSchedule : IAstStage
{
    public string Name => "gantt:schedule";

    public ContentNode Run(ContentNode tree)
    {
        var format = Last(tree, "dateFormat") ?? MermaidDate.Default;
        var ids = tree.SelfAndDescendants()
            .Where(node => node is { Kind: MermaidKinds.Name, Role: GanttRoles.Id })
            .Select(node => node.Inner(MermaidKinds.Words)?.Text.Trim() ?? string.Empty)
            .Where(id => id.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var scheduled = false;

        // A tree is rewritten in the order it is written, so the first schedule met is the first task's.
        return AstRewrite.Each(tree, node =>
        {
            if (node.Kind == GanttKinds.Schedule && !scheduled)
            {
                scheduled = true;
                if (!node.Children.Any(child => child.Role == GanttRoles.Start) && node.Trouble is null)
                    return node.Saying("The first task says when it starts, with no task before it to start after: Design :2014-01-01, 3d.");
            }

            if (!node.IsLeaf || node.Text.Trim() is not { Length: > 0 } said) return node;

            return node.Role switch
            {
                GanttRoles.Start when !Starts(said, format) => node.Saying($"'{said}' is no date written as {format}."),
                GanttRoles.End when MermaidDate.Read(said, format) is null && MermaidDuration.Read(said) is null =>
                    node.Saying($"'{said}' is neither a date written as {format} nor a length of time, such as 3d."),
                GanttRoles.Reference when !ids.Contains(said) => node.Saying($"No task has the id {said}."),
                _ when node.Role == GanttRoles.Of("excludes") || node.Role == GanttRoles.Of("includes") =>
                    Days(said, format) is { } wrong ? node.Saying($"'{wrong}' is no date, day of the week or weekends.") : node,
                _ => node,
            };
        });
    }

    /// <summary>What the last line setting <paramref name="word"/> says, where one does.</summary>
    internal static string? Last(ContentNode tree, string word) =>
        tree.SelfAndDescendants()
            .Where(node => node.Kind == GanttKinds.Setting)
            .Select(node => node.Children.FirstOrDefault(child => child.Role == GanttRoles.Of(word))?.Text.Trim())
            .LastOrDefault(said => said is { Length: > 0 });

    /// <summary>Whether a start is a date: a stamp, in the chart's format, or written as any date is.</summary>
    internal static bool Starts(string said, string format) =>
        (format is "x" or "X" && said.All(char.IsAsciiDigit)) || MermaidDate.Read(said, format) is not null || MermaidDate.Loosely(said) is not null;

    /// <summary>The first thing an <c>excludes</c> or <c>includes</c> line names that is no date, day of the week or <c>weekends</c>.</summary>
    private static string? Days(string said, string format) =>
        said.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(day =>
            !day.Equals("weekends", StringComparison.OrdinalIgnoreCase)
            && !GanttGrammar.Weekdays.Contains(day.ToLowerInvariant())
            && MermaidDate.Read(day, format) is null
            && MermaidDate.Read(day, MermaidDate.Default) is null);
}

