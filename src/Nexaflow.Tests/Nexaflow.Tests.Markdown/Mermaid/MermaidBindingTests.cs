using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// A binding standing where a diagram's content would be (<c>{{Path}}</c>): the parser holds it as written, and
/// <see cref="MermaidParser.Bind"/> reads what it comes to into its place as content nobody wrote here — the whole diagram
/// before a header, lines of it after one — so the tree still prints as its author wrote it.
/// </summary>
[TestClass]
[CoversNode("mermaid")]
public class MermaidBindingTests
{
    private static ContentNode[] Bound(ContentNode tree) =>
        [.. tree.SelfAndDescendants().Where(node => node.Kind == Kinds.BoundContent)];

    private static ContentNode[] Supplied(ContentNode tree) =>
        [.. tree.Children.Where(line => line.Role == Roles.Supplied)];

    [TestMethod]
    public void ABindingOnALineOfItsOwnIsHeldAsWritten()
    {
        foreach (var source in new[] { "{{Graph}}\n", "graph TD\n  a --> b\n  {{More}}\n" })
        {
            var tree = MermaidParser.Parse(source);

            Assert.AreEqual(source, tree.Print(), "held, not read");
            Assert.AreEqual(1, Bound(tree).Length, source);
        }
    }

    [TestMethod]
    public void ABindingAmongWordsIsTheWordsOwn()
    {
        var tree = MermaidParser.Parse("graph TD\n  a[\"Owned by {{Name}}\"]\n");

        Assert.AreEqual(0, Bound(tree).Length, "a binding in a label is read where the label is drawn");
    }

    [TestMethod]
    public void BeforeTheHeaderWhatIsSuppliedIsTheWholeDiagram()
    {
        var source = "{{Graph}}\n";
        var bound = MermaidParser.Bind(MermaidParser.Parse(source), path => path == "Graph" ? "graph TD\n  a --> b\n" : null);

        Assert.AreEqual(source, bound.Print(), "what was supplied takes up none of the source");
        Assert.AreEqual(MermaidDiagram.Flowchart, MermaidBlock.Of(bound).Diagram, "the header it supplied names the diagram");
        Assert.IsTrue(Supplied(bound).Length > 0);
    }

    [TestMethod]
    public void AfterTheHeaderWhatIsSuppliedIsLinesOfTheDiagramItNames()
    {
        var source = "graph TD\n  a --> b\n  {{More}}\n";
        var bound = MermaidParser.Bind(MermaidParser.Parse(source), _ => "b --> c\nc --> d\n");

        Assert.AreEqual(source, bound.Print());
        Assert.AreEqual(2, Supplied(bound).Length, "a line of the diagram for each line supplied");
        Assert.AreEqual(1, bound.SelfAndDescendants().Count(node => node.Kind == MermaidKinds.Header),
                        "the header it was read after is not supplied again");
    }

    [TestMethod]
    public void FrontMatterWhatIsSuppliedOpensWithIsFrontMatter()
    {
        var source = "graph TD\n  a --> b\n  {{More}}\n";
        var supplied = "---\nconfig:\n  nexaflow:\n    collapsed:\n      c: \"C\"\n---\nb --> c\n";
        var bound = MermaidParser.Bind(MermaidParser.Parse(source), _ => supplied);

        var folds = MermaidBlock.Of(bound).Configs.Aggregate(NexaflowConfig.None, (all, yaml) => all.And(NexaflowConfig.Read(yaml)));

        Assert.AreEqual("C", folds.Collapsed["c"], "what was supplied says which of its nodes have more behind them");
    }

    [TestMethod]
    public void EveryFrontMatterSaysItsPart()
    {
        var own = NexaflowConfig.Read("config:\n  nexaflow:\n    collapsed:\n      a: \"A\"\n");
        var more = NexaflowConfig.Read("config:\n  nexaflow:\n    collapsed:\n      a: \"Other\"\n      b: \"B\"\n");

        var both = own.And(more);

        Assert.AreEqual("A", both.Collapsed["a"], "the first to say wins");
        Assert.AreEqual("B", both.Collapsed["b"]);
    }

    [TestMethod]
    public void ABindingNothingSuppliesIsLeftAsWrittenSayingSo()
    {
        var source = "{{Graph}}\n";
        var bound = MermaidParser.Bind(MermaidParser.Parse(source), _ => null);

        Assert.AreEqual(source, bound.Print());
        Assert.AreEqual(0, Supplied(bound).Length);
        StringAssert.Contains(Bound(bound).Single().Trouble, "Nothing is bound to 'Graph'.");
    }

    [TestMethod]
    public void ABlockWithNoBindingIsTheSameBlock()
    {
        var tree = MermaidParser.Parse("graph TD\n  a --> b\n");

        Assert.AreSame(tree, MermaidParser.Bind(tree, _ => "never asked"));
    }
}
