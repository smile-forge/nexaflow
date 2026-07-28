using Nexaflow.Features.WindowsSearch.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch;

/// <summary>
/// The rule behind the verification banner. Kept pure precisely so this is testable — the threshold and
/// its wording are what the user actually experiences, and neither should need an index, a UI thread or a
/// background sweep to pin down.
/// </summary>
[TestClass]
[CoversNode("search-verify")]
public class VerificationPlannerTests
{
    [TestMethod]
    public void NoCandidates_NothingToVerify()
    {
        var plan = VerificationPlanner.ForNewResults(verified: 7, candidates: 0);

        Assert.AreEqual(VerifyPhase.Done, plan.Phase);
        Assert.AreEqual(0, plan.SweepNow, "no file should be opened when every row matched by name");
        StringAssert.Contains(plan.Banner, "Nothing else to check");
    }

    [TestMethod]
    public void NothingFoundAtAll_SaysSoPlainly()
    {
        // "0 file name(s) matched" reads as a partial answer about names when the search simply found
        // nothing — the state a user hits when a pattern is wrong.
        var plan = VerificationPlanner.ForNewResults(verified: 0, candidates: 0);

        Assert.AreEqual(VerifyPhase.Done, plan.Phase);
        Assert.AreEqual("No matches.", plan.Banner);
    }

    [TestMethod]
    public void FewCandidates_VerifyWithoutAsking()
    {
        var plan = VerificationPlanner.ForNewResults(verified: 2, candidates: 9);

        Assert.AreEqual(VerifyPhase.Running, plan.Phase);
        Assert.AreEqual(9, plan.SweepNow, "a handful of files is not worth interrupting the user for");
        StringAssert.Contains(plan.Banner, "verifying 9");
    }

    [TestMethod]
    public void ExactlyTheLimit_StillRunsUnattended()
    {
        var plan = VerificationPlanner.ForNewResults(verified: 0, candidates: VerificationPlanner.AutoVerifyLimit);

        Assert.AreEqual(VerifyPhase.Running, plan.Phase, "the limit is inclusive — 50 is fine, 51 asks");
        Assert.AreEqual(VerificationPlanner.AutoVerifyLimit, plan.SweepNow);
    }

    [TestMethod]
    public void ManyCandidates_AsksButStillSweepsAFirstSlice()
    {
        var plan = VerificationPlanner.ForNewResults(verified: 12, candidates: 340);

        Assert.AreEqual(VerifyPhase.Prompt, plan.Phase);

        // The important part: it does NOT stall on a question. The visible top of the list is settled
        // anyway, so the results are usable while the user decides about the tail.
        Assert.AreEqual(VerificationPlanner.AutoVerifyLimit, plan.SweepNow);
        StringAssert.Contains(plan.Banner, "340 more");
        StringAssert.Contains(plan.Banner, "check them?");
    }

    [TestMethod]
    public void Banner_SeparatesProvenFromPossible()
    {
        // "12 matched by name" is a different claim from "352 results", and conflating them is what makes a
        // speculative result set misleading.
        var plan = VerificationPlanner.ForNewResults(verified: 12, candidates: 340);

        StringAssert.Contains(plan.Banner, "12 matched by name");
    }

    [TestMethod]
    public void AfterSweep_WithNothingLeft_IsDone()
    {
        var plan = VerificationPlanner.AfterSweep(confirmed: 18, stillPending: 0);

        Assert.AreEqual(VerifyPhase.Done, plan.Phase);
        StringAssert.Contains(plan.Banner, "18 confirmed");
    }

    [TestMethod]
    public void AfterSweep_WithATailLeft_AsksAgain()
    {
        // Finishing the first 50 of 340 must re-offer the rest, not quietly report a total as if settled.
        var plan = VerificationPlanner.AfterSweep(confirmed: 31, stillPending: 290);

        Assert.AreEqual(VerifyPhase.Prompt, plan.Phase);
        StringAssert.Contains(plan.Banner, "290 more");
    }

    [TestMethod]
    public void ConfirmedNeverIncludesUnsettledRows()
    {
        // The reported arithmetic bug: "34 matches with 26 possibles" meant 8 were actually proven, but
        // the banner had been handed the total row count as if it were the confirmed count.
        var plan = VerificationPlanner.AfterSweep(confirmed: 8, stillPending: 26);

        StringAssert.Contains(plan.Banner, "8 confirmed");
        Assert.IsFalse(plan.Banner.Contains("34"), "the total row count is not a confirmed count");
    }

