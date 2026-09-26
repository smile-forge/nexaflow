using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.State.Stages;

/// <summary>
/// Works out what a state's name stands for where the line it is written on does not say. A <c>[*]</c> is the dot its scope
/// starts at where a transition leaves it and the one it stops at where one reaches it (<see cref="MarkerNode"/>) — which depends
/// on the braces round it and the dividers above it. And a name a composite state is called by, written anywhere, is that
/// composite rather than a state of its own (<see cref="GroupReferenceNode"/>) — which depends on composites opened above it or
/// below. Mermaid reads both the same way.
/// </summary>
public sealed class ResolveStates : IAstStage
{
    /// <summary>What the dots a scope starts and stops at are written as.</summary>
    public const string Edge = "[*]";

    public string Name => "state:states";

    public ContentNode Run(ContentNode tree)
    {
        var composites = new Dictionary<string, int>(StringComparer.Ordinal);
        var opened = 0;

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Line))
        {
            if (line.Stated() is not { Kind: StateKinds.Opens } opens) continue;

            if (Id(opens.Children.FirstOrDefault(child => child.Kind == StateKinds.Named)) is { Length: > 0 } id) composites.TryAdd(id, opened);
            opened++;
        }

        var told = new Dictionary<ContentNode, ContentNode>(ReferenceEqualityComparer.Instance);
        var groups = 0;
        Walk(tree, null);

        return told.Count == 0 ? tree : AstRewrite.Each(tree, node => told.GetValueOrDefault(node, node));

        // Each scope in turn — the block, and every composite state inside it — counting the regions its dividers make.
        void Walk(ContentNode holder, string? scope)
        {
            var region = 1;

            foreach (var part in holder.Children)
            {
                if (part.Kind == MermaidKinds.Group)
                {
                    Walk(part, (groups++).ToString(CultureInfo.InvariantCulture));
                    continue;
                }

                switch (part.Stated())
                {
                    case { Kind: StateKinds.Concurrent }:
                        region++;
                        break;

                    case { Kind: StateKinds.Transition } stated:
                        var ends = stated.Children.Where(child => child.Kind == StateKinds.Named).Take(2).ToList();
                        for (var at = 0; at < ends.Count; at++) Told(ends[at], scope, region, leaving: at == 0, marks: true);
                        break;

                    case { Kind: StateKinds.State } stated:
                        foreach (var named in stated.Children.Where(child => child.Kind == StateKinds.Named)) Told(named, scope, region, leaving: true, marks: true);
                        break;

                    case { Kind: StateKinds.Note or StateKinds.Click } stated:
                        foreach (var named in stated.SelfAndDescendants().Where(node => node.Kind == StateKinds.Named)) Told(named, scope, region, leaving: true, marks: false);
                        break;
                }
            }
        }

        void Told(ContentNode named, string? scope, int region, bool leaving, bool marks)
        {
            if (Id(named) is not { Length: > 0 } id) return;

            if (id == Edge)
            {
                if (marks) told[named] = new MarkerNode(named, Marker(!leaving, scope, region), stop: !leaving);
            }
            else if (composites.TryGetValue(id, out var group))
            {
                told[named] = new GroupReferenceNode(named, group);
            }
        }
    }

    /// <summary>
    /// What the dot a scope starts or stops at is called, which is what a <c>class</c> line styles it by. Each region past the first of
    /// a composite state has dots of its own, as Mermaid draws them.
    /// </summary>
    private static string Marker(bool stop, string? scope, int region)
    {
        var name = stop ? StateGrammar.Pseudo[1] : StateGrammar.Pseudo[0];

        return scope is null ? name : region > 1 ? $"{name}@{scope}#{region}" : $"{name}@{scope}";
    }

    /// <summary>What a state is called where it is named.</summary>
    private static string? Id(ContentNode? named) => named?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name)?.Words()?.Text;
}
