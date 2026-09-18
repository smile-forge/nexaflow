using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Plot;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Plot;

/// <summary>
/// The tree a <c>scatter</c>, <c>bubble</c>, <c>heatmap</c> or <c>density2d</c> block is read into: what
/// was written prints back as it was written, whatever it was, and every line is a setting, a comment, a
/// row of the table or held with the reason.
///
/// <para>
/// The blocks include what nobody means to write — prose, a colon with no key, a quote never closed —
/// because a block is read on every keystroke, and most of what it is handed is half-written.
/// </para>
/// </summary>
[TestClass]
[CoversNode("correlation-plots-block-syntax")]
public class PlotParserTests
{
    private static readonly (string What, string Source)[] Blocks =
    [
        ("a bare table", "1.2  3.4\n2.5  5.1"),
        ("a header and rows", "weight  mpg\n3504    18.0\n2372    24.0"),
        ("settings above a table", "x: weight\ny: mpg\n\nweight  mpg\n3504  18.0"),
        ("commas", "gdp,life,pop\n1280,52.9,31.9e6"),
        ("commas with space round them", "gdp , life , pop\n1280 , 52.9 , 3e6"),
        ("a quoted cell", "region  count\n\"North East\"  12"),
        ("an empty quoted cell", "\"\"  1"),
        ("a matrix", "      mpg   hp\nmpg   1.00  -0.78\nhp   -0.78   1.00"),
        ("the data keyword", "x: a\ndata\nsize: 4\n1 2"),
        ("a setting name below the table", "1 2\nsize: 4"),
        ("a mistyped setting", "widht: 400\n1 2"),
        ("a setting with nothing after its colon", "x:\n1 2"),
        ("space round the colon", "x   :   weight\n1 2"),
        ("a colon in a value", "title: mpg: the whole story\n1 2"),
        ("windows line endings", "x: a\r\n1 2\r\n"),
        ("comments and blank lines", "# a note\n\nx: weight\n\n# the table\n1 2"),
        ("tabs", "\tx:\tweight\t\n1\t2\n"),
        ("signed and exponent numbers", "-1.5  3e-4\n+2  .5"),
        ("prose", "just some prose"),
        ("a colon with no key", ": 40"),
        ("a quote never closed", "a  \"north  1\n2 3"),
        ("a lone quote", "\"\n1 2"),
        ("a carriage return on its own", "x: a\r1 2"),
        ("nothing at all", ""),
        ("only space", "  \n \n"),
    ];

    // ── The two invariants ──────────────────────────────────────────────────

    [TestMethod]
    public void EveryBlockReadsBackAsItWasWritten()
    {
        foreach (var (what, source) in Blocks)
            Assert.AreEqual(source, PlotParser.Parse(source).Print(), what);
    }

    [TestMethod]
    public void EveryPrefixOfEveryBlockReadsBackToo()
    {
        foreach (var (what, source) in Blocks)
            for (var length = 0; length <= source.Length; length++)
            {
                var typed = source[..length];
                Assert.AreEqual(typed, PlotParser.Parse(typed).Print(),
                                $"{what}: after {length} character(s)");
            }
    }

    [TestMethod]
    public void TheParserOnlyEverCopies()
    {
        foreach (var (what, source) in Blocks)
            foreach (var place in PlotParser.Parse(source).Placed())
            {
                if (!place.Node.IsLeaf) continue;

                Assert.IsTrue(place.End <= source.Length,
                    $"{what}: {place.Node.Kind} claims {place.Start}+{place.Node.Width} of {source.Length}");

                Assert.AreEqual(source.Substring(place.Start, place.Node.Width), place.Node.Text,
                    $"{what}: {place.Node.Kind} at {place.Start} is not what the source says");
            }
    }

