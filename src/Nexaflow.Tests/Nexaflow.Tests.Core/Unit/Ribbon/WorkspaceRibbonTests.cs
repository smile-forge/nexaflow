using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.Ribbon;

/// <summary>
/// A workspace's ribbon: the shipped buttons until it is saved, and again whenever it would be left with none; every window
/// hears a change with who made it; and a pin of a page already on it is refused, its place given instead.
/// </summary>
[TestClass]
public class WorkspaceRibbonTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup() => _dir = Directory.CreateDirectory(Path.Combine(TestTemp.Root, "nexaflow-ribbon-" + Guid.NewGuid().ToString("N"))).FullName;

    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private static RibbonEntry Button(string label, string kind) => new() { Label = label, PageKind = kind };

    [TestMethod]
    [CoversNode("ribbon-defaults")]
    public void NothingSaved_IsTheShippedButtons_AndAnEmptiedRibbon_GetsThemBack_ForEveryWindow()
    {
        var ribbon  = new WorkspaceRibbon(_dir);
        var shipped = RibbonLayoutService.LoadDefaults().Select(entry => entry.Label).ToList();
        CollectionAssert.AreEqual(shipped, ribbon.Entries.Select(entry => entry.Label).ToList());

        object? heardFrom = "nothing heard";
        ribbon.Changed += by => heardFrom = by;
        ribbon.Save([], by: this);

        CollectionAssert.AreEqual(shipped, ribbon.Entries.Select(entry => entry.Label).ToList());
        Assert.IsNull(heardFrom, "the window that emptied it takes the shipped buttons up too");
    }

    [TestMethod]
    [CoversNode("ribbon-layout-sync")]
    public void AnEdit_IsSaved_AndHeardWithWhoMadeIt()
    {
        var ribbon = new WorkspaceRibbon(_dir);
        object? heardFrom = null;
        ribbon.Changed += by => heardFrom = by;

        ribbon.Save([Button("Notes", "Notes")], by: this);

        Assert.AreSame(this, heardFrom);
        Assert.AreEqual("Notes", new WorkspaceRibbon(_dir).Entries.Single().Label, "read back from the workspace's folder");
    }

    [TestMethod]
    [CoversNode("ribbon-pin-tab")]
    [CoversNode("ribbon-pin-dedup")]
    public void ATabIsPinnedWhereItWasDropped_AndPinnedAgain_IsRefusedWithItsPlace()
    {
        var ribbon  = new WorkspaceRibbon(_dir);
        var runtime = TestRuntimes.Unstarted(new Workspace { Name = "W" });
        ribbon.Save([Button("A", "A"), Button("B", "B")], by: this);
        var tab = new Page { PageKind = "Test.Unpinnable", Title = "Report", Icon = "📄", PageParams = new() { ["Path"] = @"C:\r" } };

        var changes = 0;
        ribbon.Changed += _ => changes++;

        Assert.AreEqual(0, ribbon.PinTab(tab, at: 1, runtime).Count);
        CollectionAssert.AreEqual(new[] { "A", "Report", "B" }, ribbon.Entries.Select(entry => entry.Label).ToArray());
        Assert.AreEqual(@"C:\r", ribbon.Entries[1].PageParams!["Path"]);

        CollectionAssert.AreEqual(new[] { 1 }, ribbon.PinTab(tab, at: -1, runtime).ToArray());
        Assert.AreEqual(3, ribbon.Entries.Count);
        Assert.AreEqual(1, changes, "a refused pin changes nothing");
    }
}
