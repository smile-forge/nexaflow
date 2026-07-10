using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// Re-binding a saved integrity report to a freshly-loaded tree. The Integrity page can only edit a row whose
/// link it has bound to the real instance, so a binding miss shows up as "this row is from an older scan" and
/// the user is stuck until a full rescan — which is exactly what a stale index used to cause after a removal.
/// </summary>
[TestClass]
[CoversNode("product-snaplinks")]
public class IntegrityBinderTests
{
    private static Snaplink Code(string doc, string? cls = null, string? method = null) =>
        new() { Type = "code", Doc = doc, Class = cls, Method = method };

    private static IntegrityIssue Issue(string nodeId, int index, Snaplink link, string? concern = null) =>
        new() { NodeId = nodeId, NodeTitle = nodeId, Concern = concern, Index = index, Link = link, Detail = "broken" };

    /// <summary>A node whose own snaplinks are the given links (fresh instances, as a reload would produce).</summary>
    private static ProductState TreeWith(params Snaplink[] links) => new()
    {
        Nodes = new Dictionary<string, ProductNode> { ["n"] = new() { Title = "N", Snaplinks = [.. links] } }
    };

    [TestMethod]
    public void BindsByIndex_WhenNothingHasMoved()
    {
        var a = Code("a.cs"); var b = Code("b.cs");
        var state = TreeWith(a, b);
        var issues = new List<IntegrityIssue> { Issue("n", 0, Code("a.cs")), Issue("n", 1, Code("b.cs")) };

        var bound = IntegrityBinder.Bind(state, issues);

        Assert.AreSame(a, bound[0]);
        Assert.AreSame(b, bound[1]);
    }

    [TestMethod]
    public void StillBinds_AfterAnEarlierLinkWasRemoved_AndIndicesDrifted()
    {
        // The report was taken when 'a' sat at 0 and 'c' at 2. 'a' has since been removed from the tree.
        var b = Code("b.cs"); var c = Code("c.cs");
        var state = TreeWith(b, c);
        var issues = new List<IntegrityIssue> { Issue("n", 2, Code("c.cs")) };

        var bound = IntegrityBinder.Bind(state, issues);

        Assert.AreSame(c, bound[0], "a drifted index must fall back to matching the target, not strand the row");
    }

    [TestMethod]
    public void ReindexAfterRemoval_KeepsTheRemainingIndicesTruthful()
    {
        var first  = Issue("n", 0, Code("a.cs"));
        var second = Issue("n", 1, Code("b.cs"));
        var third  = Issue("n", 2, Code("c.cs"));
        var other  = Issue("n", 2, Code("z.cs"), concern: "tests");   // different list — must not shift
        var report = new IntegrityReport { Issues = [first, second, third, other] };

        IntegrityBinder.ReindexAfterRemoval(report, first, removedIndex: 0);

        Assert.AreEqual(0, second.Index);
        Assert.AreEqual(1, third.Index);
        Assert.AreEqual(2, other.Index, "a concern's snaplink list is not affected by a node-level removal");
    }

    [TestMethod]
    public void TwoIdenticalLinks_BindToDistinctInstances()
    {
        var a1 = Code("dup.cs"); var a2 = Code("dup.cs");
        var state = TreeWith(a1, a2);
        var issues = new List<IntegrityIssue> { Issue("n", 0, Code("dup.cs")), Issue("n", 1, Code("dup.cs")) };

        var bound = IntegrityBinder.Bind(state, issues);

        Assert.AreSame(a1, bound[0]);
        Assert.AreSame(a2, bound[1]);
        Assert.AreNotSame(bound[0], bound[1], "each live link may back at most one row");
    }

    [TestMethod]
    public void ALinkThatIsTrulyGone_DoesNotBind()
    {
        var state = TreeWith(Code("b.cs"));
        var bound = IntegrityBinder.Bind(state, [Issue("n", 0, Code("vanished.cs"))]);
        Assert.IsNull(bound[0]);
    }

    [TestMethod]
    public void ConcernLevelLinks_BindThroughTheirConcern()
    {
        var live = Code("t.cs");
        var state = new ProductState
        {
            Nodes = new Dictionary<string, ProductNode>
            {
                ["n"] = new() { Title = "N", Concerns = [new ConcernLink { Tag = "tests", Snaplinks = [live] }] }
            }
        };

        var bound = IntegrityBinder.Bind(state, [Issue("n", 0, Code("t.cs"), concern: "tests")]);

        Assert.AreSame(live, bound[0]);
    }

    [TestMethod]
    public void MarkdownHeadingPath_ParticipatesInTargetIdentity()
    {
        var one = new Snaplink { Type = "markdown", Doc = "d.md", TitlePath = ["A", "B"] };
        var two = new Snaplink { Type = "markdown", Doc = "d.md", TitlePath = ["A", "C"] };
        var state = new ProductState
        {
            Nodes = new Dictionary<string, ProductNode> { ["n"] = new() { Title = "N", Snaplinks = [one, two] } }
        };

        var reported = new Snaplink { Type = "markdown", Doc = "d.md", TitlePath = ["A", "C"] };
        var bound = IntegrityBinder.Bind(state, [Issue("n", 99, reported)]);   // index nonsense on purpose

        Assert.AreSame(two, bound[0], "two links to the same doc differ by heading path");
    }
}
