using Nexaflow.AiHost.Llama;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>The likeliest token read straight from a read's scores, as llama.cpp's greedy sampler chooses it: the
/// highest score, the first of several that share it, wherever it falls against the vector width; a score the repeat ban
/// struck out is never chosen.</summary>
[TestClass]
[CoversNode("ai-host")]
public class LlamaScoresTests
{
    [TestMethod]
    public void TheLikeliest_IsTheHighestScore_WhereverItStands()
    {
        var random = new Random(7);
        foreach (int length in new[] { 1, 3, 7, 8, 9, 16, 31, 129_280 })
        {
            var scores = Enumerable.Range(0, length).Select(_ => (float)(random.NextDouble() * 20 - 10)).ToArray();
            foreach (int at in new[] { 0, length / 3, length / 2, length - 1 }.Distinct())
            {
                float was = scores[at];
                scores[at] = 11;
                Assert.AreEqual(at, LlamaScores.Likeliest(scores), $"the highest of {length} at {at}");
                scores[at] = was;
            }
        }
    }

    [TestMethod]
    public void OfScoresThatTie_TheFirstIsTheLikeliest()
    {
        float[] scores = [1, 5, 2, 5, 5, 0, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0];
        Assert.AreEqual(1, LlamaScores.Likeliest(scores));
    }

    [TestMethod]
    public void AStruckScore_IsNeverTheLikeliest()
    {
        var scores = Enumerable.Repeat(float.NegativeInfinity, 1000).ToArray();
        scores[998] = -40;
        Assert.AreEqual(998, LlamaScores.Likeliest(scores), "the one score left standing, however low");
    }
}