    /// <summary>
    /// Every character a reader can reach for, typed at every position of every block. None of them may
    /// stop a line reading — which is the whole of what holds the block together while it is edited.
    /// </summary>
    [TestMethod]
    public void AnythingTypedAnywhereStillReads()
    {
        string[] typed = ["\"", ",", ":", "#", " ", "\t", "\n", "-", ".", "e", "1"];

        foreach (var (what, source) in Blocks)
            foreach (var character in typed)
                for (var at = 0; at <= source.Length; at++)
                {
                    var written = source[..at] + character + source[at..];

                    Assert.AreEqual(written, PlotParser.Parse(written).Print(),
                                    $"{what}: {character} typed at {at}");
                }
    }

    // ── The shape of a block ────────────────────────────────────────────────

    [TestMethod]
    public void EveryLineIsALineOfTheBlock()
    {
        var tree = PlotParser.Parse("x: weight\n\n# a note\n1 2");

        Assert.AreEqual(PlotKinds.Block, tree.Kind);
        Assert.AreEqual(4, tree.Children.Count);
        Assert.IsTrue(tree.Children.All(line => line.Kind == PlotKinds.Line));
    }

    [TestMethod]
    public void AnEmptyBlockIsNoLinesAtAll()
    {
        var tree = PlotParser.Parse("");

        Assert.AreEqual(PlotKinds.Block, tree.Kind);
        Assert.AreEqual(0, tree.Children.Count);
    }

    // ── Settings ────────────────────────────────────────────────────────────

    [TestMethod]
    public void AKeyThatNamesASettingIsASetting()
    {
        var setting = Said("x: weight").Single();

        Assert.AreEqual(PlotKinds.Setting, setting.Kind);
        Assert.AreEqual("x", setting.Part(Roles.Name)?.Text);
        Assert.AreEqual(":", setting.Part(Roles.Separator)?.Text);
        Assert.AreEqual("weight", setting.Part(PlotRoles.Value)?.Text);
    }

    [TestMethod]
    public void TheSpaceEitherSideOfASettingBelongsToTheLine()
    {
        // So the key and the value are only what they say, and a reader who lines their settings up
        // still gets the same setting.
        var setting = Said("  x   :   weight  ").Single();

        Assert.AreEqual("x", setting.Part(Roles.Name)?.Text);
        Assert.AreEqual("weight", setting.Part(PlotRoles.Value)?.Text);
    }

    [TestMethod]
    public void ASettingWithNothingAfterItsColonHasNoValue()
    {
        var setting = Said("x:").Single();

        Assert.AreEqual(PlotKinds.Setting, setting.Kind);
        Assert.IsNull(setting.Part(PlotRoles.Value));
    }

    [TestMethod]
    public void AValueIsHeldWholeHoweverManyColonsItHas()
    {
        var setting = Said("title: mpg: the whole story").Single();

        Assert.AreEqual("mpg: the whole story", setting.Part(PlotRoles.Value)?.Text);
    }

    [TestMethod]
    public void AKeyThatNamesNoSettingIsARow()
    {
        // A mistyped key is data rather than an error, so the block keeps reading; saying what went
        // wrong with it is the reader's.
        Assert.AreEqual(PlotKinds.Row, Said("widht: 400").Single().Kind);
    }

    [TestMethod]
    public void ASettingNameBelowTheTableIsARow()
    {
        // The settings are the lines above the table and the first row closes them, so a column headed
        // size is a column — the same rule a word cloud keeps, for the same reason.
        var kinds = Lines("x: weight\n1 2\nsize: 4").ToList();

        Assert.AreEqual(PlotKinds.Setting, kinds[0]);
        Assert.AreEqual(PlotKinds.Row, kinds[1]);
        Assert.AreEqual(PlotKinds.Row, kinds[2]);
    }

    [TestMethod]
    public void CommentsAndBlankLinesDoNotCloseTheSettings()
    {
        string[] read = [PlotKinds.Setting, Kinds.Space, Kinds.Comment, PlotKinds.Setting, PlotKinds.Row];

        CollectionAssert.AreEqual(read, Lines("x: weight\n\n# the table\ny: mpg\n1 2").ToList());
    }

    [TestMethod]
    public void TheDataKeywordClosesTheSettingsOnPurpose()
    {
        // The escape hatch for a table whose first row would otherwise read as a setting.
        var kinds = Lines("x: a\ndata\nsize: 4").ToList();

        Assert.AreEqual(PlotKinds.Setting, kinds[0]);
        Assert.AreEqual(PlotKinds.Data, kinds[1]);
        Assert.AreEqual(PlotKinds.Row, kinds[2]);
    }

