using System;
using System.Collections.Generic;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Common;

/// <summary>A page holds its view-model: made once, on first need, and the only thing a page shown again is told.</summary>
[TestClass]
[CoversNode("chrome-page-views")]
public class PageViewModelTests
{
    private sealed class Recording : IPageViewModel
    {
        public Dictionary<string, string>? Shown { get; private set; }
        public string GetContext() => string.Empty;
        public void Reinitialize(Dictionary<string, string> pageParams) => Shown = pageParams;
    }

    [TestMethod]
    public void TheViewModel_IsMadeOnFirstNeed_AndKept()
    {
        var made = 0;
        var page = new Page { ViewModelFactory = () => { made++; return new Recording(); } };

        Assert.IsNull(page.ViewModel, "defining a page makes nothing");
        var first = page.GetOrCreateViewModel();

        Assert.AreSame(first, page.GetOrCreateViewModel());
        Assert.AreSame(first, page.ViewModel);
        Assert.AreEqual(1, made);
    }

    [TestMethod]
    public void AFailingFactory_IsRecorded_NotThrown_AndNotRunAgain()
    {
        var made = 0;
        var page = new Page { ViewModelFactory = () => { made++; throw new InvalidOperationException("broken"); } };

        Assert.IsNull(page.GetOrCreateViewModel());
        Assert.IsNull(page.GetOrCreateViewModel());

        Assert.AreEqual(1, made);
        StringAssert.Contains(page.LoadException?.Message, "broken");
    }

    [TestMethod]
    public void BeingShownAgain_ReachesAPageThatIsMade_AndOnlyThat()
    {
        var model = new Recording();
        var page = new Page { ViewModelFactory = () => model };

        page.Reinitialize(new() { ["topic"] = "early" });
        Assert.IsNull(model.Shown, "a page not yet made is made with its parameters; it is not told them");

        page.GetOrCreateViewModel();
        page.Reinitialize(new() { ["topic"] = "again" });
        Assert.AreEqual("again", model.Shown?["topic"]);
    }
}
