using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.WordCloud;
using Nexaflow.Markdown.WordCloud.Stages;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.WordCloud;

/// <summary>
/// What a block's stages say it comes to (<see cref="ResolveCloud"/>, <see cref="ResolveWords"/>): its settings, its words and
/// their sizes, and the line between a fault that stops it being a cloud at all and a word that is merely not in one yet — each
/// marked where it is wrong.
/// </summary>
[TestClass]
[CoversNode("wordcloud-block-syntax")]
public class WordCloudStagesTests
{
    private static ContentNode Staged(string source) => new ResolveWords().Run(new ResolveCloud().Run(WordCloudParser.Parse(source)));

    private static WordCloudBlockNode Read(string source)
    {
        var tree = Staged(source);
        Assert.IsInstanceOfType(tree, typeof(WordCloudBlockNode), string.Join("; ", Troubles(tree)));
        return (WordCloudBlockNode)tree;
    }

    /// <summary>The words the cloud is made of, in the order they are packed.</summary>
    private static WordCloudWordNode[] Drawn(ContentNode tree) =>
        [.. tree.SelfAndDescendants().OfType<WordCloudWordNode>().OrderBy(word => word.Rank)];

    private static string[] Texts(ContentNode tree) => [.. Drawn(tree).Select(word => word.Text)];

    private static string[] Troubles(ContentNode tree) =>
        [.. tree.SelfAndDescendants().Where(node => node.Trouble is not null && !node.IsDerived).Select(node => node.Trouble!)];

    /// <summary>What is wrong with the piece written as <paramref name="written"/>, or null.</summary>
    private static string? SaidAgainst(ContentNode tree, string written) =>
        tree.SelfAndDescendants().FirstOrDefault(node => node.Trouble is not null && node.Print() == written)?.Trouble;

    /// <summary>What stops a block being a cloud, said against the setting's value that was wrong.</summary>
    private static string Refused(string source, string at)
    {
        var tree = Staged(source);
        Assert.IsNotInstanceOfType(tree, typeof(WordCloudBlockNode), "this should not have read as a cloud");
        return SaidAgainst(tree, at) ?? throw new AssertFailedException($"{source}: nothing said against `{at}` — {string.Join("; ", Troubles(tree))}");
    }

    [TestMethod]
    public void TheStagesPrintAsWhatWasWritten()
    {
        foreach (var source in new[] { "WPF: 40\nXAML: lots\n", "shape: blob\nWPF: 40", "", "colour: #f00\n\"shape\": 4\r\n# note" })
            Assert.AreEqual(source, Staged(source).Print());
    }

    // ── The words ──────────────────────────────────────────────────────────

    [TestMethod]
    public void EveryPairIsAWordAndWhatItCountsFor()
    {
        var words = Drawn(Read("WPF: 40\nXAML: 25.5"));

        CollectionAssert.AreEqual(new[] { "WPF", "XAML" }, words.Select(word => word.Text).ToArray());
        CollectionAssert.AreEqual(new[] { 40, 25.5 }, words.Select(word => word.Weight).ToArray());
    }

    [TestMethod]
    public void TheHeaviestWordIsPackedFirstAndTiesKeepTheirOrder()
    {
        // The packing is greedy and never goes back, so the order is what decides which word gets the middle.
        CollectionAssert.AreEqual(new[] { "big", "middling", "small", "also" }, Texts(Read("small: 1\nbig: 90\nalso: 1\nmiddling: 40")));
    }

    [TestMethod]
    public void AWeightThatWillNotReadKeepsItsLineAndLosesItsPlace()
    {
        var tree = Read("WPF: 40\nXAML: lots");

        StringAssert.Contains(SaidAgainst(tree, "lots"), "not a weight", "said against the weight");
        CollectionAssert.AreEqual(new[] { "WPF" }, Texts(tree), "and the rest are still a cloud");
    }

    [TestMethod]
    public void AWordWorthNothingIsNotAWordOfTheCloud()
    {
        foreach (var weight in new[] { "0", "-4" })
        {
            var tree = Read($"WPF: {weight}");

            Assert.IsNotNull(SaidAgainst(tree, weight), weight);
            Assert.AreEqual(0, Drawn(tree).Length, weight);
        }
    }