    [TestMethod]
    public void UnreadableRows_AreReportedSeparately_AndNotOfferedForRechecking()
    {
        // Files whose text is compressed can never be settled by re-reading them. Counting them as
        // "might match — check them?" makes the button look broken: it runs, nothing changes.
        var plan = VerificationPlanner.AfterSweep(confirmed: 8, stillPending: 0, unreadable: 26);

        Assert.AreEqual(VerifyPhase.Done, plan.Phase, "nothing left that checking again could resolve");
        StringAssert.Contains(plan.Banner, "8 confirmed");
        StringAssert.Contains(plan.Banner, "26 couldn't be checked");
    }

    [TestMethod]
    public void UnreadableAndPending_AreDistinctInTheBanner()
    {
        var plan = VerificationPlanner.AfterSweep(confirmed: 5, stillPending: 12, unreadable: 3);

        Assert.AreEqual(VerifyPhase.Prompt, plan.Phase, "12 rows can still be settled, so still offer");
        StringAssert.Contains(plan.Banner, "3 couldn't be checked");
        StringAssert.Contains(plan.Banner, "12 more");
    }

    [TestMethod]
    public void ProbableHits_AreCountedApartFromConfirmedOnes()
    {
        // A hit found by scanning bytes of a format we don't understand is real but may be incidental.
        // Folding it into "confirmed" overstates it; hiding it loses a genuine result.
        var plan = VerificationPlanner.AfterSweep(confirmed: 8, stillPending: 0, unreadable: 4, uncertain: 6);

        StringAssert.Contains(plan.Banner, "8 confirmed");
        StringAssert.Contains(plan.Banner, "6 probable");
        StringAssert.Contains(plan.Banner, "4 couldn't be checked");
    }

    [TestMethod]
    public void CleanSweep_MentionsOnlyWhatHappened()
    {
        // No noise about categories that are empty.
        var plan = VerificationPlanner.AfterSweep(confirmed: 12, stillPending: 0);

        Assert.AreEqual("12 confirmed.", plan.Banner);
    }

    // ── Where the rows came from ──────────────────────────────────────────────

    [TestMethod]
    public void IndexResults_SayTheyCameFromTheIndex()
        => Assert.AreEqual("Windows search index returned 498 file(s). ",
                           VerificationPlanner.OriginPrefix(SearchOrigin.Index, 498));

    [TestMethod]
    public void WalkedResults_SayTheyCameFromAFolderScan()
        => Assert.AreEqual("Folder scan returned 42 file(s). ",
                           VerificationPlanner.OriginPrefix(SearchOrigin.FolderScan, 42));

    [TestMethod]
    public void BannerLeadsWithWhereTheRowsCameFrom()
    {
        // A count with no source is unreadable: the index and a folder scan cover different files and match
        // on different things, so 498 means nothing until you know which produced it.
        var prefix = VerificationPlanner.OriginPrefix(SearchOrigin.Index, 498);

        var opening = VerificationPlanner.ForNewResults(145, 353, prefix);
        StringAssert.StartsWith(opening.Banner, "Windows search index returned 498 file(s). ");
        StringAssert.Contains(opening.Banner, "145 matched by name");

        var closing = VerificationPlanner.AfterSweep(200, 0, unreadable: 240, uncertain: 28, originPrefix: prefix);
        StringAssert.StartsWith(closing.Banner, "Windows search index returned 498 file(s). ");
        StringAssert.Contains(closing.Banner, "200 confirmed");
    }

    [TestMethod]
    public void UnreadableNote_SaysWhyRatherThanJustThatItFailed()
    {
        // "couldn't be checked" alone reads as a defect; the reason is what tells the user an extractor for
        // that file type is the missing piece.
        var plan = VerificationPlanner.AfterSweep(confirmed: 0, stillPending: 0, unreadable: 240);

        StringAssert.Contains(plan.Banner, "text is compressed or encoded");
    }

    [TestMethod]
    public void AfterSkip_SaysWhatWasLeftUnchecked()
    {
        // Declining must not read as "31 results" — the user needs to know the set is still speculative.
        var plan = VerificationPlanner.AfterSkip(confirmed: 31, unchecked_: 290);

        Assert.AreEqual(VerifyPhase.Done, plan.Phase);
        StringAssert.Contains(plan.Banner, "290 unchecked");
    }
}
