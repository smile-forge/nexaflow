using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Ast;

/// <summary>
/// Things standing for characters, indexed by how wide each is: where each starts and which holds a place, answered as a list
/// adding up the widths answers them, through every run replaced and every thing added — without anybody's start written down.
/// </summary>
[TestClass]
[CoversNode("windowed-reading")]
public class ContentIndexTests
{
    private sealed record Wide(int Width) : IContentWidth;

    [TestMethod]
    public void WhereEachStartsAndWhichHoldsAPlace_AreWhatAddingUpTheWidthsSays_ThroughEveryEdit()
    {
        var random = new Random(77);
        var expected = new List<Wide>();
        var index = ContentIndex<Wide>.Empty;

        for (var step = 0; step < 2000; step++)
        {
            if (step % 5 == 0 || expected.Count == 0)
            {
                var item = new Wide(random.Next(0, 50));
                expected.Add(item);
                index = index.Added(item);
            }
            else
            {
                var from = random.Next(expected.Count + 1);
                var to = Math.Min(expected.Count, from + random.Next(0, 40));
                var items = Enumerable.Range(0, random.Next(0, 60)).Select(_ => new Wide(random.Next(0, 50))).ToList();

                expected.RemoveRange(from, to - from);
                expected.InsertRange(from, items);
                index = index.Replaced(from, to, items);
            }

            Same(expected, index, random, $"step {step}");
        }
    }

    [TestMethod]
    public void ReplacingARunKeepsEveryOtherThing_AsTheVeryThingItWas()
    {
        var items = Enumerable.Range(0, 5000).Select(at => new Wide(at % 7 + 1)).ToList();
        var index = ContentIndex<Wide>.Of(items).Replaced(2500, 2502, [new Wide(3)]);

        Assert.AreSame(items[2499], index[2499].Item);
        Assert.AreSame(items[2502], index[2501].Item);
        Assert.AreEqual(items.Take(2501).Sum(item => item.Width) - items[2500].Width + 3, index[2501].Start);
    }

    private static void Same(List<Wide> expected, ContentIndex<Wide> index, Random random, string when)
    {
        Assert.AreEqual(expected.Count, index.Count, when);
        Assert.AreEqual(expected.Sum(item => item.Width), index.Width, when);

        var starts = new List<int>(expected.Count);
        var start = 0;
        foreach (var item in expected) { starts.Add(start); start += item.Width; }

        for (var ask = 0; ask < Math.Min(20, expected.Count); ask++)
        {
            var at = random.Next(expected.Count);
            Assert.AreSame(expected[at], index[at].Item, $"{when}: thing {at}");
            Assert.AreEqual(starts[at], index[at].Start, $"{when}: where thing {at} starts");

            var offset = random.Next(start + 1);
            var holding = -1;
            for (var (look, from) = (0, 0); look < expected.Count; from += expected[look].Width, look++)
                if (offset < from + expected[look].Width) { holding = look; break; }

            Assert.AreEqual(holding < 0 ? expected.Count - 1 : holding, index.IndexAt(offset), $"{when}: which holds {offset}");
        }

        var walked = index.From(0).ToList();
        CollectionAssert.AreEqual(starts, walked.Select(placed => placed.Start).ToList(), $"{when}: walked in order");
    }
}