    [TestMethod]
    public void AWordWithNoWeightSaysSoAgainstTheWord()
    {
        var tree = Read("WPF:");

        StringAssert.Contains(SaidAgainst(tree, "WPF"), "no weight");
        Assert.AreEqual(0, Drawn(tree).Length);
    }

    [TestMethod]
    public void AWordInQuotesIsCountedAndStillLeavesTheSettingSet()
    {
        var tree = Read("shape: star\n\"shape\": 40");

        Assert.AreEqual(WordCloudShape.Star, tree.Settings.Shape);
        CollectionAssert.AreEqual(new[] { "shape" }, Texts(tree), "the word, without its quotes");
    }

    [TestMethod]
    public void AWordKnowsWhereItWasWritten()
    {
        // What makes the picture editable: the run drawn for a word carries the part, so a caret in the word lands on the
        // characters the word was written with.
        const string source = "WPF: 40\n\"XAML\": 25";
        var xaml = ContentPart.Of(Read(source)).SelfAndDescendants().Single(part => part.Node is WordCloudWordNode { Text: "XAML" });

        Assert.AreEqual("XAML", source.Substring(xaml.Start, xaml.Length));
    }

    [TestMethod]
    public void AnEmptyBlockSaysWhatItNeeds()
    {
        CollectionAssert.AreEqual(new[] { ResolveWords.Empty }, Troubles(Staged("")));
        CollectionAssert.AreEqual(new[] { ResolveWords.Empty }, Troubles(Staged("shape: star\n# nothing yet")));
    }

    // ── The settings ───────────────────────────────────────────────────────

    [TestMethod]
    public void ABlockWithNoSettingsTakesTheDefaults()
    {
        var tree = Read("WPF: 40");

        Assert.AreEqual(WordCloudSettings.Default.Shape, tree.Settings.Shape);
        Assert.AreEqual(WordCloudSettings.Default.MinSize, tree.Settings.MinSize);
        Assert.AreEqual(WordCloudSettings.Default.MaxSize, tree.Settings.MaxSize);
        Assert.AreEqual(WordCloudColouring.Themed, tree.Colours.Colouring);
        Assert.IsNull(tree.Colours.Background);
    }

    [TestMethod]
    public void EverySettingIsRead()
    {
        var tree = Read("""
                        width: 500
                        height: 300
                        shape: pentagon
                        ellipticity: 1
                        font: Georgia
                        bold: no
                        min-size: 10
                        max-size: 60
                        scale: log
                        gridSize: 8
                        gap: 3
                        rotate: 0.5
                        minRotation: -45
                        maxRotation: 45
                        rotationSteps: 3
                        color: #ff0000 #00ff00
                        background: #101010
                        seed: 7
                        shuffle: false
                        fit: false
                        WPF: 40
                        """);

        var it = tree.Settings;

        Assert.AreEqual(500, it.Width);
        Assert.AreEqual(300, it.Height);
        Assert.AreEqual(WordCloudShape.Pentagon, it.Shape);
        Assert.AreEqual(1, it.Ellipticity);
        Assert.AreEqual("Georgia", it.Font);
        Assert.IsFalse(it.Bold);
        Assert.AreEqual(10, it.MinSize);
        Assert.AreEqual(60, it.MaxSize);
        Assert.AreEqual(WordCloudScale.Logarithmic, it.Scale);
        Assert.AreEqual(8, it.GridSize);
        Assert.AreEqual(3, it.Gap);
        Assert.AreEqual(0.5, it.Rotate);
        Assert.AreEqual(-45, it.MinRotation);
        Assert.AreEqual(45, it.MaxRotation);
        Assert.AreEqual(3, it.RotationSteps);
        Assert.AreEqual(7, it.Seed);
        Assert.IsFalse(it.Shuffle);
        Assert.IsFalse(it.Fit);

        Assert.AreEqual(WordCloudColouring.Written, tree.Colours.Colouring);
        CollectionAssert.AreEqual(new[] { (byte)0xFF, (byte)0x00 }, tree.Colours.Written.Select(colour => colour.R).ToArray());
        Assert.AreEqual((byte)0x10, tree.Colours.Background!.Value.R);
    }

