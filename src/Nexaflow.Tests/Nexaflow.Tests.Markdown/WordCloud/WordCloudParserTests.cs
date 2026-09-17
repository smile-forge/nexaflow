using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.WordCloud;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.WordCloud;

/// <summary>
/// The tree a <c>wordcloud</c> block is read into: what was written prints back as it was written, whatever
/// it was, and every line is either a word with its weight or a setting with its value.
///
/// <para>
/// The blocks include what nobody means to write — prose, a colon with no key, a word with no number — because
/// a block is read on every keystroke, and most of what it is handed is half-written.
/// </para>
/// </summary>
[TestClass]
[CoversNode("wordcloud-block-syntax")]
public class WordCloudParserTests
{
    private static readonly (string What, string Source)[] Blocks =
    [
        ("words and weights", "WPF: 40\nXAML: 25\nMVVM: 12"),
        ("a setting among the words", "shape: star\nWPF: 40\nXAML: 25"),
        ("a quoted word", "\"shape\": 40\nWPF: 25"),
        ("windows line endings", "WPF: 40\r\nXAML: 25\r\n"),
        ("comments and blank lines", "# the stack\n\nWPF: 40\n  XAML: 25  \n\n# the end"),
        ("a colon inside a quoted word", "\"9:15\": 4\nWPF: 2"),
        ("space round the colon", "WPF   :   40\nXAML:"),
        ("tabs", "\tWPF:\t40\t\n"),
        ("prose", "just some prose"),
        ("a colon with no key", ": 40"),
        ("a weight that is not a number", "WPF: lots"),
        ("a carriage return on its own", "WPF: 40\rXAML: 25"),
        ("nothing at all", ""),
        ("only space", "  \n \n"),
    ];

    [TestMethod]
    public void EveryBlockReadsBackAsItWasWritten()
    {
        foreach (var (what, source) in Blocks)
            Assert.AreEqual(source, WordCloudParser.Parse(source).Print(), what);
    }

    [TestMethod]
    public void EveryPrefixOfEveryBlockReadsBackToo()
    {
        foreach (var (what, source) in Blocks)
            for (var length = 0; length <= source.Length; length++)
            {
                var typed = source[..length];
                Assert.AreEqual(typed, WordCloudParser.Parse(typed).Print(),
                                $"{what}: after {length} character(s)");
            }
    }

    [TestMethod]
    public void TheParserOnlyEverCopies()
    {
        foreach (var (what, source) in Blocks)
            foreach (var place in WordCloudParser.Parse(source).Placed())
            {
                if (!place.Node.IsLeaf) continue;

                Assert.IsTrue(place.End <= source.Length,
                    $"{what}: {place.Node.Kind} claims {place.Start}+{place.Node.Width} of {source.Length}");

                Assert.AreEqual(source.Substring(place.Start, place.Node.Width), place.Node.Text,
                    $"{what}: {place.Node.Kind} at {place.Start} is not what the source says");
            }
    }

    [TestMethod]
    public void EveryLineIsALineOfTheBlock()
    {
        var tree = WordCloudParser.Parse("WPF: 40\n\n# a note\nprose");

        Assert.AreEqual(WordCloudKinds.Block, tree.Kind);
        Assert.AreEqual(4, tree.Children.Count);
        Assert.IsTrue(tree.Children.All(line => line.Kind == WordCloudKinds.Line));
    }

    [TestMethod]
    public void AnEntryIsItsWordItsColonAndItsWeight()
    {
        // The space either side belongs to the line, so the word and the weight are only what they say.
        var entry = Entries("  WPF :  40  ").Single();

        Assert.AreEqual("WPF", entry.Part(WordCloudRoles.Word)?.Text);
        Assert.AreEqual(":", entry.Part(Roles.Separator)?.Text);
        Assert.AreEqual("40", entry.Part(WordCloudRoles.Weight)?.Text);
    }

