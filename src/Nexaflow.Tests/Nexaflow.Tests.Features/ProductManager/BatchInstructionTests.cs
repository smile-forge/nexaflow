using System.Linq;
using Nexaflow.Services.Initiatives.Cli;
using Nexaflow.Services.Initiatives.Product.Model;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// One batch instruction, dispatched the way a script line is. Batch is the only transactional way to edit
/// the tree — every line parses and applies in memory, and a single bad line means nothing is written — so
/// what it does and does not accept is the contract, not an implementation detail.
/// </summary>
[TestClass]
[NoCoverage("Batch instruction dispatch for the headless CLI - batch is deliberately CLI-only, so it maps to no product node.")]
public class BatchInstructionTests
{
    private static ProductState WithLinks(params Snaplink[] links) => new()
    {
        Product = new ProductDocument { Product = "P" },
        Nodes = new()
        {
            ["n"] = new ProductNode
            {
                Title = "n",
                Concerns = [new ConcernLink { Tag = "tests", Snaplinks = [.. links] }]
            }
        }
    };

    private static (bool Ok, string Message) Run(ProductState s, string line) =>
        Program.ApplyOne(s, [.. Program.Tokenize(line)]);

    /// <summary>A move breaks several snaplinks at once; running them as separate remap calls means a
    /// half-applied tree if the third path is wrong. As a batch instruction they land together or not at all.</summary>
    [TestMethod]
    public void Remap_IsABatchInstruction()
    {
        var s = WithLinks(new Snaplink { Type = "code", Doc = "old/A.cs", Class = "A" });

        var (ok, msg) = Run(s, "remap old/A.cs new/A.cs");

        Assert.IsTrue(ok, msg);
        Assert.AreEqual("new/A.cs", s.Nodes["n"].Concerns!.Single().Snaplinks!.Single().Doc);
    }

    /// <summary>Standalone, "nothing referenced that path" is a fair answer. In a script the path came from
    /// a move the author already made, so a miss means the script is wrong — and batch must abort, not
    /// silently apply the other nine lines.</summary>
    [TestMethod]
    public void Remap_InABatch_FailsWhenNothingMatched()
    {
        var s = WithLinks(new Snaplink { Type = "code", Doc = "old/A.cs", Class = "A" });

        var (ok, msg) = Run(s, "remap does/not/exist.cs new/A.cs");

        Assert.IsFalse(ok);
        StringAssert.Contains(msg, "nothing to remap");
    }

    [TestMethod]
    public void RemoveSnaplink_AddressesOneLinkByItsFields()
    {
        var s = WithLinks(
            new Snaplink { Type = "code", Doc = "tests/A.cs", Class = "A", Method = "One" },
            new Snaplink { Type = "code", Doc = "tests/A.cs", Class = "A", Method = "Two" });

        var (ok, msg) = Run(s, "remove-snaplink n --concern tests --doc tests/A.cs --method Two");

        Assert.IsTrue(ok, msg);
        Assert.AreEqual("One", s.Nodes["n"].Concerns!.Single().Snaplinks!.Single().Method);
    }

    /// <summary>Two ways to name the same link that disagree the moment anything reorders the list. Picking
    /// one silently would delete the entry next to the one the script meant.</summary>
    [TestMethod]
    public void RemoveSnaplink_RefusesAnIndexAndAMatcherTogether()
    {
        var s = WithLinks(new Snaplink { Type = "code", Doc = "tests/A.cs", Class = "A" });

        var (ok, msg) = Run(s, "remove-snaplink n --concern tests --index 0 --class A");

        Assert.IsFalse(ok);
        StringAssert.Contains(msg, "alternatives");
        Assert.AreEqual(1, s.Nodes["n"].Concerns!.Single().Snaplinks!.Count, "nothing was removed");
    }

    [TestMethod]
    public void AnUnknownInstruction_NamesWhatBatchAccepts()
    {
        var (ok, msg) = Run(WithLinks(), "validate .");

        Assert.IsFalse(ok);
        StringAssert.Contains(msg, "remap", "the supported-instruction list must stay in step with the dispatch");
    }
}
