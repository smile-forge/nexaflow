using System.Linq;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Architecture;

/// <summary>
/// Every <c>[TestClass]</c> in this suite must declare the product node it backs with <c>[CoversNode]</c>
/// or opt out with <c>[NoCoverage]</c> — the CI backstop to the author-time analyzer. Id validity is
/// additionally checked wherever <c>.product/tree.json</c> is present (skipped on a clean checkout
/// without it).
/// <para>
/// Each suite carries its own copy because the check reads <c>typeof(…).Assembly</c>: a guard sitting in
/// one assembly silently narrows to that assembly, which is exactly how tests drop out of the rule when a
/// suite is split off. Splitting Visuals out of Tests.Core would have taken 60 test classes with it.
/// </para>
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
