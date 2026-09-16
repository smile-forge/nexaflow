using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// That a diagram's grammar is not only written but joined up: named where its diagram is read, and tested against
/// <see cref="MermaidGrammarContract"/> — so a grammar cannot be added that the parser never reaches, or that nothing checks
/// holds what every other grammar holds. How a diagram is added is <c>docs/mermaid-diagrams.md</c>.
/// </summary>
[TestClass]
[CoversNode("mermaid-diagram-kit")]
public class MermaidKitRulesTests
{
    [TestMethod]
    public void EveryGrammarIsTheOneItsDiagramIsReadWith()
    {
        var registered = Enum.GetValues<MermaidDiagram>()
            .Select(MermaidDiagrams.Grammar)
            .OfType<IMermaidGrammar>()
            .Select(grammar => grammar.GetType())
            .ToHashSet();

        var loose = typeof(IMermaidGrammar).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IMermaidGrammar).IsAssignableFrom(type))
            .Where(type => !registered.Contains(type))
            .Select(type => type.Name)
            .ToList();

        Assert.AreEqual(0, loose.Count,
                        $"{string.Join(", ", loose)}: a grammar nothing reads with — name it for its diagram in MermaidDiagrams.Grammar.");
    }

    [TestMethod]
    public void EveryGrammarIsTestedAgainstTheContract()
    {
        var tested = typeof(MermaidKitRulesTests).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsSubclassOf(typeof(MermaidGrammarContract)))
            .Select(type => ((MermaidGrammarContract)Activator.CreateInstance(type)!).Diagram)
            .ToHashSet();

        var untested = Enum.GetValues<MermaidDiagram>()
            .Where(diagram => MermaidDiagrams.Grammar(diagram) is not null && !tested.Contains(diagram))
            .ToList();

        Assert.AreEqual(0, untested.Count,
                        $"{string.Join(", ", untested)}: a grammar with no tests deriving from MermaidGrammarContract — see docs/mermaid-diagrams.md.");
    }
}
