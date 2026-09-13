using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Syntax;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// Several edits made as one. Each step is planned against the files as the steps before it left them, nothing is
/// written until every step has planned cleanly, and a file several steps touch comes out as one change — so a
/// change that spans files either lands whole or not at all, and never leaves the tree half-way through a refactor.
/// <para>
/// Planning only, like <see cref="GraphEdit"/>: it reads through a delegate and writes nothing. The caller writes
/// <see cref="Outcome.Files"/>, after checking none of them changed underneath it.
/// </para>
/// </summary>
public static class EditPlan
{
    /// <summary>One thing to do. <see cref="Label"/> is how a refusal names it — a script's line, say.</summary>
    public abstract record Step(string Label);

    /// <summary>An edit to one declaration, file or element: every op <see cref="GraphEdit.Plan"/> takes.</summary>
    public sealed record Edit(string Label, string NodeId, StructuralEdit.Op Op, string? Text,
                              StructuralEdit.Options? Options = null, string? RenameTo = null) : Step(Label);

    /// <summary>A new file.</summary>
    public sealed record Create(string Label, string RelativePath, string? Text) : Step(Label);

    /// <summary>A declaration moved into a type (<c>code:</c>) or to a file (<c>file:</c>, created if absent).</summary>
    public sealed record Move(string Label, string NodeId, string Destination) : Step(Label);

    /// <summary>A file's text replaced whole by a caller that worked it out — see <see cref="GraphEdit.Rewrite"/>.</summary>
    public sealed record Rewrite(string Label, string RelativePath, string Before, string After, string Description) : Step(Label);

    /// <summary>What one step did, against the text the steps before it had left.</summary>
    public sealed record Planned(Step Step, string Message, IReadOnlyList<GraphEdit.FileChange> Changes,
                                 IReadOnlyList<string> Notes);

    /// <summary>A file as it was before any step and as the last one left it. <see cref="Before"/> is null for a
    /// file that did not exist, <see cref="After"/> for one that no longer does.</summary>
    public sealed record Written(string RelativePath, string? Before, string? After);

    public sealed record Outcome(bool Ok, string Message, IReadOnlyList<Planned> Steps, IReadOnlyList<Written> Files);

    public static Outcome Run(KnowledgeGraph graph, IReadOnlyList<Step> steps, GraphEdit.ReadText read,
                              Func<string, string> newlineFor)
    {
        var current = new Dictionary<string, string?>(StringComparer.Ordinal);
        var before  = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? Read(string rel) => current.TryGetValue(rel, out var text) ? text : read(rel);

        var planned = new List<Planned>();
        foreach (var step in steps)
        {
            var result = step switch
            {
                Edit e   => GraphEdit.Plan(graph, e.NodeId, e.Op, e.Text, Read, e.Options, e.RenameTo),
                Create c => GraphEdit.Create(c.RelativePath, c.Text, Read, newlineFor),
                Move m   => GraphEdit.Move(graph, m.NodeId, m.Destination, Read, newlineFor),
                Rewrite r => GraphEdit.Rewrite(r.RelativePath, r.Before, r.After, r.Description, Read),
                _        => GraphEdit.Result.Fail($"{step.GetType().Name} is not a step this can plan."),
            };

            // Naming the step only matters when there is more than one to tell apart.
            if (!result.Ok)
                return new Outcome(false, steps.Count == 1 ? result.Message : $"{step.Label}: {result.Message}",
                                   planned, []);

            foreach (var change in result.Changes)
            {
                if (!before.ContainsKey(change.RelativePath)) before[change.RelativePath] = read(change.RelativePath);
                current[change.RelativePath] = change.Kind == GraphEdit.ChangeKind.Deleted ? null : change.NewText;
            }
            planned.Add(new Planned(step, result.Message, result.Changes, result.Notes));
        }

        var files = before.Keys
            .Where(rel => !string.Equals(before[rel], current[rel], StringComparison.Ordinal))
            .Select(rel => new Written(rel, before[rel], current[rel]))
            .ToList();

        return new Outcome(true, planned.Count == 1 ? planned[0].Message : $"{planned.Count} edits to {files.Count} file(s)",
                           planned, files);
    }
}
