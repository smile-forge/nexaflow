using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Music.Abc;

/// <summary>
/// What the music of a tune is written as, split as far as it goes: a length suffix into its numbers and slashes, a tuplet marker
/// into its numbers in their places, a named decoration into its bangs and its name, a quoted run into its quotes, its placement
/// and its words, and a syllable into its words and what joins or escapes them. What any of it means is the stages'.
/// </summary>
[TestClass]
[CoversNode("abc-ast")]
public class AbcPiecesTests
{
    [TestMethod]
    public void ALengthIsItsNumbersAndItsSlashes()
    {
        CollectionAssert.AreEqual(new[] { "3", "/", "2" }, Pieces(Length("A3/2")));
        CollectionAssert.AreEqual(new[] { "/", "/" }, Pieces(Length("A//")));
        CollectionAssert.AreEqual(new[] { "4" }, Pieces(Length("A4")));
        Assert.IsNull(First("A", AbcKinds.Note).Part(AbcRoles.Length), "no suffix, no length");
    }

    [TestMethod]
    public void ATupletMarkerIsItsBracketAndEachNumberInItsPlace()
    {
        var bare = First("(3::2 ABC", AbcKinds.Tuplet);
        Assert.AreEqual("3", bare.Part(AbcRoles.Tupled)?.Text);
        Assert.IsNull(bare.Part(AbcRoles.InTimeOf), "q is left out, and so is simply not there");
        Assert.AreEqual("2", bare.Part(AbcRoles.Covers)?.Text);

        var full = First("(3:2:3 ABC", AbcKinds.Tuplet);
        Assert.AreEqual("2", full.Part(AbcRoles.InTimeOf)?.Text);
        Assert.AreEqual("3", full.Part(AbcRoles.Covers)?.Text);
    }

    [TestMethod]
    public void ANamedDecorationIsItsBangsAndItsName()
    {
        var named = First("! trill ! A", AbcKinds.Decoration);
        Assert.AreEqual("trill", named.Part(Roles.Name)?.Text, "the space either side of the name is not part of it");
        Assert.AreEqual("! trill !", named.Print());

        Assert.IsTrue(First("TA", AbcKinds.Decoration).IsLeaf, "a one-character shorthand has nothing inside it to split");
    }

    [TestMethod]
    public void AQuotedRunIsItsQuotesItsPlacementAndItsWords()
    {
        var placed = First("\"^Allegro\" A", AbcKinds.Annotation);
        Assert.AreEqual("^", placed.Part(AbcRoles.Placement)?.Text);
        Assert.AreEqual("Allegro", placed.Part(Roles.Body)?.Text);

        var chord = First("\"Am7\" A", AbcKinds.Annotation);
        Assert.IsNull(chord.Part(AbcRoles.Placement), "a chord symbol places nothing");
        Assert.AreEqual("Am7", chord.Part(Roles.Body)?.Text);
    }

    [TestMethod]
    public void ASyllableHoldsWhatJoinsItsWordsAndWhatKeepsItsHyphen()
    {
        var words = AbcParser.Parse("X:1\nK:C\nABC|\nw:a~b c\\-d e\n").Children
            .Single(line => line.Kind == AbcKinds.LyricLine).Part(AbcRoles.Value)!;
        var syllables = words.Children.Where(piece => piece.Kind == AbcKinds.Syllable).ToList();

        Assert.AreEqual(3, syllables.Count);
        CollectionAssert.AreEqual(new[] { "a", "~", "b" }, Pieces(syllables[0]));
        Assert.AreEqual(AbcRoles.Joined, syllables[0].Children[1].Role);
        CollectionAssert.AreEqual(new[] { "c", "\\", "-d" }, Pieces(syllables[1]));
        Assert.AreEqual(AbcRoles.Escape, syllables[1].Children[1].Role);
        Assert.IsTrue(syllables[2].IsLeaf, "a syllable with neither is the one leaf of its words");
    }

    [TestMethod]
    public void EveryPiecePrintsAsItWasWritten()
    {
        foreach (var music in new[]
                 {
                     "A3/2 B// c/ d4", "(3::2 ABC (3:2:3 ABC (3: AB (5", "! trill ! A !! B !fermata! c", "\"^x\" A \"\" B \"^\" c \"Am7\" d",
                     "TA .B ~c", "z3/2 x// Z2 y4",
                 })
        {
            var abc = $"X:1\nK:C\n{music}|\nw:a~~b \\- c\\- d~\n";
            Assert.AreEqual(abc, AbcParser.Parse(abc).Print(), music);
        }
    }

    private static ContentNode Length(string music) => First(music, AbcKinds.Note).Part(AbcRoles.Length)!;

    private static ContentNode First(string music, string kind) =>
        AbcParser.Parse($"X:1\nK:C\n{music}\n").Children.Last(line => line.Kind == AbcKinds.Line)
            .SelfAndDescendants().First(node => node.Kind == kind);

    private static string[] Pieces(ContentNode node) => [.. node.Children.Select(piece => piece.Text)];
}
