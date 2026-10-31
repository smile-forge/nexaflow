using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Treemap;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Visuals.Markdown.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Treemap;

/// <summary>
/// A treemap drawn on the shared layout tree: every item a tile as big as its share, a section's tile holding the tiles of what is in
/// it under a band with its label and total, a leaf's its label and value — and an item worth nothing yet a chip under the map.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("treemap")]
public class TreemapBuilderTests : MermaidBuilderContract
{
    private const string Products =
        "treemap-beta\n\"Products\"\n    \"Electronics\"\n        \"Phones\": 50\n        \"Computers\": 30\n        \"Accessories\": 20\n    \"Clothing\"\n        \"Men's\": 40\n        \"Women's\": 40";

    /// <summary>A treemap is a language of its own: its fence names it, though a Mermaid header naming it is read too.</summary>
    public override MermaidDiagram Diagram => MermaidDiagram.Treemap;

    /// <inheritdoc/>
    public override string Language => TreemapParser.Language;

    /// <inheritdoc/>
    protected override bool FitsTheRoom => true;

    /// <inheritdoc/>
    protected override IEnumerable<(string What, string Source)> Drawn { get; } =
    [
        ("the documented hierarchy", Products),
        ("two sections side by side", "treemap-beta\n\"Category A\"\n    \"Item A1\": 10\n    \"Item A2\": 20\n\"Category B\"\n    \"Item B1\": 15\n    \"Item B2\": 25"),
        ("styled by its classes", "treemap-beta\n\"Main\"\n    \"A\": 20\n    \"B\":::important\n        \"B1\": 10\n        \"B2\": 15\n    \"C\": 5\n\nclassDef important fill:#f96,stroke:#333,stroke-width:2px;"),
        ("money, formatted", "---\nconfig:\n  treemap:\n    valueFormat: '$0,0'\n---\ntreemap-beta\n\"Budget\"\n    \"Operations\"\n        \"Salaries\": 700000\n        \"Equipment\": 200000\n    \"Marketing\"\n        \"Advertising\": 400000"),
        ("no header", "\"A\": 1\n\"B\": 2"),
        ("values not shown", "---\nconfig:\n  treemap:\n    showValues: false\n---\ntreemap-beta\n\"A\": 1\n\"B\": 2"),
        ("still being written", "treemap-beta\n\"S\"\n    \"\": 4\n    \"B\":\n\"T\""),
        ("a label far longer than its tile", "treemap-beta\n\"A label far longer than the tile it is drawn in could ever hold\": 1\n\"B\": 99"),
        ("rules broken", "treemap-beta\n\"A\": many\n\"B: 2\n\"C\": 3:::missing"),
        ("nothing to draw", "treemap-beta\ntitle Empty"),
    ];

    [TestMethod]
    public void EveryItemIsATileStandingForItWithEverythingUnderIt() => UiThread.Run(() =>
    {
        const string source = "treemap-beta\n\"S\"\n    \"A\": 1\n    \"B\": 3\n\"C\": 4";
        var laid = Lay(source);

        Assert.AreEqual("\"S\"\n    \"A\": 1\n    \"B\": 3\n", Written(source, Pieces(laid, TreemapPiece.Section).Single().Part));
        CollectionAssert.AreEquivalent(new[] { "    \"A\": 1\n", "    \"B\": 3\n", "\"C\": 4" },
                                       Pieces(laid, TreemapPiece.Leaf).Select(leaf => Written(source, leaf.Part)).ToArray());
    });

    [TestMethod]
    public void ASectionHoldsTheTilesOfWhatIsInIt_InsideItsOwnTile() => UiThread.Run(() =>
    {
        var laid = Lay("treemap-beta\n\"S\"\n    \"A\": 1\n    \"B\": 3\n\"C\": 4");
        var section = Pieces(laid, TreemapPiece.Section).Single();
        var leaves = section.SelfAndDescendants().Where(piece => piece.Kind == TreemapPiece.Leaf).ToList();

        Assert.AreEqual(2, leaves.Count, "a section's pieces hold its leaves, as its tile holds theirs");
        Assert.IsTrue(leaves.All(leaf => Shape(section).Contains(leaf.Bounds)), "and they are drawn inside it");
        Assert.AreEqual(Shape(section).Top, section.Bounds.Top, 0.5, "so choosing it washes the whole tile, and nothing more");
        Assert.AreEqual(Shape(section).Bottom, section.Bounds.Bottom, 0.5);
    });

