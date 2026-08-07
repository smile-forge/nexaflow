using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Controls;

namespace Nexaflow.Tests.Core.Visuals.Controls;

/// <summary>
/// The one search chip every <c>ISearchable</c> page shows. It binds by CONVENTION rather than through an
/// interface, so nothing at compile time proves it still reaches a page's search state — these tests are
/// that proof. They also pin the composed AutomationIds, which are the ids each page used before the chip
/// was extracted and which UI automation still addresses.
/// <para>Interactive desktop only (WPF elements need an STA thread). Run with
/// <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("page-search-chip")]
public partial class SearchStatusChipTests   // partial: the nested fake's [RelayCommand]s generate into it
{
    /// <summary>Stands in for a page ViewModel: exactly the member names the chip binds by convention.
    /// If a rename ever breaks that shape, these tests fail where a silent empty chip would not.</summary>
    private sealed partial class FakePage : ObservableObject
    {
        [ObservableProperty] private bool _isSearchActive;
        [ObservableProperty] private int _searchMatchCount;
        [ObservableProperty] private string _currentSearchTerm = string.Empty;

        public bool HasSearchMatches => SearchMatchCount > 0;
        partial void OnSearchMatchCountChanged(int value) => OnPropertyChanged(nameof(HasSearchMatches));

        public int Nexts, Previouses, Clears;

        [RelayCommand] private void FindNextMatch() => Nexts++;
        [RelayCommand] private void FindPreviousMatch() => Previouses++;
        [RelayCommand] private void ClearSearch() => Clears++;
    }

    private static void WithChip(Action<SearchStatusChip, FakePage> test) => UiThread.Run(() =>
    {
        var page = new FakePage();
        var chip = new SearchStatusChip { PagePrefix = "Log", DataContext = page };

        // No window, so force a layout pass — bindings and Visibility only settle once measured.
        chip.Measure(new Size(1000, 100));
        chip.Arrange(new Rect(0, 0, 1000, 100));
        chip.UpdateLayout();

        test(chip, page);
    });

    private static void Settle(SearchStatusChip chip)
    {
        chip.Measure(new Size(1000, 100));
        chip.Arrange(new Rect(0, 0, 1000, 100));
        chip.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    private static DependencyObject? ById(DependencyObject root, string id) =>
        Descendants(root).FirstOrDefault(d => (string?)d.GetValue(AutomationProperties.AutomationIdProperty) == id);

    [TestMethod]
    public void AutomationIds_ComposeFromThePagePrefix() => WithChip((chip, _) =>
    {
        // These are the exact ids the Editor/Markdown/Email views declared by hand before the extraction;
        // a page keeps its own ids so automation written against one viewer still finds them.
        Assert.AreEqual("Log_SearchStatus", chip.AutomationIdStatus);
        Assert.AreEqual("Log_SearchMatchCount", chip.AutomationIdMatchCount);
        Assert.AreEqual("Log_SearchPrevious", chip.AutomationIdPrevious);
        Assert.AreEqual("Log_SearchNext", chip.AutomationIdNext);
        Assert.AreEqual("Log_SearchClear", chip.AutomationIdClear);

        // …and they actually reach the elements, not just the DPs.
        foreach (var id in new[] { "Log_SearchStatus", "Log_SearchMatchCount", "Log_SearchPrevious",
                                   "Log_SearchNext", "Log_SearchClear" })
            Assert.IsNotNull(ById(chip, id), $"no element carries {id}");
    });

    [TestMethod]
    public void ChangingThePrefix_RecomposesEveryId() => WithChip((chip, _) =>
    {
        chip.PagePrefix = "Tabular";
        Assert.AreEqual("Tabular_SearchStatus", chip.AutomationIdStatus);
        Assert.AreEqual("Tabular_SearchClear", chip.AutomationIdClear);
    });

    [TestMethod]
    public void TheChipIsHidden_UntilASearchIsActive() => WithChip((chip, page) =>
    {
        var panel = (UIElement)ById(chip, "Log_SearchStatus")!;
        Assert.AreEqual(Visibility.Collapsed, panel.Visibility, "nothing searched yet");

        page.IsSearchActive = true;
        Settle(chip);
        Assert.AreEqual(Visibility.Visible, panel.Visibility);
    });

    [TestMethod]
    public void ZeroMatches_StillShowsTheChip_ButOffersNoStepping() => WithChip((chip, page) =>
    {
        // "no matches for X" is a result worth showing — the page sets IsSearchActive even at zero — but
        // there is nothing to step through, so only dismiss stays available.
        page.IsSearchActive = true;
        page.SearchMatchCount = 0;
        page.CurrentSearchTerm = "alpha42";
        Settle(chip);

        Assert.AreEqual(Visibility.Visible, ((UIElement)ById(chip, "Log_SearchStatus")!).Visibility);
        Assert.AreEqual(Visibility.Collapsed, ((UIElement)ById(chip, "Log_SearchPrevious")!).Visibility);
        Assert.AreEqual(Visibility.Collapsed, ((UIElement)ById(chip, "Log_SearchNext")!).Visibility);
        Assert.AreEqual(Visibility.Visible, ((UIElement)ById(chip, "Log_SearchClear")!).Visibility);
    });

    [TestMethod]
    public void WithMatches_TheCountAndTermAreShown_AndSteppingAppears() => WithChip((chip, page) =>
    {
        page.IsSearchActive = true;
        page.SearchMatchCount = 7;
        page.CurrentSearchTerm = "alpha42";
        Settle(chip);

        var count = (TextBlock)ById(chip, "Log_SearchMatchCount")!;
        Assert.AreEqual("7 match(es)", count.Text);
        Assert.AreEqual(Visibility.Visible, ((UIElement)ById(chip, "Log_SearchPrevious")!).Visibility);
        Assert.AreEqual(Visibility.Visible, ((UIElement)ById(chip, "Log_SearchNext")!).Visibility);

        // The term is rendered beside the count so "7 match(es) for alpha42" reads as one sentence.
        var term = Descendants(chip).OfType<TextBlock>().FirstOrDefault(t => t.Text == " for alpha42");
        Assert.IsNotNull(term, "the searched term is not shown");
    });

    [TestMethod]
    public void SuffixText_MarksATruncatedCount() => WithChip((chip, page) =>
    {
        // A page that stopped scanning at a cap sets this, so a floor never reads as an exact total.
        page.IsSearchActive = true;
        page.SearchMatchCount = 5000;
        chip.SuffixText = "+";
        Settle(chip);

        Assert.IsNotNull(Descendants(chip).OfType<TextBlock>().FirstOrDefault(t => t.Text == "+"));
    });

    [TestMethod]
    public void TheButtons_DriveThePagesOwnCommands() => WithChip((chip, page) =>
    {
        page.IsSearchActive = true;
        page.SearchMatchCount = 3;
        Settle(chip);

        ((Button)ById(chip, "Log_SearchNext")!).Command.Execute(null);
        ((Button)ById(chip, "Log_SearchPrevious")!).Command.Execute(null);
        ((Button)ById(chip, "Log_SearchClear")!).Command.Execute(null);

        Assert.AreEqual(1, page.Nexts);
        Assert.AreEqual(1, page.Previouses);
        Assert.AreEqual(1, page.Clears);
    });
}
