using Nexaflow.AiHost.Models;
using Nexaflow.Tests.Fixtures;
using Loaded = Nexaflow.AiHost.Models.VramBudget.Loaded;

namespace Nexaflow.Tests.AiHost;

/// <summary>Admission against a GPU's budget.</summary>
[TestClass]
[CoversNode("ai-host")]
public class VramBudgetTests
{
    private const long GB = 1L << 30;

    [TestMethod]
    public void AModelThatFits_UnloadsNothing()
        {
            var admission = VramBudget.Plan(24 * GB, [new Loaded("gemma", 8 * GB, Resident: true, InUse: false, 1)], 8 * GB)!;

            Assert.AreEqual((0, false), (admission.Unload.Count, admission.Waits));
        }

    [TestMethod]
    public void ModelsLoadedOnDemand_GoLeastRecentlyUsedFirst_AndOnlyAsManyAsTheRoomNeeds()
    {
        Loaded[] loaded =
        [
            new("resident", 8 * GB, Resident: true, InUse: false, LastUsed: 1),
            new("older", 6 * GB, Resident: false, InUse: false, LastUsed: 2),
            new("newer", 6 * GB, Resident: false, InUse: false, LastUsed: 3),
        ];

        CollectionAssert.AreEqual(new[] { "older" }, VramBudget.Plan(24 * GB, loaded, 6 * GB)!.Unload.ToArray());
        CollectionAssert.AreEqual(new[] { "older", "newer" }, VramBudget.Plan(24 * GB, loaded, 12 * GB)!.Unload.ToArray());
    }

    [TestMethod]
    public void WhatMustStay_NeverGoes_AndAModelThatCannotFitIsRefusedWithNothingUnloaded()
    {
        Loaded[] loaded =
        [
            new("resident", 8 * GB, Resident: true, InUse: false, LastUsed: 1),
            new("busy", 8 * GB, Resident: false, InUse: true, LastUsed: 2),
            new("spare", 4 * GB, Resident: false, InUse: false, LastUsed: 3),
        ];

        Assert.IsNull(VramBudget.Plan(24 * GB, loaded, 20 * GB), "the resident model stays, and nothing else is enough even once idle");
    }

    [TestMethod]
    public void WhenOnlyAModelInUseStandsInTheWay_TheLoadWaitsForIt_UnloadingNothingYet()
    {
        Loaded[] loaded =
        [
            new("resident", 8 * GB, Resident: true, InUse: false, LastUsed: 1),
            new("busy", 8 * GB, Resident: false, InUse: true, LastUsed: 2),
            new("spare", 4 * GB, Resident: false, InUse: false, LastUsed: 3),
        ];

        var admission = VramBudget.Plan(24 * GB, loaded, 12 * GB)!;

        Assert.IsTrue(admission.Waits);
        Assert.AreEqual(0, admission.Unload.Count, "nothing goes until it is known to be enough");
    }

    [TestMethod]
    public void APicture_MayHaveIdleResidentsMakeWay_AfterEveryOtherModel()
    {
        Loaded[] loaded =
        [
            new("speech", 6 * GB, Resident: true, InUse: false, LastUsed: 1),
            new("chat", 6 * GB, Resident: true, InUse: false, LastUsed: 2),
            new("encoder", 4 * GB, Resident: false, InUse: false, LastUsed: 3),
        ];

        Assert.IsNull(VramBudget.Plan(16 * GB, loaded, 10 * GB), "without a picture's room, residents stay");
        CollectionAssert.AreEqual(new[] { "encoder", "speech" }, VramBudget.Plan(16 * GB, loaded, 10 * GB, displaceResidents: true)!.Unload.ToArray(),
            "the spare part goes first, then the resident used longest ago");
    }

    [TestMethod]
    public void APicture_WaitsForAResidentInUse_RatherThanTakingItAway()
    {
        Loaded[] loaded =
        [
            new("chat", 10 * GB, Resident: true, InUse: true, LastUsed: 1),
        ];

        Assert.IsTrue(VramBudget.Plan(16 * GB, loaded, 10 * GB, displaceResidents: true)!.Waits, "a reply in progress keeps its model");
    }
}
