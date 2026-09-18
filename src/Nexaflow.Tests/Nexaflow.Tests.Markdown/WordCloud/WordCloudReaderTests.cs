using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.WordCloud;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.WordCloud;

/// <summary>
/// What a block comes to: its settings, its words, and the line between a fault that stops it being a cloud
/// at all and a word that is merely not in one yet.
/// </summary>
[TestClass]
[CoversNode("wordcloud-block-syntax")]
public class WordCloudReaderTests
{
    private static WordCloudChart Read(string source)
    {
        Assert.IsTrue(Try(source, out var chart, out var error), error);
        return chart!;
    }

    private static bool Try(string source, out WordCloudChart? chart, out string? error) =>
        WordCloudReader.TryRead(ContentPart.Of(WordCloudParser.Parse(source)), out chart, out error);

    private static string Refused(string source)
    {
        Assert.IsFalse(Try(source, out _, out var error), "this should not have read as a cloud");
        Assert.IsNotNull(error);
        return error!;
    }

    // ── The words ──────────────────────────────────────────────────────────

    [TestMethod]
    public void EveryPairIsAWordAndWhatItCountsFor()
    {
        var chart = Read("WPF: 40\nXAML: 25.5");

        Assert.AreEqual(2, chart.Words.Count);
        Assert.AreEqual("WPF", chart.Words[0].Text);
        Assert.AreEqual(40, chart.Words[0].Weight);
        Assert.AreEqual(25.5, chart.Words[1].Weight);
    }

    [TestMethod]
    public void TheHeaviestWordComesFirstAndTiesKeepTheirOrder()
    {
        // The packing is greedy and never goes back, so the order is what decides which word gets the middle.
        var chart = Read("small: 1\nbig: 90\nalso: 1\nmiddling: 40");

        CollectionAssert.AreEqual(new[] { "big", "middling", "small", "also" },
                                  chart.Drawable.Select(word => word.Text).ToArray());
    }

    [TestMethod]
    public void AWeightThatWillNotReadKeepsItsLineAndLosesItsPlace()
    {
        var chart = Read("WPF: 40\nXAML: lots");

        Assert.AreEqual(2, chart.Words.Count, "the word is still in the block");
        Assert.IsNotNull(chart.Words[1].Trouble, "and it says why it is not in the picture");
        CollectionAssert.AreEqual(new[] { "WPF" }, chart.Drawable.Select(word => word.Text).ToArray());
    }

    [TestMethod]
    public void AWordWorthNothingIsNotAWordOfTheCloud()
    {
        foreach (var weight in new[] { "0", "-4" })
        {
            var chart = Read($"WPF: {weight}");

            Assert.IsNotNull(chart.Words[0].Trouble, weight);
            Assert.AreEqual(0, chart.Drawable.Count, weight);
        }
    }

    [TestMethod]
    public void AWordWithNoWeightSaysSo()
    {
        var chart = Read("WPF:");

        Assert.IsNotNull(chart.Words[0].Trouble);
        Assert.AreEqual(0, chart.Drawable.Count);
    }

    [TestMethod]
    public void AWordInQuotesIsCountedAndStillLeavesTheSettingSet()
    {
        var chart = Read("shape: star\n\"shape\": 40");

        Assert.AreEqual(WordCloudShape.Star, chart.Settings.Shape);
        CollectionAssert.AreEqual(new[] { "shape" }, chart.Drawable.Select(word => word.Text).ToArray());
    }

    [TestMethod]
    public void AWordKnowsWhereItWasWritten()
    {
        // What makes the picture editable: the run drawn for a word carries the part, so a caret in the
        // word lands on the characters the word was written with.
        const string source = "WPF: 40\nXAML: 25";
        var chart = Read(source);

        var xaml = chart.Words[1].Word;
        Assert.AreEqual("XAML", source.Substring(xaml.Start, xaml.Length));
    }

    // ── The settings ───────────────────────────────────────────────────────

    [TestMethod]
    public void ABlockWithNoSettingsTakesTheDefaults()
    {
        var chart = Read("WPF: 40");

        Assert.AreEqual(WordCloudSettings.Default.Shape, chart.Settings.Shape);
        Assert.AreEqual(WordCloudSettings.Default.MinSize, chart.Settings.MinSize);
        Assert.AreEqual(WordCloudSettings.Default.MaxSize, chart.Settings.MaxSize);
    }

