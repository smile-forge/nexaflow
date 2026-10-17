using Nexaflow.Core;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>The --openTab deep link's page parameters: each --tabParam key=value, the first '=' parting the two.</summary>
[TestClass]
[CoversNode("launch-control")]
public class AppDeepLinkTests
{
    [TestMethod]
    public void EachTabParam_IsAPageParameter_SplitAtItsFirstEquals()
    {
        var given = App.TabParams(["--uiTest", "--openTab", "Images", "--tabParam", @"paths=C:\a=b.png", "--TABPARAM", "draft=",
                                   "--tabParam", "nokey", "--tabParam", "=nothing", "--tabParam"]);

        CollectionAssert.AreEquivalent(new Dictionary<string, string> { ["paths"] = @"C:\a=b.png", ["draft"] = "" }, given);
    }
}
