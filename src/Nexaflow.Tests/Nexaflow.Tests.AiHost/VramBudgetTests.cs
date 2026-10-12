using Nexaflow.AiHost.Models;
using Nexaflow.Tests.Fixtures;
using Loaded = Nexaflow.AiHost.Models.VramBudget.Loaded;

namespace Nexaflow.Tests.AiHost;

/// <summary>Admission against the GPU's budget.</summary>
[TestClass]
[CoversNode("ai-host")]
public class VramBudgetTests
{
    private const long GB = 1L << 30;

    [TestMethod]
    public void AModelThatFits_UnloadsNothing()
        => Assert.AreEqual(0, VramBudget.Plan(24 * GB, [new Loaded("gemma", 8 * GB, Resident: true, InUse: false, 1)], 8 * GB)!.Count);

    [TestMethod]
    public void ModelsLoadedOnDemand_GoLeastRecentlyUsedFirst_AndOnlyAsManyAsTheRoomNeeds()
    {
        Loaded[] loaded =
        [
            new("resident", 8 * GB, Resident: true, InUse: false, LastUsed: 1),
            new("older", 6 * GB, Resident: false, InUse: false, LastUsed: 2),
            new("newer", 6 * GB, Resident: false, InUse: false, LastUsed: 3),
        ];

        CollectionAssert.AreEqual(new[] { "older" }, VramBudget.Plan(24 * GB, loaded, 6 * GB)!.ToArray());
        CollectionAssert.AreEqual(new[] { "older", "newer" }, VramBudget.Plan(24 * GB, loaded, 12 * GB)!.ToArray());
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

        Assert.IsNull(VramBudget.Plan(24 * GB, loaded, 12 * GB), "the resident model and the one in use stay, and the spare is not enough");
    }
}