    [TestMethod]
    public void ColourIsSpeltEitherWay_AndTheRandomThrowsAreNamed()
    {
        Assert.AreEqual((byte)0xFF, Read("colour: #ff0000\nWPF: 4").Colours.Written.Single().R);
        Assert.AreEqual((byte)0xFF, Read("color: #ff0000\nWPF: 4").Colours.Written.Single().R);
        Assert.AreEqual(WordCloudColouring.RandomDark, Read("color: random-dark\nWPF: 4").Colours.Colouring);
        Assert.AreEqual(WordCloudColouring.RandomLight, Read("color: Random-Light\nWPF: 4").Colours.Colouring);
    }

    [TestMethod]
    public void AMistypedSettingIsAWordThatSaysWhyItIsNotOne()
    {
        // A key the grammar does not know is a word, so a mistyped setting is a word with a weight that is not a number — and the
        // reason it gives has to name both readings, because nothing else can tell which one was meant.
        var tree = Read("colorScheme: warm\nWPF: 40");

        StringAssert.Contains(SaidAgainst(tree, "warm"), "is not a setting");
        CollectionAssert.AreEqual(new[] { "WPF" }, Texts(tree));
    }

    [TestMethod]
    public void ASettingWrittenBelowTheWordsSaysWhereItShouldHaveGone()
    {
        // It is read as a word, which is the rule; but "star is not a weight" would send a reader looking for the wrong mistake, so
        // the reason names the one they actually made.
        StringAssert.Contains(SaidAgainst(Read("design: 90\nshape: star"), "star"), "above the words");
    }

    [TestMethod]
    public void AValueThatIsNotWhatItsSettingTakesStopsTheBlock_AndIsMarkedWhereItIsWritten()
    {
        StringAssert.Contains(Refused("width: wide\nWPF: 40", "wide"), "not a number");
        StringAssert.Contains(Refused("shape: blob\nWPF: 40", "blob"), "not a shape");
        StringAssert.Contains(Refused("scale: cubic\nWPF: 40", "cubic"), "not a scale");
        StringAssert.Contains(Refused("bold: sometimes\nWPF: 40", "sometimes"), "not true or false");
        StringAssert.Contains(Refused("gridSize: 900\nWPF: 40", "900"), "is outside");
        StringAssert.Contains(Refused("minSize: 40\nmaxSize: 10\nWPF: 40", "40"), "larger than");
        StringAssert.Contains(Refused("colour: blue\nWPF: 40", "blue"), "not a colour");
        StringAssert.Contains(Refused("background: grey\nWPF: 40", "grey"), "not a hex colour");
    }

    [TestMethod]
    public void ALineThatIsNotAPairIsMarked_AndTheWordsAroundItAreStillWords()
    {
        var tree = Staged("WPF: 40\njust some prose");

        StringAssert.Contains(SaidAgainst(tree, "just some prose"), "not a `word: weight` line");
        CollectionAssert.AreEqual(new[] { "WPF" }, Texts(tree));
    }

    // ── Sizes ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void TheLightestWordIsSetAtTheSmallestSizeAndTheHeaviestAtTheLargest()
    {
        var words = Drawn(Read("minSize: 10\nmaxSize: 50\nbig: 100\nsmall: 1"));

        Assert.AreEqual(50, words[0].Size, 1e-9);
        Assert.AreEqual(10, words[1].Size, 1e-9);
    }

    [TestMethod]
    public void WordsThatAllWeighTheSameAreAllSetAtTheLargestSize()
    {
        foreach (var word in Drawn(Read("minSize: 10\nmaxSize: 50\na: 3\nb: 3\nc: 3"))) Assert.AreEqual(50, word.Size, 1e-9);
    }

    [TestMethod]
    public void TheScaleDecidesWhatTheMiddleOfTheRangeLooksLike()
    {
        const string words = "big: 100\nmiddling: 25\nsmall: 1";

        // Linear puts 25 a quarter of the way up; a root scale, which spreads the light words apart, puts it halfway; a log scale
        // higher still.
        double Middling(string scale) => Drawn(Read($"minSize: 4\nmaxSize: 100\nscale: {scale}\n{words}"))[1].Size;

        var (linear, root, log) = (Middling("linear"), Middling("sqrt"), Middling("log"));

        Assert.IsTrue(linear < root, $"linear {linear} should sit below root {root}");
        Assert.IsTrue(root < log, $"root {root} should sit below log {log}");
    }
}
