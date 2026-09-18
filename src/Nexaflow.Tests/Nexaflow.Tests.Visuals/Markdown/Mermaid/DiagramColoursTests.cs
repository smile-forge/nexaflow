using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A run of colours a number is read along, and the bar that explains one.
///
/// <para>
/// What these hold is that a run stays a run: ordered from one end to the other, with nothing in the
/// middle that reads as further along than an end. That is the whole reason the ramps here are the ones
/// designed for it rather than any two colours somebody liked.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("mermaid-diagram-kit")]
public class DiagramColoursTests
{
    private static readonly DiagramRamp[] Ramps = Enum.GetValues<DiagramRamp>();

    [TestMethod]
    public void EveryRampRunsFromOneEndToTheOther()
    {
        foreach (var ramp in Ramps)
            Assert.AreNotEqual(DiagramColours.At(ramp, 0), DiagramColours.At(ramp, 1),
                               $"{ramp} ends where it started, so nothing along it means anything");
    }

    [TestMethod]
    public void EveryRampGetsSteadilyLighterOrSteadilyDarkerUnlessItDiverges()
    {
        // The property that makes a run readable as a quantity: no two shares the same brightness, so it
        // still reads in order when it is printed grey.
        foreach (var ramp in Ramps)
        {
            if (DiagramColours.Diverges(ramp)) continue;

            var was = Grey(DiagramColours.At(ramp, 0));
            var up = Grey(DiagramColours.At(ramp, 1)) > was;

            for (var share = 0.1; share <= 1.0001; share += 0.1)
            {
                var now = Grey(DiagramColours.At(ramp, share));

                Assert.IsTrue(up ? now > was - 0.01 : now < was + 0.01,
                              $"{ramp} turns back on itself at {share:0.0}");

                was = now;
            }
        }
    }

    [TestMethod]
    public void ADivergingRampIsPalestInTheMiddle()
    {
        // Which is what makes the middle value read as the middle rather than as an end.
        var ramp = DiagramRamp.BlueRed;

        Assert.IsTrue(Grey(DiagramColours.At(ramp, 0.5)) > Grey(DiagramColours.At(ramp, 0)));
        Assert.IsTrue(Grey(DiagramColours.At(ramp, 0.5)) > Grey(DiagramColours.At(ramp, 1)));
    }

    [TestMethod]
    public void ARampWithALowEndAndAHighOneIsNotTurnedAbout()
    {
        // A correlation read blue for negative and red for positive, which is the way round anybody
        // expects it.
        Assert.IsTrue(DiagramColours.At(DiagramRamp.BlueRed, 0).B > DiagramColours.At(DiagramRamp.BlueRed, 0).R);
        Assert.IsTrue(DiagramColours.At(DiagramRamp.BlueRed, 1).R > DiagramColours.At(DiagramRamp.BlueRed, 1).B);
    }

    [TestMethod]
    public void AShareOutsideNoughtToOneIsHeldAtTheEnd()
    {
        foreach (var ramp in Ramps)
        {
            Assert.AreEqual(DiagramColours.At(ramp, 0), DiagramColours.At(ramp, -2));
            Assert.AreEqual(DiagramColours.At(ramp, 1), DiagramColours.At(ramp, 5));
        }
    }

    [TestMethod]
    public void ColoursWrittenOutAreReadBetween()
    {
        Color[] stops = [Colors.Black, Colors.White];

        Assert.AreEqual(Colors.Black, DiagramColours.At(stops, 0));
        Assert.AreEqual(Colors.White, DiagramColours.At(stops, 1));
        Assert.AreEqual(128, DiagramColours.At(stops, 0.5).R, 1);
    }

    [TestMethod]
    public void ThreeColoursPutTheMiddleOneInTheMiddle()
    {
        Color[] stops = [Colors.Blue, Colors.White, Colors.Red];

        Assert.AreEqual(Colors.White, DiagramColours.At(stops, 0.5));
    }

    [TestMethod]
    public void EveryRampCanBeNamed()
    {
        foreach (var ramp in Ramps)
            Assert.AreEqual(ramp, DiagramColours.Named(ramp.ToString()),
                            $"{ramp} cannot be asked for by its own name");

        Assert.AreEqual(DiagramRamp.BlueRed, DiagramColours.Named("rdbu"));
        Assert.IsNull(DiagramColours.Named("sunburst"));
    }

    // ── The bar ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void ABarIsTheColoursWithItsNumbersBeside() => UiThread.Run(() =>
    {
        var bar = Bar(out var laid);

        Assert.IsTrue(bar.Size.Width > DiagramLegend.SwatchSize, "the numbers are beside the bar");
        Assert.AreEqual(3, laid.Root.SelfAndDescendants().Count(piece => piece.Words is not null));
    });

    [TestMethod]
    public void ABarStandsForNothingAnybodyWrote() => UiThread.Run(() =>
    {
        // Nobody typed a run of colours, so there is nowhere in one for a caret to go.
        Bar(out var laid);

        foreach (var piece in laid.Root.SelfAndDescendants())
            Assert.IsNull(piece.Part, $"{piece.Kind} was drawn from something nobody wrote");
    });

    private static DiagramBar Bar(out Laid laid)
    {
        // The same words a shape is made to the size of, which is the one maker these tests share.
        var bar = new DiagramBar(DiagramColours.Stops(DiagramRamp.Viridis),
                                 [(0, DiagramShapesTests.Words("0")),
                                  (0.5, DiagramShapesTests.Words("50")),
                                  (1, DiagramShapesTests.Words("100"))],
                                 Brushes.Gray);

        var build = new LayoutBuilder();
        build.Open("Root");
        bar.Draw(build, new Point(0, 0));
        build.Close();

        laid = new Laid(build.Seal(), bar.Size, []);
        return bar;
    }

    private static double Grey(Color colour) =>
        ((0.2126 * colour.R) + (0.7152 * colour.G) + (0.0722 * colour.B)) / 255;
}
