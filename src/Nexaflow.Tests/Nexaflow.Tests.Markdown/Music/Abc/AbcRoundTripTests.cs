using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Music.Abc;

/// <summary>
/// The promise the whole tree rests on: what was read prints back as it was written.
///
/// <para>
/// Both halves are here, and the second is the one that holds the first up. <c>Print(Parse(s)) == s</c>
/// says nothing was lost — but a parser that returned the input as one undigested lump would pass it, and
/// so would one that quietly repaired what it read. <see cref="TheParserOnlyEverCopies"/> is the stricter
/// claim: every leaf's characters are found in the source at the offset the tree puts them at, so there
/// is nowhere for an invented character to hide.
/// </para>
/// <para>
/// The prefixes are the point of the exercise rather than a flourish. Every prefix of a tune is something
/// somebody typed on the way to typing the tune, so a parser that handles the finished article and not
/// its prefixes is a parser an editor cannot use — and half the prefixes here have an unclosed bracket,
/// quotation or brace, which is what makes them worth asking about.
/// </para>
/// </summary>
[TestClass]
[CoversNode("abc-ast-roundtrip")]
public class AbcRoundTripTests
{
    [TestMethod]
    public void EveryConstructReadsBackAsItWasWritten()
    {
        foreach (var (what, abc) in AbcConstructs.Everything)
            Assert.AreEqual(abc, AbcParser.Parse(abc).Print(), what);
    }

    [TestMethod]
    public void EveryLineOfEveryConstructReadsBackOnItsOwn()
    {
        // A line on its own is what the editor holds while somebody is writing the line above it, and it
        // is also what a stage sees when it is handed one line of a tune to re-read.
        foreach (var (what, abc) in AbcConstructs.EverythingAndItsLines())
            Assert.AreEqual(abc, AbcParser.Parse(abc).Print(), what);
    }

    [TestMethod]
    public void EveryPrefixOfEveryConstructReadsBackToo()
    {
        foreach (var (what, abc) in AbcConstructs.Everything)
            for (var length = 0; length <= abc.Length; length++)
            {
                var typed = abc[..length];
                Assert.AreEqual(typed, AbcParser.Parse(typed).Print(), $"{what}: after {length} character(s)");
            }
    }

    [TestMethod]
    public void AndEverySuffix()
    {
        // What arrives when somebody pastes the back half of a tune — a run that starts inside a chord,
        // or with the closing bracket of something that was never opened here.
        foreach (var (what, abc) in AbcConstructs.Everything)
            for (var start = 0; start <= abc.Length; start++)
            {
                var pasted = abc[start..];
                Assert.AreEqual(pasted, AbcParser.Parse(pasted).Print(), $"{what}: from character {start}");
            }
    }

    [TestMethod]
    public void TheParserOnlyEverCopies()
    {
        foreach (var (what, abc) in AbcConstructs.Everything)
            foreach (var place in AbcParser.Parse(abc).Placed())
            {
                if (!place.Node.IsLeaf) continue;

                Assert.IsTrue(place.End <= abc.Length,
                    $"{what}: {place.Node.Kind} claims {place.Start}+{place.Node.Width} of {abc.Length}");

                Assert.AreEqual(abc.Substring(place.Start, place.Node.Width), place.Node.Text,
                    $"{what}: {place.Node.Kind} at {place.Start} is not what the source says");
            }
    }

    [TestMethod]
    public void NothingReadableIsHeldAsUnreadable()
    {
        // The other half of holding what cannot be read: a parser that held *everything* verbatim would
        // pass every test above and have read nothing. So the tunes that are meant to be readable must
        // come back with no verbatim pieces in them at all.
        foreach (var (what, abc) in AbcConstructs.Everything)
        {
            if (what.StartsWith("what nobody can read")) continue;

            var held = AbcParser.Parse(abc).SelfAndDescendants()
                .Where(node => node.Trouble is not null)
                .Select(node => $"{node.Text} ({node.Trouble})")
                .ToList();

            Assert.AreEqual(0, held.Count, $"{what}: held as unreadable — {string.Join(", ", held)}");
        }
    }

    [TestMethod]
    public void AndTheTuneThatCannotBeReadIsHeldRatherThanLost()
    {
        var (_, abc) = AbcConstructs.Everything.Single(e => e.What.StartsWith("what nobody can read"));

        var tree = AbcParser.Parse(abc);

        Assert.AreEqual(abc, tree.Print(), "it still prints back");
        Assert.IsTrue(tree.SelfAndDescendants().Any(node => node.Trouble is not null),
            "and it says what it could not make sense of");
    }
}
