using System;
using System.Linq;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// That a diagram on the shared layout tree is joined up end to end: a grammar and a builder both, or neither, and its builder
/// tested against <see cref="MermaidBuilderContract"/>. How a diagram is added is <c>docs/mermaid-diagrams.md</c>.
/// </summary>
[TestClass]
[CoversNode("mermaid-diagram-kit")]
public class MermaidBuilderRulesTests
{
    [TestMethod]
    public void EveryDiagramWithAGrammarIsDrawnOnTheSharedTree_AndOnlyThose()
    {
        var mismatched = Enum.GetValues<MermaidDiagram>()
            .Where(diagram => (MermaidDiagrams.Grammar(diagram) is null) != (MermaidBuilders.For(diagram) is null))
            .Select(diagram => MermaidDiagrams.Grammar(diagram) is null ? $"{diagram} has a builder and no grammar" : $"{diagram} has a grammar and no builder")
            .ToList();

        Assert.AreEqual(0, mismatched.Count,
                        $"{string.Join("; ", mismatched)} — a diagram is read by its grammar (MermaidDiagrams.Grammar) and drawn by its builder (MermaidBuilders.For), both or neither.");
    }

    [TestMethod]
    public void EveryBuilderIsTestedAgainstTheContract()
    {
        var tested = typeof(MermaidBuilderRulesTests).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsSubclassOf(typeof(MermaidBuilderContract)))
            .Select(type => ((MermaidBuilderContract)Activator.CreateInstance(type)!).Diagram)
            .ToHashSet();

        var untested = Enum.GetValues<MermaidDiagram>()
            .Where(diagram => MermaidBuilders.For(diagram) is not null && !tested.Contains(diagram))
            .ToList();

        Assert.AreEqual(0, untested.Count,
                        $"{string.Join(", ", untested)}: a builder with no tests deriving from MermaidBuilderContract — see docs/mermaid-diagrams.md.");
    }
}
