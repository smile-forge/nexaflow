using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Venn.Stages;

/// <summary>
/// Gathers each <c>set</c> and <c>union</c> with the <c>text</c> items written in it into one
/// <see cref="VennKinds.Region"/>, so the tree says what the diagram is — regions holding their items — rather than only
/// the lines it was written on.
///
/// <para>
/// Which items belong to a region is Mermaid's indentation rule: the <c>text</c> lines indented under a set or a union,
/// one after another, are in it. Comments and blank lines between them go with them; after the last item they are left
/// where they are, since nothing says they belong to the region rather than to whatever comes next. An item written
/// anywhere else — at the start of a line naming its region first, or indented after something that is not an item — is
/// not moved into a region it was not written in: the tree only ever re-nests what is already next to each other, and
/// <see cref="ResolveRegions"/> says which region such an item sits in.
/// </para>
/// </summary>
public sealed class GroupRegions : IAstStage
{
    public string Name => "venn:group-regions";

    public ContentNode Run(ContentNode tree) =>
        AstRewrite.Regrouping(tree, (node, children) => node.Kind == MermaidKinds.Block ? Grouped(children) : null);

    private static List<ContentNode>? Grouped(IReadOnlyList<ContentNode> lines)
    {
        var grouped = new List<ContentNode>(lines.Count);
        var moved = false;

        for (var at = 0; at < lines.Count;)
        {
            if (Said(lines[at])?.Kind is not (VennKinds.Set or VennKinds.Union))
            {
                grouped.Add(lines[at++]);
                continue;
            }

            // Past the last line the region takes: its own, and each item indented under it.
            var end = at + 1;
            for (var next = at + 1; next < lines.Count; next++)
            {
                var said = Said(lines[next]);
                if (said is null || said.Kind is Kinds.Comment or MermaidKinds.Directive) continue;
                if (said.Kind != VennKinds.Text || !Indented(lines[next]) || said.Part(VennRoles.Region) is not null) break;

                end = next + 1;
            }

            grouped.Add(ContentNode.Branch(VennKinds.Region, [.. lines.Skip(at).Take(end - at)]));
            moved = true;
            at = end;
        }

        return moved ? grouped : null;
    }

    /// <summary>What a line says — the first thing on it that is not the space before it — or null for one that says nothing.</summary>
    internal static ContentNode? Said(ContentNode line) =>
        line.Kind == MermaidKinds.Line ? line.Children.FirstOrDefault(child => child.Kind != Kinds.Space) : null;

    /// <summary>Whether a line starts with space rather than at the start of its row.</summary>
    internal static bool Indented(ContentNode line) => line.Children is [{ Kind: Kinds.Space }, _, ..];
}