    [TestMethod]
    public void EverySettingIsRead()
    {
        var chart = Read("""
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

        var it = chart.Settings;

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
        Assert.AreEqual("#ff0000 #00ff00", it.Colour);
        Assert.AreEqual("#101010", it.Background);
        Assert.AreEqual(7, it.Seed);
        Assert.IsFalse(it.Shuffle);
        Assert.IsFalse(it.Fit);
    }

    [TestMethod]
    public void ColourIsSpeltEitherWay()
    {
        Assert.AreEqual("#ff0000", Read("colour: #ff0000\nWPF: 4").Settings.Colour);
        Assert.AreEqual("#ff0000", Read("color: #ff0000\nWPF: 4").Settings.Colour);
    }

    [TestMethod]
    public void AMistypedSettingIsAWordThatSaysWhyItIsNotOne()
    {
        // A key the grammar does not know is a word, so a mistyped setting is a word with a weight that is
        // not a number — and the reason it gives has to name both readings, because nothing else can tell
        // which one was meant.
        var chart = Read("colorScheme: warm\nWPF: 40");

        Assert.AreEqual("colorScheme", chart.Words[0].Text);
        StringAssert.Contains(chart.Words[0].Trouble, "is not a setting");
        CollectionAssert.AreEqual(new[] { "WPF" }, chart.Drawable.Select(word => word.Text).ToArray());
    }

    [TestMethod]
    public void ASettingWrittenBelowTheWordsSaysWhereItShouldHaveGone()
    {
        // It is read as a word, which is the rule; but "star is not a weight" would send a reader looking for
        // the wrong mistake, so the reason names the one they actually made.
        var chart = Read("design: 90\nshape: star");

        StringAssert.Contains(chart.Words[1].Trouble, "above the words");
    }

    [TestMethod]
    public void AValueThatIsNotWhatItsSettingTakesStopsTheBlock()
    {
        StringAssert.Contains(Refused("width: wide\nWPF: 40"), "not a number");
        StringAssert.Contains(Refused("shape: blob\nWPF: 40"), "not a shape");
        StringAssert.Contains(Refused("scale: cubic\nWPF: 40"), "not a scale");
        StringAssert.Contains(Refused("bold: sometimes\nWPF: 40"), "not true or false");
        StringAssert.Contains(Refused("gridSize: 900\nWPF: 40"), "is outside");
        StringAssert.Contains(Refused("minSize: 40\nmaxSize: 10\nWPF: 40"), "larger than");
    }

    [TestMethod]
    public void ALineThatIsNotAPairStopsTheBlock()
    {
        StringAssert.Contains(Refused("WPF: 40\njust some prose"), "not a `word: weight` line");
    }

    // ── Sizes ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void TheLightestWordIsSetAtTheSmallestSizeAndTheHeaviestAtTheLargest()
    {
        var chart = Read("minSize: 10\nmaxSize: 50\nbig: 100\nsmall: 1");

        Assert.AreEqual(50, chart.SizeOf(100), 1e-9);
        Assert.AreEqual(10, chart.SizeOf(1), 1e-9);
    }

    [TestMethod]
    public void WordsThatAllWeighTheSameAreAllSetAtTheLargestSize()
    {
        var chart = Read("minSize: 10\nmaxSize: 50\na: 3\nb: 3\nc: 3");

        foreach (var word in chart.Drawable) Assert.AreEqual(50, chart.SizeOf(word.Weight), 1e-9);
    }

    [TestMethod]
    public void TheScaleDecidesWhatTheMiddleOfTheRangeLooksLike()
    {
        const string words = "big: 100\nmiddling: 25\nsmall: 1";

        // Linear puts 25 a quarter of the way up; a root scale, which spreads the light words apart, puts
        // it halfway; a log scale higher still.
        var linear = Read($"minSize: 4\nmaxSize: 100\nscale: linear\n{words}").SizeOf(25);
        var root = Read($"minSize: 4\nmaxSize: 100\nscale: sqrt\n{words}").SizeOf(25);
        var log = Read($"minSize: 4\nmaxSize: 100\nscale: log\n{words}").SizeOf(25);

        Assert.IsTrue(linear < root, $"linear {linear} should sit below root {root}");
        Assert.IsTrue(root < log, $"root {root} should sit below log {log}");
    }
}