    [TestMethod]
    public void AKeyThatNamesASettingIsASetting()
    {
        var line = Lines("shape: star").Single(piece => piece.Kind != Kinds.Space);

        Assert.AreEqual(WordCloudKinds.Setting, line.Kind);
        Assert.AreEqual("shape", line.Part(Roles.Name)?.Text);
        Assert.AreEqual("star", line.Part(WordCloudRoles.Value)?.Text);
    }

    [TestMethod]
    public void ASettingNameBelowTheWordsIsAWord()
    {
        // Half the settings are ordinary English — shape, scale, colour, gap — and a list of words is full of
        // ordinary English. The settings are the lines above the words, and the first word closes them, so a
        // cloud of design terms stays a cloud.
        var lines = WordCloudParser.Parse("shape: star\ndesign: 90\nscale: 60\ncolour: 40")
                                   .SelfAndDescendants().ToList();

        Assert.AreEqual(1, lines.Count(node => node.Kind == WordCloudKinds.Setting));
        Assert.AreEqual(3, lines.Count(node => node.Kind == WordCloudKinds.Entry));
    }

    [TestMethod]
    public void CommentsAndBlankLinesDoNotCloseTheSettings()
    {
        var tree = WordCloudParser.Parse("shape: star\n\n# the words\n\ngap: 3\ndesign: 90");

        Assert.AreEqual(2, tree.SelfAndDescendants().Count(node => node.Kind == WordCloudKinds.Setting));
    }

    [TestMethod]
    public void AWordInQuotesIsAWordEvenWhenItNamesASetting()
    {
        // The escape hatch the grammar needs: one list of lines does two jobs, and this is how a cloud
        // counts the word `shape` without losing its shape.
        var entry = Entries("\"shape\": 40").Single();

        Assert.AreEqual(WordCloudKinds.Entry, entry.Kind);
        Assert.AreEqual("shape", Word(entry).Text);
        Assert.AreEqual("40", entry.Part(WordCloudRoles.Weight)?.Text);
    }

    [TestMethod]
    public void TheQuotesAreHeldBesideTheWordRatherThanInIt()
    {
        var word = Entries("\"shape\": 40").Single().Part(WordCloudRoles.Word)!;

        Assert.AreEqual("\"shape\"", word.Print(), "the line still prints back as it was written");
        Assert.AreEqual("shape", Word(Entries("\"shape\": 40").Single()).Text, "and the word drawn has no quotes");
    }

    [TestMethod]
    public void TheFirstColonOutsideQuotesEndsTheWord()
    {
        var entry = Entries("\"9:15\": 4").Single();

        Assert.AreEqual("9:15", Word(entry).Text);
        Assert.AreEqual("4", entry.Part(WordCloudRoles.Weight)?.Text);
    }

    [TestMethod]
    public void AnEntryWithNothingAfterItsColonHasNoWeight()
    {
        var entry = Entries("WPF:   ").Single();

        Assert.AreEqual("WPF", entry.Part(WordCloudRoles.Word)?.Text);
        Assert.IsNull(entry.Part(WordCloudRoles.Weight));
    }

    [TestMethod]
    public void ALineThatIsNotAPairIsHeldWithTheReason()
    {
        var shown = Lines("just some prose").Single(piece => piece.Kind != Kinds.Space);

        Assert.AreEqual(Kinds.Verbatim, shown.Kind);
        Assert.IsNotNull(shown.Trouble);
    }

    // ── Reading the tree ───────────────────────────────────────────────────

    private static IEnumerable<ContentNode> Lines(string source) =>
        WordCloudParser.Parse(source).Children.SelectMany(line => line.Children);

    private static IEnumerable<ContentNode> Entries(string source) =>
        WordCloudParser.Parse(source).SelfAndDescendants().Where(node => node.Kind == WordCloudKinds.Entry);

    /// <summary>The word as it is drawn — the leaf inside the quotes, where there are any.</summary>
    private static ContentNode Word(ContentNode entry)
    {
        var word = entry.Part(WordCloudRoles.Word)!;
        return word.IsLeaf ? word : word.Children.First(child => child.Kind == WordCloudKinds.Word);
    }
}
