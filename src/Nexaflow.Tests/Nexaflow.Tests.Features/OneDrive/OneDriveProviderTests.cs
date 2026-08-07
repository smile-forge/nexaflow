using System.Linq;
using Nexaflow.Features.Common.ThisPc;
using Nexaflow.Features.OneDrive;
using Nexaflow.Features.OneDrive.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.OneDrive;

/// <summary>
/// Turning detected accounts into This PC rows, and what the user's settings do to them. The rule that
/// matters throughout: an override is keyed on the account's id, never its name, because the id becomes
/// the virtual root a saved tab points at.
/// </summary>
[TestClass]
[CoversNode("onedrive-provider")]
public class OneDriveProviderTests
{
    private static OneDriveThisPcProvider Provider(OneDriveConfig config, FakeRegistryView registry)
        => new(config, new OneDriveDetector(registry, _ => null));

    private static FakeRegistryView OneAccount() =>
        new FakeRegistryView().WithAccount("Business1", @"C:\Users\me\OneDrive - Contoso", displayName: "Contoso");

    [TestMethod]
    public void ADetectedAccountBecomesACloudRowPointingAtItsFolder()
    {
        var item = Provider(new OneDriveConfig(), OneAccount()).GetItems().Single();

        Assert.AreEqual("onedrive.Business1", item.Id);
        Assert.AreEqual("OneDrive - Contoso", item.Label);
        Assert.AreEqual(@"C:\Users\me\OneDrive - Contoso", item.TargetPath);
        Assert.AreEqual("OneDrive", item.TypeLabel);
        Assert.AreEqual(ThisPcItemIcon.Cloud, item.Icon);
        Assert.AreEqual(ThisPcItemBacking.LocalPath, item.Backing,
                        "it is a real folder, so the browser mounts it rather than serving it itself");
    }

    [TestMethod]
    public void WithNoOneDriveThereAreNoRowsAndNoComplaint()
    {
        Assert.AreEqual(0, Provider(new OneDriveConfig(), new FakeRegistryView()).GetItems().Count);
    }

    [TestMethod]
    public void HidingAnAccountRemovesItsRow()
    {
        var config = new OneDriveConfig
        {
            Overrides = [new SyncFolderOverride("onedrive.Business1", Hidden: true)],
        };

        Assert.AreEqual(0, Provider(config, OneAccount()).GetItems().Count);
    }

    [TestMethod]
    public void RenamingAnAccountChangesItsLabelButNotItsIdentity()
    {
        var config = new OneDriveConfig
        {
            Overrides = [new SyncFolderOverride("onedrive.Business1", Label: "Work")],
        };

        var item = Provider(config, OneAccount()).GetItems().Single();

        Assert.AreEqual("Work", item.Label);
        Assert.AreEqual("onedrive.Business1", item.Id,
                        "the id is the virtual root a pinned tab points at — a rename must not move it");
        Assert.AreEqual(@"C:\Users\me\OneDrive - Contoso", item.TargetPath);
    }

    [TestMethod]
    public void AnOverrideForAnAccountThatIsGoneDoesNotResurrectIt()
    {
        var config = new OneDriveConfig
        {
            Overrides = [new SyncFolderOverride("onedrive.Business9", Label: "Signed out long ago")],
        };

        var items = Provider(config, OneAccount()).GetItems();

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("onedrive.Business1", items[0].Id);
    }

    [TestMethod]
    public void AFolderTheUserAddedAppearsEvenWithNothingDetected()
    {
        var config = new OneDriveConfig
        {
            Custom = [new SyncFolderEntry("onedrive.custom.abc", "Archive", @"D:\Archive")],
        };

        var item = Provider(config, new FakeRegistryView()).GetItems().Single();

        Assert.AreEqual("onedrive.custom.abc", item.Id);
        Assert.AreEqual("Archive", item.Label);
        Assert.AreEqual(@"D:\Archive", item.TargetPath);
    }

    [TestMethod]
    public void AddedFoldersFollowTheDetectedOnes()
    {
        var config = new OneDriveConfig
        {
            Custom = [new SyncFolderEntry("onedrive.custom.abc", "Archive", @"D:\Archive")],
        };

        var items = Provider(config, OneAccount()).GetItems();

        CollectionAssert.AreEqual(new[] { "onedrive.Business1", "onedrive.custom.abc" },
                                  items.Select(i => i.Id).ToArray());
        Assert.IsTrue(items[0].SortOrder < items[1].SortOrder);
    }

    [TestMethod]
    public void AnIncompleteAddedFolderIsIgnoredRatherThanShownBroken()
    {
        var config = new OneDriveConfig
        {
            Custom =
            [
                new SyncFolderEntry("", "No id", @"D:\A"),
                new SyncFolderEntry("onedrive.custom.b", "No path", ""),
            ],
        };

        Assert.AreEqual(0, Provider(config, new FakeRegistryView()).GetItems().Count);
    }

    // ── Reacting to the user editing Options ─────────────────────────────────

    [TestMethod]
    public void ApplyingSettingsTellsTheBrowserToRequery()
    {
        var config   = new OneDriveConfig();
        var provider = Provider(config, OneAccount());
        var raised   = 0;
        provider.Changed += () => raised++;

        config.RaiseChanged();

        Assert.AreEqual(1, raised);
    }

    [TestMethod]
    public void ARowHiddenInOptionsDisappearsWithoutWaitingForTheMemoToLapse()
    {
        // GetItems is memoised so drawing This PC stays free; a config change has to cut through that,
        // or the row the user just hid would linger for half a minute.
        var config   = new OneDriveConfig();
        var provider = Provider(config, OneAccount());

        Assert.AreEqual(1, provider.GetItems().Count);

        config.Overrides = [new SyncFolderOverride("onedrive.Business1", Hidden: true)];
        config.RaiseChanged();

        Assert.AreEqual(0, provider.GetItems().Count);
    }

    [TestMethod]
    public void RepeatedCallsDoNotRereadTheRegistryEveryTime()
    {
        var registry = new CountingRegistryView(OneAccount());
        var provider = new OneDriveThisPcProvider(new OneDriveConfig(),
                                                  new OneDriveDetector(registry, _ => null));

        for (int i = 0; i < 5; i++) provider.GetItems();

        Assert.AreEqual(1, registry.Reads, "This PC redraws often; detection is memoised between draws");
    }

    private sealed class CountingRegistryView(FakeRegistryView inner) : IRegistryView
    {
        public int Reads { get; private set; }

        public IReadOnlyList<string> SubKeyNames(string path)
        {
            Reads++;
            return inner.SubKeyNames(path);
        }

        public string? GetString(string path, string valueName) => inner.GetString(path, valueName);
    }
}
