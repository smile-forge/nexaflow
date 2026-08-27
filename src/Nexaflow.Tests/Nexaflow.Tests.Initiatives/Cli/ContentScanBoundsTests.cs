using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Services.Initiatives.Cli;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Cli;

/// <summary>
/// `graph grep --mode content` has two bounds, and they must not be the same bound.
///
/// <para>
/// <c>--limit</c> bounds what is printed; <c>--scan-cap</c> bounds how much work is done. They were one loop
/// condition, so reaching the output limit ended the search and the reported total became "however many
/// turned up before we stopped looking" — while reading exactly like a total. The repo sweep for
/// <c>SupportsMultipleFiles</c> answered <b>40</b>. It is <b>124</b>, and the two call sites that decide
/// whether a multi-selection may run a file action both sort past the fortieth, so the honest-looking answer
/// supported the conclusion "nothing reads this flag" — which is false, and which this project's own hard
/// rule ("pattern searches go through the graph, not grep") makes it easy to act on.
/// </para>
/// <para>
/// A search tool may return fewer results than exist. It may not misreport how many exist, and it may not
/// stop looking because it stopped printing.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("Result bounding for the headless CLI — infrastructure, not a product-tree node.")]
public class ContentScanBoundsTests
{
    /// <summary>Nodes 0..n-1; every third one is a match, so the matches are spread across the whole list.</summary>
    private static List<GraphNode> Nodes(int n) =>
        [.. Enumerable.Range(0, n).Select(i => new GraphNode { Id = $"code:f{i:D4}.cs#T:T{i}", Type = "type" })];

    private static List<(int Line, string Text)> EveryThird(GraphNode n) =>
        int.Parse(n.Id[6..10]) % 3 == 0 ? [(1, "hit")] : [];

    private static readonly Action<GraphNode, List<(int Line, string Text)>> Ignore = (_, _) => { };

    [TestMethod]
    public void TheLimitTrimsPrinting_ButTheTotalIsStillTheTrueTotal()
    {
        // The regression, in one assertion: 300 nodes, 100 of them matching, printing capped at 10.
        var tally = Program.ScanContent(Nodes(300), limit: 10, scanCap: int.MaxValue, EveryThird, Ignore);

        Assert.AreEqual(10, tally.Reported, "printing is what --limit bounds");
        Assert.AreEqual(100, tally.Matched,
            "the total must count every match, not the ones printed before the limit was reached");
        Assert.AreEqual(300, tally.Scanned, "hitting the output limit must not stop the scan");
        Assert.IsFalse(tally.Capped);
    }

    [TestMethod]
    public void MatchesBeyondTheLimit_AreStillFound_NotJustUncounted()
    {
        // The specific shape that misled a real audit: every match the caller sees is an early one, and the
        // interesting call site is late. It must still be counted, so the summary line contradicts any
        // "that's all of them" reading of the printed list.
        var seen = new List<string>();
        var tally = Program.ScanContent(Nodes(300), limit: 5, scanCap: int.MaxValue, EveryThird,
                                        (n, _) => seen.Add(n.Id));

        CollectionAssert.AreEqual(
            new[] { "code:f0000.cs#T:T0", "code:f0003.cs#T:T3", "code:f0006.cs#T:T6",
                    "code:f0009.cs#T:T9", "code:f0012.cs#T:T12" },
            seen,
            "emission stops at the limit, in order");
        Assert.AreEqual(100, tally.Matched, "…but the 95 later matches were still found");
    }

    [TestMethod]
    public void TheScanCap_StopsTheWork_AndSaysSo()
    {
        var tally = Program.ScanContent(Nodes(300), limit: int.MaxValue, scanCap: 30, EveryThird, Ignore);

        Assert.IsTrue(tally.Capped, "the caller prints INCOMPLETE off this — without it the zero reads as absence");
        Assert.AreEqual(30, tally.Scanned);
        Assert.AreEqual(10, tally.Matched, "only what the capped scan actually reached");
    }

    [TestMethod]
    public void AnUncappedScanOverNoMatches_IsAnHonestZero()
    {
        // "0 matches" is only meaningful when everything was looked at. This is the case the whole split
        // exists to protect: absence reported as absence, not as "we stopped early".
        var tally = Program.ScanContent(Nodes(300), limit: 40, scanCap: int.MaxValue, _ => [], Ignore);

        Assert.AreEqual(0, tally.Matched);
        Assert.AreEqual(300, tally.Scanned);
        Assert.IsFalse(tally.Capped);
    }

    [TestMethod]
    public void WhenEverythingFits_NothingIsTrimmedAndNothingIsFlagged()
    {
        var tally = Program.ScanContent(Nodes(30), limit: 40, scanCap: int.MaxValue, EveryThird, Ignore);

        Assert.AreEqual(10, tally.Matched);
        Assert.AreEqual(10, tally.Reported, "Matched == Reported is what suppresses the 'showing N' suffix");
        Assert.IsFalse(tally.Capped);
    }

    [TestMethod]
    public void ALimitOfZero_CountsWithoutPrinting()
    {
        // Worth pinning: it is the "just tell me how many" case, and under the old loop it was "stop at once
        // and report none", which is the bug at its purest.
        var tally = Program.ScanContent(Nodes(300), limit: 0, scanCap: int.MaxValue, EveryThird, Ignore);

        Assert.AreEqual(0, tally.Reported);
        Assert.AreEqual(100, tally.Matched);
        Assert.AreEqual(300, tally.Scanned);
    }
}
