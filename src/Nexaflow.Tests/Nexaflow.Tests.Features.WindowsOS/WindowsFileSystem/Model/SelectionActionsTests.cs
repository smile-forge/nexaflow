using System.IO;
using Nexaflow.Features.WindowsFileSystem.Model;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem.Model;

/// <summary>
/// What a selection's actions act on, with no strip over them: the selected paths, or the open folder when nothing is
/// selected — where "New" comes first — and nothing at all on This PC with nothing clicked.
/// </summary>
[TestClass]
[CoversNode("winfs-action-backing")]
public class SelectionActionsTests
{
    private string _scratch = string.Empty;
    private string _file = string.Empty;

    [TestInitialize]
    public void CreateScratch()
    {
        _scratch = ModelHarness.Scratch();
        _file    = Path.Combine(_scratch, "a.txt");
        File.WriteAllText(_file, "a");
    }

    [TestCleanup]
    public void RemoveScratch() { try { Directory.Delete(_scratch, recursive: true); } catch { } }

    [TestMethod]
    public async Task WithNothingSelected_TheOpenFolderIsActedOn_AndNewComesFirst()
    {
        var model = await ModelHarness.AtAsync(_scratch);

        var offered = await model.Actions.ForStripAsync([]);

        CollectionAssert.AreEqual(new[] { _scratch }, offered.Targets.ToArray());
        Assert.IsTrue(offered.OffersNew);
    }

    [TestMethod]
    public async Task WithAFileSelected_ItIsActedOn_AndNewIsNotOffered()
    {
        var model = await ModelHarness.AtAsync(_scratch);
        var row   = model.Listing.Entries.Single(e => e.Name == "a.txt");

        var offered = await model.Actions.ForStripAsync([row]);

        CollectionAssert.AreEqual(new[] { _file }, offered.Targets.ToArray());
        Assert.IsFalse(offered.OffersNew);
    }

    [TestMethod]
    public async Task OnThisPc_NothingClickedHasNoActions_AndNewIsNotOffered()
    {
        var model = ModelHarness.ThisPc();

        Assert.AreEqual(0, model.Actions.ForMenu([]).Count);
        Assert.AreEqual(0, model.Actions.TargetsOf([]).Count);
        Assert.IsFalse((await model.Actions.ForStripAsync([])).OffersNew);
    }
}
