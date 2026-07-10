using System.Linq;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// Every feature <c>[TestClass]</c> must declare the product node it backs with <c>[CoversNode]</c> (so the
/// tree can represent test coverage and the Integrity page can offer to link it) or opt out with
/// <c>[NoCoverage]</c>. Runs in CI as the backstop to the author-time analyzer; id validity is additionally
/// checked wherever <c>.product/tree.json</c> is present. Complements <see cref="CoverageGuardTests"/>
/// (which checks each feature has <em>a</em> test) by checking each test <em>declares what it covers</em>.
/// </summary>
[TestClass]
[NoCoverage("architecture guard — maps to no single product node")]
public class CoverageDeclarationGuardTests
{
    [TestMethod]
    public void Every_test_class_declares_the_node_it_covers()
    {
        var violations = CoverageDeclarationCheck.Verify(
            typeof(CoverageDeclarationGuardTests).Assembly, CoverageDeclarationCheck.LoadValidNodeIds());

        Assert.AreEqual(0, violations.Count,
            "Test classes must carry [CoversNode(\"node-id\")] (or [NoCoverage(\"reason\")]). "
            + $"Add them, or run `scan-tests --suggest-attributes` for the tree-derived starting set:\n  "
            + string.Join("\n  ", violations.Select(v => $"{v.TypeName} {v.Message}")));
    }
}
