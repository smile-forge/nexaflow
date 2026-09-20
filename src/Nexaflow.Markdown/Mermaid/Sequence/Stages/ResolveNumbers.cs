using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Sequence.Stages;

/// <summary>
/// Says what number each message is given. <c>autonumber</c> is written once and numbers every message under it, from where it
/// says and by as much as it says, until <c>autonumber off</c> — so the number a message carries is a fact about every line
/// above it rather than about its own, and nothing on the line itself says it.
/// </summary>
/// <param name="numbered">Whether the front matter asks for numbering without a line saying so — <c>showSequenceNumbers</c>.</param>
public sealed class ResolveNumbers(bool numbered) : IAstStage
{
    public string Name => "sequence:numbers";

    public ContentNode Run(ContentNode tree)
    {
        var on = numbered;
        var next = 1d;
        var step = 1d;
        var said = new Dictionary<ContentNode, string>();

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Line))
        {
            if (line.Stated() is not { } stated) continue;

            if (stated.Kind == SequenceKinds.Numbering)
            {
                if (Told(stated, MermaidKinds.Setting, SequenceRoles.Off) is not null)
                {
                    on = false;
                    continue;
                }

                on = true;
                next = Counted(stated, SequenceRoles.Start) ?? 1;
                step = Counted(stated, SequenceRoles.Step) ?? 1;
                continue;
            }

            if (stated.Kind != SequenceKinds.Message || !on) continue;

            said[stated] = next.ToString("0.##", CultureInfo.InvariantCulture);
            next += step;
        }

        if (said.Count == 0) return tree;

        return AstRewrite.Each(tree, node => said.TryGetValue(node, out var number)
            ? node.Saying(SequenceKinds.Fact, SequenceRoles.Number, number)
            : node);
    }

    private static double? Counted(ContentNode stated, string role) =>
        MermaidNumber.Read(Told(stated, MermaidKinds.Number, role));

    /// <summary>What a line says in a kind and a role, wherever it is written inside it — or null where nothing is.</summary>
    private static string? Told(ContentNode stated, string kind, string role) =>
        stated.SelfAndDescendants()
              .FirstOrDefault(inner => inner.Kind == kind && inner.Role == role && inner.Width > 0)?.Text;
}
