using System;
using System.Collections.Generic;
using System.Text;

namespace Nexaflow.Features.Executable.Services;

/// <summary>
/// Renders a <see cref="DependencyGraph"/> as a mermaid flowchart whose nodes are clickable.
/// <para>
/// Links are emitted as real mermaid <c>click</c> directives rather than being smuggled into node
/// labels, so the generated markdown stays valid mermaid that a user can copy elsewhere and still
/// have render. The href is the module's own file path; the host turns a click into a new inspector
/// tab for that dependency.
/// </para>
/// </summary>
public static class DependencyMermaid
{
    /// <summary>Builds the markdown block, including the fence.</summary>
    public static string Build(DependencyGraph graph)
    {
        var builder = new StringBuilder();
        builder.AppendLine("```mermaid");
        builder.AppendLine("graph LR");

        var ids     = new Dictionary<DependencyNode, string>();
        var clicks  = new List<string>();
        var styled  = new List<string>();
        int counter = 0;

        string IdOf(DependencyNode node)
        {
            if (ids.TryGetValue(node, out var existing)) return existing;
            string id = $"n{counter++}";
            ids[node] = id;
            return id;
        }

        void Emit(DependencyNode node)
        {
            string id = IdOf(node);
            builder.AppendLine($"  {id}{Shape(node)}");

            // Two different actions share one gesture, distinguished by a scheme on the href: a
            // module that has not been opened up yet expands in place, and one that has (or the
            // root) opens as its own inspector tab. The label carries the matching marker so the
            // node says which it will do before it is clicked.
            if (node.CanExpand)
                clicks.Add($"  click {id} href \"{ExpandScheme}{Escape(node.Name)}\" " +
                           $"\"Expand {Escape(node.Name)}\"");
            else if (node.Path is { Length: > 0 } path &&
                     node.Kind is DependencyKind.Resolved or DependencyKind.Cycle)
                clicks.Add($"  click {id} href \"{OpenScheme}{Escape(path)}\" " +
                           $"\"Inspect {Escape(node.Name)}\"");

            if (StyleClass(node) is { } css) styled.Add($"  class {id} {css}");

            foreach (var child in node.Children)
            {
                string childId = IdOf(child);
                builder.AppendLine(child.IsDelayLoad
                    ? $"  {id} -.-> {childId}"     // dashed: resolved on first call, not at load
                    : $"  {id} --> {childId}");
                Emit(child);
            }
        }

        Emit(graph.Root);

        // classDef lines carry no colour: the renderer themes nodes, and a hard-coded fill here
        // would survive into a light theme as an unreadable block.
        builder.AppendLine("  classDef apiset stroke-dasharray: 4 3");
        builder.AppendLine("  classDef missing stroke-width: 2px");
        foreach (var line in styled) builder.AppendLine(line);
        foreach (var line in clicks) builder.AppendLine(line);

        builder.AppendLine("```");
        return builder.ToString();
    }

    /// <summary>Href prefixes the host uses to tell the two click actions apart.</summary>
    public const string ExpandScheme = "nexaflow-expand:";
    public const string OpenScheme   = "nexaflow-open:";

    private static string Shape(DependencyNode node)
    {
        // A leading + is the whole affordance: it marks every node that still has a subtree behind
        // it, so expansion is one deliberate click at a time rather than a depth setting that
        // detonates the entire graph.
        string prefix = node.CanExpand ? "+ " : "";

        string label = prefix + Escape(node.Name) + node.Kind switch
        {
            DependencyKind.ApiSet  => " (API set)",
            DependencyKind.Missing => " — not found",
            DependencyKind.Cycle   => " ↩",
            DependencyKind.Elided  => "",
            _                      => node.ImportCount > 0 ? $"<br/>{node.ImportCount} imports" : "",
        };

        return node.Kind switch
        {
            DependencyKind.ApiSet  => $"([\"{label}\"])",   // stadium — a virtual module
            DependencyKind.Missing => $"{{{{\"{label}\"}}}}", // hexagon — a problem
            DependencyKind.Elided  => $"[\"…\"]",
            _                      => $"[\"{label}\"]",
        };
    }

    private static string? StyleClass(DependencyNode node) => node.Kind switch
    {
        DependencyKind.ApiSet  => "apiset",
        DependencyKind.Missing => "missing",
        _                      => null,
    };

    /// <summary>Mermaid labels are quoted, so a quote or a newline in a path would end the label early.</summary>
    private static string Escape(string text)
        => text.Replace("\"", "&quot;").Replace("\r", "").Replace("\n", " ");
}
