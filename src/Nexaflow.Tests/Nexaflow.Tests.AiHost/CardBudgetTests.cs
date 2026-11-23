using Nexaflow.AiHost;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>A GPU's budget as what is free on the card says it now: what another program takes is left it, what it gives back
/// is there to use, and the host's own models are counted back in rather than twice.</summary>
[TestClass]
[CoversNode("ai-host")]
public class CardBudgetTests
{
    private const long GB = 1L << 30;

    private static readonly HostGpu Card = new(0, "card", "0000:01:00.0", 24 * GB, 20 * GB - HostGpu.ReservedBytes, 20 * GB);

    [TestMethod]
    public void TheBudget_CountsTheHostsOwnModelsBack_AndLeavesAnotherProgramWhatItTook()
    {
        // 6 GB of the host's models loaded, and a game taking 4 GB since it started: 10 GB free.
        Assert.AreEqual(16 * GB - HostGpu.ReservedBytes, ExecutionRuntime.BudgetOf(Card, 10 * GB, 6 * GB));
    }

    [TestMethod]
    public void WhatAnotherProgramGivesBack_IsThereToUse_ButNeverMoreThanTheCardHas()
    {
        Assert.AreEqual(23 * GB - HostGpu.ReservedBytes, ExecutionRuntime.BudgetOf(Card, 17 * GB, 6 * GB), "past the budget it started with");
        Assert.AreEqual(24 * GB - HostGpu.ReservedBytes, ExecutionRuntime.BudgetOf(Card, 20 * GB, 6 * GB), "estimates past what the card holds");
        Assert.AreEqual(0, ExecutionRuntime.BudgetOf(Card, 0, 0));
    }

}
