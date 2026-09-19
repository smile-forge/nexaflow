using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A box of compartments — what a class, a requirement and an entity are all drawn as: rows a row deep in bands one under
/// another, a rule between each band and the next, and columns that either line up down the band or follow one another.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("mermaid-diagram-kit")]
public class DiagramBoxTests
{
    private const double Tall = 18;
    private const double Pad = 12;
    private const double Air = 5;
    private const double Gap = 6;

    [TestMethod]
    public void EachBandIsAsDeepAsItsRowsWithAirAboveAndBelowThem() => UiThread.Run(() =>
    {
        var box = Measure([Band("A"), Band("one", "two")]);

        CollectionAssert.AreEqual(new[] { Tall + (Air * 2), (Tall * 2) + (Air * 2) }, box.Depths.ToArray());
        Assert.AreEqual(box.Depths.Sum(), box.Size.Height, "and the box is as deep as its bands together");
    });

    [TestMethod]
    public void ABandWrittenButLeftEmptyStillTakesItsAir() => UiThread.Run(() =>
    {
        var box = Measure([Band("A"), new DiagramCompartment([])]);

        CollectionAssert.AreEqual(new[] { Tall + (Air * 2), Air * 2 }, box.Depths.ToArray());
    });

    [TestMethod]
    public void TheBoxIsAsWideAsItsWidestRowAndNeverLessThanTheLeastItMayBe() => UiThread.Run(() =>
    {
        var wide = Measure([Band("A very long row indeed")]);
        var thin = Measure([Band("A")]);

        Assert.AreEqual(DiagramShapesTests.Words("A very long row indeed").Width + (Pad * 2), wide.Size.Width, 0.5);
        Assert.AreEqual(96, thin.Size.Width, "what little is written in it does not make it smaller than the least");
    });

    [TestMethod]
    public void ARuleRunsBetweenEachBandAndTheNextAndNoneUnderTheLast() => UiThread.Run(() =>
    {
        var bounds = new Rect(10, 20, 200, 100);
        var box = Measure([Band("A"), Band("one"), Band("two")]);

        CollectionAssert.AreEqual(new[] { 20 + box.Depths[0], 20 + box.Depths[0] + box.Depths[1] },
                                  box.Rules(bounds).ToArray());
        Assert.AreEqual(0, Measure([Band("A")]).Rules(bounds).Count(), "one band on its own is divided from nothing");
    });

    [TestMethod]
    public void ACentredRowIsSetAcrossTheMiddleAndALeftOneAPadIn() => UiThread.Run(() =>
    {
        var bounds = new Rect(10, 20, 200, 100);
        var box = Measure([new DiagramCompartment([Row("Name")]) { Centred = true }, Band("a field")]);

        var placed = box.Placed(bounds).ToList();
        var name = placed[0].Set[0];
        var field = placed[1].Set[0];

        Assert.AreEqual(10 + ((200 - name.Words.Width) / 2), name.Where.X, 0.5);
        Assert.AreEqual(10 + Pad, field.Where.X, 0.5);
        Assert.IsTrue(field.Where.Y > name.Where.Y, "and the band under it is drawn under it");
    });

    [TestMethod]
    public void ColumnsLineUpDownAnAlignedBandAndFollowOneAnotherWhereTheyDoNot() => UiThread.Run(() =>
    {
        var bounds = new Rect(0, 0, 400, 200);

        var aligned = new DiagramCompartment([Row("string", "name"), Row("int", "a much longer one")]) { Aligned = true };
        var set = Measure([aligned]).Placed(bounds).Select(placed => placed.Set[1].Where.X).ToList();
        Assert.AreEqual(set[0], set[1], 0.5, "the second column starts in the same place down the band");

        var flowing = new DiagramCompartment([Row("string", "name"), Row("int", "a much longer one")]);
        var follows = Measure([flowing]).Placed(bounds).Select(placed => placed.Set[1].Where.X).ToList();
        Assert.IsTrue(follows[0] > follows[1], "and where they do not, each follows what is written before it on its row");
    });

    [TestMethod]
    public void AColumnNothingWritesInTakesNoRoom() => UiThread.Run(() =>
    {
        var written = Measure([new DiagramCompartment([Row("string", "name")]) { Aligned = true }]);
        var blank = Measure([new DiagramCompartment([new DiagramRow([DiagramShapesTests.Words("string"), null,
                                                                     DiagramShapesTests.Words("name")])])
                             { Aligned = true }]);

        Assert.AreEqual(written.Size.Width, blank.Size.Width, 0.5);
    });

    // ── What it works with ──────────────────────────────────────────────────

    private static DiagramBox Measure(IReadOnlyList<DiagramCompartment> bands) =>
        DiagramBox.Measure(bands, Tall, Pad, Air, Gap, new Size(96, 34));

    private static DiagramCompartment Band(params string[] rows) => new([.. rows.Select(said => Row(said))]);

    private static DiagramRow Row(params string[] columns) =>
        new([.. columns.Select(said => DiagramShapesTests.Words(said))]);
}
