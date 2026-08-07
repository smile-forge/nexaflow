using System.Linq;
using Nexaflow.Features.OneDrive.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.OneDrive;

/// <summary>
/// Reading the machine's configured OneDrive accounts. The registry is messier than the documentation
/// suggests, and every case below was observed rather than imagined: a co-authoring pseudo-account
/// sitting alongside the real ones, an account with a blank display name, an account present but not
/// syncing anywhere.
/// </summary>
[TestClass]
[CoversNode("onedrive-detect")]
public class OneDriveDetectorTests
{
    private static OneDriveDetector Detector(FakeRegistryView registry, params (string Name, string Value)[] env)
        => new(registry, name => env.FirstOrDefault(e => e.Name == name).Value);

    [TestMethod]
    public void WithNoOneDriveOnTheMachineNothingIsFound()
    {
        var accounts = Detector(new FakeRegistryView()).Detect();

        Assert.AreEqual(0, accounts.Count, "no OneDrive is an ordinary state, not a failure");
    }

    [TestMethod]
    public void APersonalAccountBecomesASingleRowNamedTheWayExplorerNamesIt()
    {
        var registry = new FakeRegistryView().WithAccount("Personal", @"C:\Users\me\OneDrive");

        var account = Detector(registry).Detect().Single();

        Assert.AreEqual("onedrive.Personal", account.Id);
        Assert.AreEqual(@"C:\Users\me\OneDrive", account.FolderPath);
        Assert.AreEqual("OneDrive", account.Label, "there is only one personal account, so it isn't qualified");
        Assert.IsFalse(account.IsBusiness);
    }

    [TestMethod]
    public void AWorkAccountIsQualifiedByItsOrganisation()
    {
        var registry = new FakeRegistryView()
            .WithAccount("Business1", @"C:\Users\me\OneDrive - Contoso", displayName: "Contoso");

        var account = Detector(registry).Detect().Single();

        Assert.AreEqual("onedrive.Business1", account.Id);
        Assert.AreEqual("OneDrive - Contoso", account.Label);
        Assert.IsTrue(account.IsBusiness);
    }

    [TestMethod]
    public void TheFileCoAuthBookkeepingKeyIsNotAnAccount()
    {
        // It sits right beside the real accounts with every value blank.
        var registry = new FakeRegistryView()
            .WithAccount("Personal", @"C:\Users\me\OneDrive")
            .WithAccount("FileCoAuth", userFolder: "", displayName: "", userEmail: "");

        var accounts = Detector(registry).Detect();

        Assert.AreEqual(1, accounts.Count);
        Assert.AreEqual("onedrive.Personal", accounts[0].Id);
    }

    [TestMethod]
    public void AnAccountThatIsNotSyncingAnywhereIsSkipped()
    {
        var registry = new FakeRegistryView()
            .WithAccount("Business1", userFolder: "   ", displayName: "Contoso")
            .WithAccount("Business2", @"C:\Users\me\OneDrive - Fabrikam", displayName: "Fabrikam");

        var account = Detector(registry).Detect().Single();

        Assert.AreEqual("onedrive.Business2", account.Id);
    }

    [TestMethod]
    public void AWorkAccountWithNoDisplayNameFallsBackToItsEmail()
    {
        var registry = new FakeRegistryView()
            .WithAccount("Business1", @"C:\Work", displayName: "", userEmail: "me@contoso.com");

        Assert.AreEqual("OneDrive - me@contoso.com", Detector(registry).Detect().Single().Label);
    }

    [TestMethod]
    public void TwoUnnamedWorkAccountsAreStillTellableApart()
    {
        var registry = new FakeRegistryView()
            .WithAccount("Business1", @"C:\A")
            .WithAccount("Business2", @"C:\B");

        var labels = Detector(registry).Detect().Select(a => a.Label).ToArray();

        Assert.AreEqual(2, labels.Distinct().Count(), "two rows called the same thing would be unusable");
    }

    [TestMethod]
    public void EachWorkAccountGetsItsOwnRow()
    {
        var registry = new FakeRegistryView()
            .WithAccount("Personal",  @"C:\Users\me\OneDrive")
            .WithAccount("Business1", @"C:\Users\me\OneDrive - Contoso",  displayName: "Contoso")
            .WithAccount("Business2", @"C:\Users\me\OneDrive - Fabrikam", displayName: "Fabrikam");

        var ids = Detector(registry).Detect().Select(a => a.Id).ToArray();

        CollectionAssert.AreEqual(
            new[] { "onedrive.Personal", "onedrive.Business1", "onedrive.Business2" }, ids);
    }

    [TestMethod]
    public void AnUnrecognisedSubkeyIsIgnoredEvenIfItLooksUsable()
    {
        var registry = new FakeRegistryView().WithAccount("SomethingElse", @"C:\Somewhere");

        Assert.AreEqual(0, Detector(registry).Detect().Count);
    }

    // ── Environment fallback ─────────────────────────────────────────────────

    [TestMethod]
    public void TheEnvironmentIsConsultedOnlyWhenTheRegistrySaidNothing()
    {
        var registry = new FakeRegistryView().WithAccount("Personal", @"C:\Users\me\OneDrive");

        var accounts = Detector(registry, ("OneDriveConsumer", @"C:\Somewhere\Else")).Detect();

        Assert.AreEqual(1, accounts.Count, "mixing the two sources would list the same account twice");
        Assert.AreEqual(@"C:\Users\me\OneDrive", accounts[0].FolderPath);
    }

    [TestMethod]
    public void WithAnEmptyRegistryTheEnvironmentStillFindsTheFolder()
    {
        var accounts = Detector(new FakeRegistryView(), ("OneDriveConsumer", @"C:\Users\me\OneDrive")).Detect();

        Assert.AreEqual(@"C:\Users\me\OneDrive", accounts.Single().FolderPath);
    }

    [TestMethod]
    public void TheEnvironmentDoesNotListOnePathTwiceUnderTwoNames()
    {
        // %OneDrive% normally duplicates whichever specific variable is set.
        var accounts = Detector(new FakeRegistryView(),
            ("OneDriveConsumer", @"C:\Users\me\OneDrive"),
            ("OneDrive",         @"C:\Users\me\OneDrive\")).Detect();

        Assert.AreEqual(1, accounts.Count);
    }

    // ── The id is a path segment ─────────────────────────────────────────────

    [TestMethod]
    public void EveryIdCanFormAVirtualRootSegment()
    {
        // The id becomes ::{id} in the browser and is baked into saved tab state, so it must survive as
        // one path segment and must not follow the display name.
        var registry = new FakeRegistryView()
            .WithAccount("Personal",  @"C:\A", displayName: @"Name\With/Slashes:And")
            .WithAccount("Business1", @"C:\B", displayName: @"C:\Another");

        foreach (var account in Detector(registry).Detect())
        {
            Assert.IsFalse(account.Id.AsSpan().IndexOfAny(@"\/:") >= 0,
                           $"id '{account.Id}' would break the ::id path grammar");
            Assert.IsTrue(account.Id.StartsWith("onedrive."), "ids stay namespaced to this provider");
        }
    }
}
