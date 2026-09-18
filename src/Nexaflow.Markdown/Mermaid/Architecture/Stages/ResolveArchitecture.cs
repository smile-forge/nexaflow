using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Architecture.Stages;

/// <summary>
/// Says where a line names something the block does not declare, or declares something twice. Everything in an
/// architecture diagram is named by an id, and groups, services and junctions share one set of them, so what a name means
/// is a fact about the whole block rather than about the line naming it.
///
/// <para>
/// Mermaid asks for a group to be declared above whatever is put in it, and refuses an id already in use; both are said
/// here rather than refused, because a diagram half written has neither yet and is still worth drawing.
/// </para>
/// </summary>
public sealed class ResolveArchitecture : IAstStage
{
    public string Name => "architecture:names";

    public ContentNode Run(ContentNode tree)
    {
        var declared = new Dictionary<string, string>(StringComparer.Ordinal);
        var wrong = new Dictionary<ContentNode, string>();
        var lines = tree.SelfAndDescendants()
            .Where(node => Declares.Contains(node.Kind) || node.Kind is ArchitectureKinds.Edge or ArchitectureKinds.Align)
            .ToList();

        // What is declared, and what each of them is: an id names one thing, whether it is a group, a service or a junction.
        foreach (var line in lines.Where(line => Declares.Contains(line.Kind)))
        {
            if (Said(line, ArchitectureRoles.Id) is not { } id || Text(id) is not { Length: > 0 } name) continue;

            if (declared.TryGetValue(name, out var already)) wrong[id] = $"{name} is already the {already} written above this.";
            else declared[name] = Called(line.Kind);
        }

        foreach (var line in lines)
        {
            switch (line.Kind)
            {
                case ArchitectureKinds.Align:
                    Aligned(line, declared, wrong);
                    break;

                case ArchitectureKinds.Edge:
                    Joined(line, declared, wrong);
                    break;

                default:
                    Inside(line, declared, wrong);
                    break;
            }
        }

        if (wrong.Count == 0) return tree;

        return AstRewrite.Each(tree, node => wrong.TryGetValue(node, out var reason) ? node.Saying(reason) : node);
    }

    /// <summary>The group something is put in: it has to be a group, and to be declared above whatever is put in it.</summary>
    private static void Inside(ContentNode line, IReadOnlyDictionary<string, string> declared, Dictionary<ContentNode, string> wrong)
    {
        if (Said(line, ArchitectureRoles.In) is not { } inside || Text(inside) is not { Length: > 0 } name) return;

        if (!declared.TryGetValue(name, out var what))
            wrong[inside] = $"No group {name} is written in this diagram.";
        else if (what != Called(ArchitectureKinds.Group))
            wrong[inside] = $"{name} is a {what}, and only a group holds anything.";
        else if (string.Equals(name, Text(Said(line, ArchitectureRoles.Id)), StringComparison.Ordinal))
            wrong[inside] = $"{name} cannot be put inside itself.";
    }

    /// <summary>An edge joins two services by name, and reaches a group only through a service that is in one.</summary>
    private static void Joined(ContentNode line, IReadOnlyDictionary<string, string> declared, Dictionary<ContentNode, string> wrong)
    {
        foreach (var end in line.Children.Where(child => child.Kind == MermaidKinds.Name))
        {
            if (Text(end) is not { Length: > 0 } name) continue;

            if (!declared.TryGetValue(name, out var what)) wrong[end] = $"No service {name} is written in this diagram.";
            else if (what == Called(ArchitectureKinds.Group)) wrong[end] = $"{name} is a group, and an edge joins services: name a service in it and add {ArchitectureGrammar.GroupMark}.";
        }

        var sides = line.Children.Where(child => child.Kind == MermaidKinds.Key && child.Role == ArchitectureRoles.Side).ToList();
        if (sides.Count == 2 && string.Equals(sides[0].Text, sides[1].Text, StringComparison.OrdinalIgnoreCase))
            wrong[line] = "An edge leaving and arriving on the same side says nothing about where its ends sit.";
    }

    /// <summary>An align line shares a row or a column between services, and needs two of them to share anything.</summary>
    private static void Aligned(ContentNode line, IReadOnlyDictionary<string, string> declared, Dictionary<ContentNode, string> wrong)
    {
        var members = line.Inner(MermaidKinds.Names)?.Children.Where(child => child.Kind == MermaidKinds.Name).ToList() ?? [];

        foreach (var member in members)
        {
            if (Text(member) is not { Length: > 0 } name) continue;

            if (!declared.TryGetValue(name, out var what)) wrong[member] = $"No service {name} is written in this diagram.";
            else if (what == Called(ArchitectureKinds.Group)) wrong[member] = $"{name} is a group, and a row or a column is shared between services.";
        }

        if (members.Count(member => Text(member) is { Length: > 0 }) < 2)
            wrong[line] = "An align line shares a row or a column between two services or more.";
    }

    /// <summary>The kinds of line that declare something an id names.</summary>
    private static readonly string[] Declares = [ArchitectureKinds.Group, ArchitectureKinds.Service, ArchitectureKinds.Junction];

    private static string Called(string kind) => kind switch
    {
        ArchitectureKinds.Group => "group",
        ArchitectureKinds.Junction => "junction",
        _ => "service",
    };

    private static ContentNode? Said(ContentNode line, string role) =>
        line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name
                                              && child.Children.Any(inner => inner.Kind == MermaidKinds.Words && inner.Role == role));

    private static string? Text(ContentNode? name) =>
        name?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Words)?.Text;
}
