using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Core.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Right-clicking the breadcrumb offers <b>Copy path</b>. Two halves: a crumb that names a location copies
/// that one, and the bar itself — the separators, the gap after the last crumb, a crumb that names no
/// location — copies where you are, which is the deepest crumb that does.
/// <para>Needs an STA thread to build the control; opens no window.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("chrome-breadcrumb-copy-path")]
public class BreadcrumbCopyPathTests
{
    /// <summary>
    /// The bar resolves its brushes and the crumb-button style by key, so seeding its own dictionary is
    /// enough — <c>FindResource</c> looks there before the app's. No Application, no theme load.
    /// </summary>
    private static BreadcrumbBar Bar(params BreadcrumbSegment[] segments)
    {
        var bar = new BreadcrumbBar();
        bar.Resources["TextBrush"]      = Brushes.White;
        bar.Resources["TextMutedBrush"] = Brushes.Gray;
        bar.Resources["RibbonButton"]   = new Style(typeof(Button));
        bar.Segments = new ObservableCollection<BreadcrumbSegment>(segments);
        return bar;
    }

    private static List<Button> Crumbs(BreadcrumbBar bar)
        => ((StackPanel)((Border)bar.Content).Child).Children.OfType<Button>().ToList();

    /// <summary>What this element's own menu would copy, or null when it has no menu of its own.</summary>
    private static string? CopyTarget(FrameworkElement element)
        => element.ContextMenu?.Items.OfType<MenuItem>()
                  .FirstOrDefault(i => (string?)i.Header == "Copy path")?.CommandParameter as string;

    [TestMethod]
    public void EachCrumbCopiesItsOwnPath() => UiThread.Run(() =>
    {
        var bar = Bar(new BreadcrumbSegment { Label = "C:\\",  Path = @"C:\" },
                      new BreadcrumbSegment { Label = "docs",  Path = @"C:\docs" });

        var crumbs = Crumbs(bar);

        Assert.AreEqual(@"C:\",     CopyTarget(crumbs[0]));
        Assert.AreEqual(@"C:\docs", CopyTarget(crumbs[1]), "the last crumb is where you are, and copies it");
    });

    [TestMethod]
    public void ACrumbThatNamesNoLocationCarriesNoMenuOfItsOwn() => UiThread.Run(() =>
    {
        // "This PC" is a place, not a path. Leaving its menu null is what lets the right-click bubble to
        // the bar, which answers with the current location instead of with nothing.
        var bar = Bar(new BreadcrumbSegment { Label = "This PC" },
                      new BreadcrumbSegment { Label = "C:\\", Path = @"C:\" });

        Assert.IsNull(Crumbs(bar)[0].ContextMenu);
    });

    [TestMethod]
    public void TheBarItselfCopiesWhereYouAre() => UiThread.Run(() =>
    {
        var bar = Bar(new BreadcrumbSegment { Label = "This PC" },
                      new BreadcrumbSegment { Label = "C:\\",  Path = @"C:\" },
                      new BreadcrumbSegment { Label = "docs",  Path = @"C:\docs" });

        Assert.AreEqual(@"C:\docs", CopyTarget(bar));
    });

    [TestMethod]
    public void ASummaryLeafFallsBackToTheDeepestCrumbThatNamesOne() => UiThread.Run(() =>
    {
        // A viewer showing "D:\pics › 6 images": the leaf is a count, so the bar answers with the folder.
        var bar = Bar(new BreadcrumbSegment { Label = @"D:\pics", Path = @"D:\pics" },
                      new BreadcrumbSegment { Label = "6 images" });

        Assert.IsNull(Crumbs(bar)[1].ContextMenu);
        Assert.AreEqual(@"D:\pics", CopyTarget(bar));
    });

    [TestMethod]
    public void NothingNamesALocation_SoThereIsNoMenuAtAll() => UiThread.Run(() =>
    {
        var bar = Bar(new BreadcrumbSegment { Label = "Settings" });

        Assert.IsNull(CopyTarget(bar));
        Assert.IsNull(Crumbs(bar)[0].ContextMenu);
    });

    [TestMethod]
    public void ClearingTheSegmentsTakesTheMenuWithThem() => UiThread.Run(() =>
    {
        // The last tab closing nulls Segments. The crumbs go; a menu still offering the closed tab's path
        // would be the same stale-chrome bug the unconditional refresh exists to prevent.
        var bar = Bar(new BreadcrumbSegment { Label = "docs", Path = @"C:\docs" });

        bar.Segments = null;

        Assert.IsNull(CopyTarget(bar));
    });
}
