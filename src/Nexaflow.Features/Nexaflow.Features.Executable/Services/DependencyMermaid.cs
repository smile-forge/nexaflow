using System;
using System.Collections.Generic;
using System.Text;

namespace Nexaflow.Features.Executable.Services;

/// <summary>
/// Renders a <see cref="DependencyGraph"/> as a mermaid flowchart whose nodes are clickable and
/// expandable.
/// <para>
/// Both affordances are real mermaid: the link is a <c>click</c> directive, and expansion is declared
/// in the <c>config: nexaflow:</c> front-matter block. Nothing is smuggled into a node label or into
/// a private href scheme, so the generated markdown is the same text a user can paste elsewhere — a
/// stock mermaid renderer ignores the config block it does not know and still draws the graph.
/// </para>
/// <para>
/// The front-matter also carries each node's module name, so a click comes back naming the module
/// rather than <c>n7</c> and this feature needs no side table mapping one to the other.
/// </para>
/// </summary>
public static class DependencyMermaid
{
    /// <summary>Builds the markdown block, including the fence.</summary>
    public static string Build(DependencyGraph graph)
    {
        var body      = new StringBuilder();
        var ids       = new Dictionary<DependencyNode, string>();
        var clicks    = new List<string>();
        var styled    = new List<string>();
        var collapsed = new List<(string id, string module)>();
        var expanded  = new List<(string id, string module)>();
        int counter   = 0;

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
            body.AppendLine($"  {id}{Shape(node)}");

            // The two actions are now two hit regions, so a node no longer has to choose: an opened
            // module still opens as its own tab, and its chip closes it again.
            //
            // The root is the exception, and gets no chip at all: the walk always opens the binary
            // you are inspecting, so there is no state in which it is closed. Offering to close it
            // produced a chip that did nothing, and left the view believing the whole graph was
            // folded away behind a node that could never be opened again.
            if (node.CanExpand)                                    collapsed.Add((id, node.Name));
            else if (node.IsExpanded && !ReferenceEquals(node, graph.Root)) expanded.Add((id, node.Name));

            if (node.Path is { Length: > 0 } path &&
                node.Kind is DependencyKind.Resolved or DependencyKind.Cycle)
                clicks.Add($"  click {id} href \"{Escape(path)}\" \"Inspect {Escape(node.Name)}\"");

            if (StyleClass(node) is { } css) styled.Add($"  class {id} {css}");

            foreach (var child in node.Children)
            {
                string childId = IdOf(child);
                body.AppendLine(child.IsDelayLoad
                    ? $"  {id} -.-> {childId}"     // dashed: resolved on first call, not at load
                    : $"  {id} --> {childId}");
                Emit(child);
            }
        }

        Emit(graph.Root);

        var builder = new StringBuilder();
        builder.AppendLine("```mermaid");
        AppendFrontMatter(builder, collapsed, expanded);
        builder.AppendLine("graph LR");
        builder.Append(body);

        // classDef lines carry no colour: the renderer themes nodes, and a hard-coded fill here
        // would survive into a light theme as an unreadable block.
        builder.AppendLine("  classDef apiset stroke-dasharray: 4 3");
        builder.AppendLine("  classDef missing stroke-width: 2px");
        foreach (var line in styled) builder.AppendLine(line);
        foreach (var line in clicks) builder.AppendLine(line);

        builder.AppendLine("```");
        return builder.ToString();
    }

    /// <summary>
    /// A native binary's import tree fans out far wider than it goes deep — a single module can name
    /// a hundred imports — so the surplus siblings fold behind one chip rather than becoming a mile
    /// of diagram.
    /// </summary>
    public const int MaxFanOut = 20;

    private static void AppendFrontMatter(
        StringBuilder builder,
        List<(string id, string module)> collapsed,
        List<(string id, string module)> expanded)
    {
        builder.AppendLine("---");
        builder.AppendLine("config:");
        builder.AppendLine("  nexaflow:");
        builder.AppendLine($"    maxFanOut: {MaxFanOut}");
        AppendSection(builder, "collapsed", collapsed);
        AppendSection(builder, "expanded",  expanded);
        builder.AppendLine("---");
    }

    private static void AppendSection(StringBuilder builder, string name, List<(string id, string module)> nodes)
    {
        if (nodes.Count == 0) return;
        builder.AppendLine($"    {name}:");
        foreach (var (id, module) in nodes)
            builder.AppendLine($"      {id}: \"{Escape(module)}\"");
    }

    private static string Shape(DependencyNode node)
    {
        string label = Escape(node.Name) + node.Kind switch
        {
            DependencyKind.ApiSet  => " (API set)",
            DependencyKind.Missing => " — not found",
            DependencyKind.Cycle   => " ↩",
            DependencyKind.Elided  => "",
            _                      => Detail(node),
        };

        return node.Kind switch
        {
            DependencyKind.ApiSet  => $"([\"{label}\"])",   // stadium — a virtual module
            DependencyKind.Missing => $"{{{{\"{label}\"}}}}", // hexagon — a problem
            DependencyKind.Elided  => $"[\"…\"]",
            _                      => $"[\"{label}\"]",
        };
    }

    /// <summary>
    /// The second line of a module's label. It says <c>functions</c> deliberately: the number is how
    /// many functions the <i>parent</i> imports from this module, and reading it as "how much is
    /// behind this node" is wrong by an order of magnitude — three functions from <c>ole32</c> opens
    /// up eighty-odd modules. Once the module has actually been opened, what is behind it is known,
    /// so that gets said too.
    /// </summary>
    private static string Detail(DependencyNode node)
    {
        var parts = new List<string>(2);
        if (node.ImportedFunctionCount > 0)
            parts.Add($"{node.ImportedFunctionCount} function{(node.ImportedFunctionCount == 1 ? "" : "s")} used");
        if (node.Walked && node.Children.Count > 0)
            parts.Add($"{node.Children.Count} modules");

        return parts.Count == 0 ? "" : "<br/>" + string.Join(" · ", parts);
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
