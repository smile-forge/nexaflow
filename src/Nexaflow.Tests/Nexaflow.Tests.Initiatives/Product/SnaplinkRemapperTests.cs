using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Product;

/// <summary>
/// Rewriting snaplink targets to follow a rename/move — the safe alternative to hand-editing tree.json.
/// The boundary rule matters: a rename of <c>Foo.cs</c> must not also rewrite <c>Foo.csproj</c>.
/// </summary>
[TestClass]
[CoversNode("product-snaplinks")]
public class SnaplinkRemapperTests
{
    private static ProductState Tree(params Snaplink[] nodeLinks) => new()
    {
        Nodes = new Dictionary<string, ProductNode> { ["n"] = new() { Title = "N", Snaplinks = [.. nodeLinks] } }
    };

    private static Snaplink Code(string doc, string? cls = null, string? method = null) =>
        new() { Type = "code", Doc = doc, Class = cls, Method = method };

    [TestMethod]
    public void ExactFileRename_RewritesThatOneLink()
    {
        var state = Tree(Code("src/Old.cs", "Old"), Code("src/Other.cs", "Other"));
        var changed = SnaplinkRemapper.Remap(state, "src/Old.cs", "src/New.cs");

        Assert.AreEqual(1, changed);
        Assert.AreEqual("src/New.cs", state.Nodes["n"].Snaplinks![0].Doc);
        Assert.AreEqual("src/Other.cs", state.Nodes["n"].Snaplinks![1].Doc, "an unrelated link is untouched");
    }

    [TestMethod]
    public void DirectoryPrefixMove_RewritesEveryLinkUnderIt()
    {
        var state = Tree(Code("src/A/One.cs"), Code("src/A/sub/Two.cs"), Code("src/B/Three.cs"));
        var changed = SnaplinkRemapper.Remap(state, "src/A", "lib/A");

        Assert.AreEqual(2, changed);
        Assert.AreEqual("lib/A/One.cs", state.Nodes["n"].Snaplinks![0].Doc);
        Assert.AreEqual("lib/A/sub/Two.cs", state.Nodes["n"].Snaplinks![1].Doc);
        Assert.AreEqual("src/B/Three.cs", state.Nodes["n"].Snaplinks![2].Doc);
    }

    [TestMethod]
    public void PathBoundary_DoesNotRewriteASiblingWithTheOldNameAsAPrefix()
    {
        var state = Tree(Code("src/Foo.cs"), Code("src/Foo.csproj"));
        var changed = SnaplinkRemapper.Remap(state, "src/Foo.cs", "src/Bar.cs");

        Assert.AreEqual(1, changed);
        Assert.AreEqual("src/Bar.cs", state.Nodes["n"].Snaplinks![0].Doc);
        Assert.AreEqual("src/Foo.csproj", state.Nodes["n"].Snaplinks![1].Doc, "Foo.csproj must not match a Foo.cs remap");
    }

    [TestMethod]
    public void ClassAndMethod_AreSetOnAffectedLinks_WhenGiven()
    {
        var state = Tree(Code("src/Old.cs", "OldClass", "OldMethod"));
        SnaplinkRemapper.Remap(state, "src/Old.cs", "src/New.cs", newClass: "NewClass", newMethod: "NewMethod");

        var link = state.Nodes["n"].Snaplinks![0];
        Assert.AreEqual("src/New.cs", link.Doc);
        Assert.AreEqual("NewClass", link.Class);
        Assert.AreEqual("NewMethod", link.Method);
    }

    [TestMethod]
    public void ConcernLevelSnaplinks_AreAlsoRemapped()
    {
        var state = new ProductState
        {
            Nodes = new Dictionary<string, ProductNode>
            {
                ["n"] = new()
                {
                    Title = "N",
                    Concerns = [new ConcernLink { Tag = "tests", Snaplinks = [Code("tests/OldTests.cs", "OldTests")] }]
                }
            }
        };

        var changed = SnaplinkRemapper.Remap(state, "tests/OldTests.cs", "tests/NewTests.cs");

        Assert.AreEqual(1, changed);
        Assert.AreEqual("tests/NewTests.cs", state.Nodes["n"].Concerns![0].Snaplinks![0].Doc);
    }

    [TestMethod]
    public void NoMatch_ChangesNothing()
    {
        var state = Tree(Code("src/A.cs"));
        Assert.AreEqual(0, SnaplinkRemapper.Remap(state, "src/Absent.cs", "src/X.cs"));
        Assert.AreEqual("src/A.cs", state.Nodes["n"].Snaplinks![0].Doc);
    }
}
