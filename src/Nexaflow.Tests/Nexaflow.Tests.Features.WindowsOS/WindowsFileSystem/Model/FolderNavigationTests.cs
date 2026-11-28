using System.IO;
using Nexaflow.Features.WindowsFileSystem.Model;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem.Model;

/// <summary>
/// Moving a tab about, with no view over it: into a folder and back up, the trail its breadcrumb shows, a path that is no
/// folder leaving the tab where it was, and This PC with its places.
/// </summary>
[TestClass]
[CoversNode("winfs-breadcrumb")]
public class FolderNavigationTests
{
    private string _scratch = string.Empty;
    private string _child = string.Empty;

    [TestInitialize]
    public void CreateScratch()
    {
        _scratch = ModelHarness.Scratch();
        _child   = Path.Combine(_scratch, "child");
        Directory.CreateDirectory(_child);
        File.WriteAllText(Path.Combine(_scratch, "note.txt"), "n");
    }

    [TestCleanup]
    public void RemoveScratch() { try { Directory.Delete(_scratch, recursive: true); } catch { } }

    [TestMethod]
    public async Task BrowsingIn_ThenUp_ReturnsToTheFolder_AndEachArrivalIsAnnounced()
    {
        var model = await ModelHarness.AtAsync(_scratch);
        var arrivals = 0;
        model.Navigation.Arrived += () => arrivals++;

        await model.Navigation.BrowseToAsync(_child);
        Assert.AreEqual(_child, model.Location.CurrentPath);

        model.Navigation.NavigateUp();
        await model.Navigation.Browsing;
        Assert.AreEqual(_scratch, model.Location.CurrentPath);
        Assert.AreEqual(2, arrivals);
    }

    [TestMethod]
    public async Task TheTrail_RunsFromTheTreesRoot_ToTheOpenFolder()
    {
        var model = await ModelHarness.AtAsync(_scratch);
        IReadOnlyList<(string Label, string Path)>? trail = null;
        model.Navigation.NavigationChanged += segments => trail = segments;

        await model.Navigation.BrowseToAsync(_child);

        Assert.IsNotNull(trail);
        Assert.AreEqual((Path.GetFileName(_scratch), _scratch), trail[0]);
        Assert.AreEqual(("child", _child), trail[^1]);
    }

    [TestMethod]
    public async Task APathThatIsNoFolder_LeavesTheTabWhereItWas()
    {
        var model = await ModelHarness.AtAsync(_scratch);

        await model.Navigation.BrowseToAsync(Path.Combine(_scratch, "note.txt"));
        await model.Navigation.BrowseToAsync(Path.Combine(_scratch, "missing"));

        Assert.AreEqual(_scratch, model.Location.CurrentPath);
    }

    [TestMethod]
    public void ThisPc_ListsItsPlaces_UnderOneCrumb_WithThisPcAtTheTreesRoot()
    {
        var model = ModelHarness.ThisPc();

        Assert.IsTrue(model.Location.IsThisPc);
        Assert.AreEqual(string.Empty, model.Location.CurrentPath);
        Assert.AreEqual(TreeNodeKind.ThisPc, model.Navigation.TreeRoots.Single().Kind);
        Assert.IsTrue(model.Listing.Entries.Count > 0, "this machine has at least one drive");
        Assert.IsTrue(model.Listing.Entries.All(e => e.IsThisPcItem && e.IsDirectory));
        Assert.AreEqual(model.Listing.Entries.Count, model.Listing.FolderCount);
    }

    [TestMethod]
    public async Task GoingToThisPc_LeavesTheFolder_AndWhatDescribedIt()
    {
        var model = await ModelHarness.AtAsync(_scratch);
        model.Listing.ListingFailure = "stale";

        model.Navigation.GoToThisPc(rebuildTree: true);

        Assert.IsTrue(model.Location.IsThisPc);
        Assert.IsFalse(model.Location.InContainer);
        Assert.IsNull(model.Listing.ListingFailure);
        Assert.IsTrue(model.Listing.Entries.All(e => e.IsThisPcItem));
    }
}
