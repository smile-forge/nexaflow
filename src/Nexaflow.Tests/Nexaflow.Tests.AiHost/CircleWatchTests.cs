using Nexaflow.AiHost.Generation;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>A reply going in circles — the same stretch of tokens over and over — is caught; one that is not, never is.</summary>
[TestClass]
[CoversNode("ai-host")]
public class CircleWatchTests
{
    private static int? FirstCaught(IEnumerable<int> tokens)
    {
        var watch = new CircleWatch();
        int at = 0;
        foreach (var token in tokens)
        {
            if (watch.Take(token)) return at;
            at++;
        }
        return null;
    }

    [TestMethod]
    public void AStretchSaidOverAndOver_IsCaught_OnceItHasFilled256Tokens()
    {
        var thought = Enumerable.Range(0, 40).Select(i => 1000 + i).ToList();   // "The user wants … Let me check …"
        var tokens  = Enumerable.Range(0, 20).Select(i => 5000 + i).Concat(Enumerable.Repeat(thought, 20).SelectMany(t => t));

        Assert.AreEqual(20 + 256 - 1, FirstCaught(tokens), "caught as the circling fills 256 tokens");
    }

    [TestMethod]
    public void ALongStretch_IsCaughtTheThirdTimeRound()
    {
        var paragraph = Enumerable.Range(0, 300).Select(i => 2000 + i).ToList();
        var watch = new CircleWatch();
        int at = 0;
        foreach (var token in Enumerable.Repeat(paragraph, 4).SelectMany(t => t))
        {
            if (watch.Take(token)) break;
            at++;
        }
        Assert.AreEqual(3 * 300 - 1, at);
        Assert.AreEqual(300, watch.Stretch);
    }

    [TestMethod]
    public void OneTokenOverAndOver_IsCaught()
        => Assert.AreEqual(255, FirstCaught(Enumerable.Repeat(7, 1000)));

    [TestMethod]
    public void AReplyThatGoesSomewhere_IsNeverCaught_ThoughItRepeatsItselfAWhile()
    {
        var random = new Random(4);
        var prose  = Enumerable.Range(0, 20_000).Select(_ => random.Next(0, 5000));
        Assert.IsNull(FirstCaught(prose));

        var line  = Enumerable.Range(0, 30).Select(i => 3000 + i).ToList();   // the same table row, five times over: 150 tokens
        var table = Enumerable.Repeat(line, 5).SelectMany(t => t).Concat(Enumerable.Range(0, 500).Select(_ => random.Next(0, 5000)));
        Assert.IsNull(FirstCaught(table), "a few repeats, short of 256 tokens, are not circles");
    }
}
