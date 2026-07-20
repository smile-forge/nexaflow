using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsSearch.ViewModels;
using NSubstitute;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch;

/// <summary>The Search page's AI context + scope. The search tool itself is covered by
/// <c>SearchViewModelTests</c> / <c>SearchQueryScorerTests</c>; this pins the honest get_context and the
/// file-scoped security context (kept in a dedicated class so its context-node coverage doesn't stack
/// with the sibling class's leaf-level [CoversNode] and trip NXCOV003).</summary>
[TestClass]
public class SearchAiTests
{
    [TestMethod]
    [CoversNode("win-search-ai-context")]
    public void Context_And_Scope_AreHonest()
    {
        var page = (IPageViewModel)new SearchViewModel("budget", @"C:\docs", [], Substitute.For<IShellServices>());

        // Context names the query and the search scope; scope is the search root (aspect-4 disambiguation).
        StringAssert.Contains(page.GetContext(), "budget");
        StringAssert.Contains(page.GetContext(), @"C:\docs");
        Assert.AreEqual(@"C:\docs", page.GetSecurityContext());
    }
}
