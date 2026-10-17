using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Llama;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>The makers' repeat ban for Unlimited-OCR: no token may end a 35-token run already written among the last
/// 128 — and it bans what their <c>SlidingWindowNoRepeatNgramProcessor</c> bans, though the host sees only what was
/// written and they see the prompt too.</summary>
[TestClass]
[CoversNode("ai-host")]
public class RepeatBanTests
{
    private static readonly RepeatBan Makers = new(35, 128);

    private static List<int> Banned(IReadOnlyList<int> written)
    {
        var banned = new List<int>();
        LlamaSampler.Banned(written.ToArray(), Makers, banned);
        return banned;
    }

    /// <summary>The makers' processor, as they wrote it, over the whole sequence the model has read.</summary>
    private static HashSet<int> MakersBan(IReadOnlyList<int> sequence)
    {
        var banned = new HashSet<int>();
        int n = Makers.Run;
        if (sequence.Count < n) return banned;
        int start = Math.Max(0, sequence.Count - Makers.Within), end = sequence.Count - n + 1;
        var prefix = sequence.Skip(sequence.Count - (n - 1)).ToList();
        for (int idx = start; idx < end; idx++)
            if (sequence.Skip(idx).Take(n - 1).SequenceEqual(prefix)) banned.Add(sequence[idx + n - 1]);
        return banned;
    }

    [TestMethod]
    public void ARunWrittenAgain_CannotEndAsItDidBefore()
    {
        var run = Enumerable.Range(100, 35).ToList();
        var written = Enumerable.Range(1, 20).Concat(run).Concat(Enumerable.Range(500, 10)).Concat(run.Take(34)).ToList();

        CollectionAssert.AreEqual(new[] { 134 }, Banned(written), "the run's last token may not follow its first 34 again");
    }

    [TestMethod]
    public void ARunThatBeganOutsideTheLast128_IsNotBanned()
    {
        var run = Enumerable.Range(100, 35).ToList();
        var written = run.Concat(Enumerable.Range(1000, 80)).Concat(run.Take(34)).ToList();   // began 149 tokens back

        Assert.AreEqual(0, Banned(written).Count);
    }

    [TestMethod]
    public void AShortReply_HasNothingBanned()
    {
        Assert.AreEqual(0, Banned(Enumerable.Range(1, 34).ToList()).Count);
        Assert.AreEqual(0, Banned([]).Count);
    }

    [TestMethod]
    public void WhatIsBanned_IsWhatTheMakersBan_WithTheirPromptBeforeIt()
    {
        var random = new Random(11);
        var prompt = Enumerable.Repeat(128815, 300).Concat([3417, 11425, 16]).ToList();   // the page's tokens, then "document parsing."
        for (int trial = 0; trial < 200; trial++)
        {
            var written = new List<int>();
            while (written.Count < 40 + random.Next(300))
            {
                if (written.Count > 40 && random.Next(4) == 0)
                    written.AddRange(written.Skip(random.Next(written.Count - 35)).Take(random.Next(20, 60)));   // a passage again
                else
                    written.Add(random.Next(50, 60));   // a small vocabulary, so runs recur by chance too
            }

            CollectionAssert.AreEquivalent(MakersBan([.. prompt, .. written]).ToList(), Banned(written).Distinct().ToList(),
                                           $"trial {trial}, {written.Count} written");
        }
    }
}
