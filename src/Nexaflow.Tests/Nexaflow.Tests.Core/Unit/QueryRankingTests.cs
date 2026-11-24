using System;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Who has a line typed in the AI bar: a symbol's owners when one claims it, whoever claims it as typed when none does,
/// and no outright winner for a handler that says it is unsure.
/// </summary>
[TestClass]
[CoversNode("console-routing-group")]
public sealed class QueryRankingTests
{
    private sealed class Handler(string? symbol, Func<string, bool, float> score) : IQueryHandler
    {
        public string Description => "test";
        public string? Symbol => symbol;
        public float CanProcess(string input, bool prefixed, IPageViewModel? pageVm = null) => score(input, prefixed);
        public System.Threading.Tasks.Task<string?> ProcessAsync(string input, bool prefixed, IPageViewModel? pageVm = null)
            => System.Threading.Tasks.Task.FromResult<string?>(null);
    }

    private static readonly Handler QuickOpen = new("/", (line, prefixed) => prefixed && line == "Projects" ? 1f : 0f);
    private static readonly Handler JsonPath  = new("$", (_, _) => 0f);
    private static readonly Handler Console   = new(">", (line, prefixed) =>
        prefixed || line.Contains('/') ? 0.95f : line == "make install" ? 0.5f : 0f);

    private static readonly IQueryHandler[] All = [QuickOpen, JsonPath, Console];

    [TestMethod]
    [DataRow("/usr/bin/env python3")]
    [DataRow("$HOME/bin/deploy")]
    public void ASymbolNoOwnerClaims_WasNoPrefix_AndTheLineKeepsIt(string line)
    {
        var (_, winner, effective, prefixed) = AIService.RankHandlers(All, line, null);

        Assert.AreSame(Console, winner);
        Assert.AreEqual(line, effective);
        Assert.IsFalse(prefixed);
    }

    [TestMethod]
    public void ASymbolItsOwnerClaims_StillRoutesToIt()
    {
        var (_, winner, effective, prefixed) = AIService.RankHandlers(All, "/Projects", null);

        Assert.AreSame(QuickOpen, winner);
        Assert.AreEqual("Projects", effective);
        Assert.IsTrue(prefixed);
    }

    [TestMethod]
    public void AnAngleBracketForcesTheConsole()
    {
        var (_, winner, effective, _) = AIService.RankHandlers(All, "> what is this", null);

        Assert.AreSame(Console, winner);
        Assert.AreEqual("what is this", effective);
    }

    [TestMethod]
    public void AnUnsureHandler_IsACandidateForTheModel_AndNoWinner()
    {
        var (scored, winner, _, _) = AIService.RankHandlers(All, "make install", null);

        Assert.IsNull(winner);
        Assert.AreEqual(1, scored.Count);
    }

    [TestMethod]
    public void ASymbolNobodyClaimsEitherWay_LeavesTheLineToTheAssistant()
    {
        var (scored, winner, _, _) = AIService.RankHandlers(All, "$nothing", null);

        Assert.IsNull(winner);
        Assert.AreEqual(0, scored.Count, "nothing to disambiguate, so the assistant has it");
    }
}