    [TestMethod]
    public void ATileIsAsBigAsItsShare() => UiThread.Run(() =>
    {
        var leaves = Pieces(Lay("treemap-beta\n\"A\": 1\n\"B\": 3"), TreemapPiece.Leaf);
        var area = leaves.ToDictionary(leaf => leaf.SelfAndDescendants().First(piece => piece.Kind == TreemapPiece.Label).Words!.Glyphs.Text,
                                       leaf => Shape(leaf).Width * Shape(leaf).Height);

        Assert.AreEqual(3, area["B"] / area["A"], 0.35, "three times the value, about three times the tile — less the air round each");
    });

    [TestMethod]
    public void ALeafsValueIsSetAsItsFormatSays_AndASectionsTotalIsWorkedOut() => UiThread.Run(() =>
    {
        var laid = Lay("---\nconfig:\n  treemap:\n    valueFormat: '$0,0'\n---\ntreemap-beta\n\"Budget\"\n    \"Salaries\": 700000\n    \"Equipment\": 200000");

        var values = Pieces(laid, TreemapPiece.Value).Select(value => value.Words!.Glyphs.Text).ToArray();
        CollectionAssert.AreEquivalent(new[] { "$700,000", "$200,000" }, values);
        Assert.IsTrue(Pieces(laid, TreemapPiece.Value).All(value => value.Words!.Writes && !value.Words.Maps),
                      "a value drawn other than as written is pressed to be shown as written and typed into");

        var total = Pieces(laid, TreemapPiece.Total).Single();
        Assert.AreEqual("$900,000", total.Words!.Glyphs.Text);
        Assert.IsFalse(total.Words.Writes, "a total follows from what is in the section, so it is never written in");
    });

    [TestMethod]
    public void AnItemWorthNothingIsAChipUnderTheMap_StillThereToWriteIn() => UiThread.Run(() =>
    {
        const string source = "treemap-beta\n\"A\": 5\n\"Empty section\"\n\"B\": 0";
        var laid = Lay(source, writing: true);

        var chips = Pieces(laid, TreemapPiece.Empty).Single().Children.Where(piece => piece.Kind is TreemapPiece.Leaf or TreemapPiece.Section).ToList();
        CollectionAssert.AreEqual(new[] { "\"Empty section\"\n", "\"B\": 0" }, chips.Select(chip => Written(source, chip.Part)).ToArray());

        var map = Pieces(laid, TreemapPiece.Map).Single();
        Assert.IsTrue(chips.All(chip => chip.Bounds.Top >= map.Bounds.Bottom), "under the map, where it has no share");
    });

    private static Nexaflow.Markdown.Layout.LayoutRect Shape(LayoutPiece item) => item.Children.First(piece => piece.Kind == MermaidPiece.Shape).Bounds;

    [TestMethod]
    public void ALeafIsInItsSectionsColour_AndSectionsAreHandedColoursLargestFirst_SoOrderWrittenChangesNone() => UiThread.Run(() =>
    {
        const string written = "treemap-beta\n\"Small\"\n    \"a\": 1\n    \"b\": 2\n\"Large\"\n    \"c\": 30\n    \"d\": 40";
        const string reordered = "treemap-beta\n\"Large\"\n    \"d\": 40\n    \"c\": 30\n\"Small\"\n    \"b\": 2\n    \"a\": 1";

        var colours = Colours(written);

        Assert.AreEqual(colours["a"], colours["b"], "the leaves of a section are drawn in its colour");
        Assert.AreEqual(colours["c"], colours["d"]);
        Assert.AreNotEqual(colours["a"], colours["c"], "each section in a colour of its own");
        CollectionAssert.AreEquivalent(colours.ToList(), Colours(reordered).ToList(), "written in another order, every item keeps its colour");

        Dictionary<string, System.Windows.Media.Color?> Colours(string source) =>
            Pieces(Lay(source), TreemapPiece.Leaf).Concat(Pieces(Lay(source), TreemapPiece.Section))
                .ToDictionary(piece => Written(source, piece.Part).Split('\n')[0].Split(':')[0].Trim().Trim('"'), Fill);
    });
}