    [TestMethod]
    public void ADataLineBelowTheTableIsJustARow()
    {
        var kinds = Lines("1 2\ndata").ToList();

        Assert.AreEqual(PlotKinds.Row, kinds[0]);
        Assert.AreEqual(PlotKinds.Row, kinds[1]);
    }

    // ── Rows ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ARowIsItsCellsAndWhatStandsBetweenThem()
    {
        var row = Said("3504  18.0  USA").Single();

        Assert.AreEqual(PlotKinds.Row, row.Kind);
        CollectionAssert.AreEqual(new[] { "3504", "18.0", "USA" }, Cells(row));
    }

    [TestMethod]
    public void SpaceAndCommasSeparateAlike()
    {
        // A table lined up in columns and a table written with commas are the same table.
        CollectionAssert.AreEqual(Cells(Said("1280 52.9 3e6").Single()),
                                  Cells(Said("1280,52.9,3e6").Single()));

        CollectionAssert.AreEqual(Cells(Said("1280 52.9 3e6").Single()),
                                  Cells(Said("1280 , 52.9 , 3e6").Single()));
    }

    [TestMethod]
    public void AQuotedCellIsOneValueSpacesAndCommasAndAll()
    {
        CollectionAssert.AreEqual(new[] { "North East, UK", "12" },
                                  Cells(Said("\"North East, UK\"  12").Single()));
    }

    [TestMethod]
    public void TheQuotesAreHeldBesideTheCellRatherThanInIt()
    {
        // So the value is drawn, pressed and typed into without them, and the line still prints back as
        // it was written.
        var cell = Said("\"North East\"  12").Single().Children
                       .First(piece => piece.Kind == PlotKinds.Cell);

        Assert.AreEqual("\"", cell.Part(Roles.Open)?.Text);
        Assert.AreEqual("North East", cell.Part(Roles.Cell)?.Text);
        Assert.AreEqual("\"", cell.Part(Roles.Close)?.Text);
    }

    [TestMethod]
    public void AQuoteNeverClosedTakesTheRestOfTheLineAndStillPrintsBack()
    {
        var row = Said("a  \"north  1").Single();

        CollectionAssert.AreEqual(new[] { "a", "\"north  1" }, Cells(row));
        Assert.AreEqual("a  \"north  1", row.Print());
    }

    [TestMethod]
    public void AMatrixRowIsOneCellWiderThanItsHeader()
    {
        // Nothing here knows that — it is what the pipeline reads the matrix form off — but the cells
        // have to be there to be counted.
        var rows = PlotParser.Parse("      mpg   hp\nmpg   1.00  -0.78")
                             .SelfAndDescendants()
                             .Where(node => node.Kind == PlotKinds.Row)
                             .ToList();

        Assert.AreEqual(2, Cells(rows[0]).Length);
        Assert.AreEqual(3, Cells(rows[1]).Length);
    }

    // ── Reading the tree back ───────────────────────────────────────────────

    /// <summary>What each line of <paramref name="source"/> turned out to be.</summary>
    private static IEnumerable<string> Lines(string source) =>
        PlotParser.Parse(source).Children
                  .Select(line => line.Children.FirstOrDefault(piece => piece.Kind != Kinds.Space)?.Kind
                                  ?? Kinds.Space);

    /// <summary>What the lines of <paramref name="source"/> say, without the space around them.</summary>
    private static IEnumerable<ContentNode> Said(string source) =>
        PlotParser.Parse(source).Children
                  .SelectMany(line => line.Children)
                  .Where(piece => piece.Kind != Kinds.Space);

    private static string[] Cells(ContentNode row) =>
        row.Children
           .Where(piece => piece.Kind == PlotKinds.Cell)
           .Select(cell => cell.IsLeaf ? cell.Text : cell.Part(Roles.Cell)?.Text ?? string.Empty)
           .ToArray();
}
