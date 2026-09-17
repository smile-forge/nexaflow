using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>A tree laid out tidily: children beside their parent, each subtree given its own room, the root's children either side.</summary>
[TestClass]
[CoversNode("mindmap")]
public class DiagramTreeTests
{
    private sealed record Node(string Name, params Node[] Children);

    private static readonly Node Root = new("root", new Node("a", new Node("a1"), new Node("a2")), new Node("b"), new Node("c"));

    private static Dictionary<Node, Rect> Laid(bool sided = true) =>
        DiagramTree.Lay(Root, node => node.Children, _ => new Size(80, 20), sided);

    [TestMethod]
    public void TheRootsChildrenTakeTurnsEitherSideOfIt()
    {
        var laid = Laid();
        var root = laid[Root];

        Assert.IsTrue(laid[Root.Children[0]].Right <= root.Left, "the first child left of the root");
        Assert.IsTrue(laid[Root.Children[1]].Left >= root.Right, "the second right of it");
        Assert.IsTrue(laid[Root.Children[2]].Right <= root.Left, "and the third back on the left");
    }

    [TestMethod]
    public void AChildIsBesideItsParent_AndASubtreeIsGivenTheRoomItNeeds()
    {
        var laid = Laid();
        var (a, a1, a2) = (laid[Root.Children[0]], laid[Root.Children[0].Children[0]], laid[Root.Children[0].Children[1]]);

        Assert.IsTrue(a1.Right <= a.Left && a2.Right <= a.Left, "a's children further out than a, on a's side");
        Assert.IsTrue(a2.Top >= a1.Bottom, "one under the other, without overlapping");
        Assert.AreEqual((a1.Top + a2.Bottom) / 2, a.Top + (a.Height / 2), 0.5, "and a against the middle of them");
        Assert.IsFalse(laid.Values.Any(one => laid.Values.Any(two => one != two && one.IntersectsWith(two))), "nothing overlaps anything");
    }

    [TestMethod]
    public void AOneSidedTreeGrowsOnlyRight()
    {
        var laid = Laid(sided: false);

        Assert.IsTrue(laid.Where(placed => placed.Key != Root).All(placed => placed.Value.Left >= laid[Root].Right - 1));
    }
}
