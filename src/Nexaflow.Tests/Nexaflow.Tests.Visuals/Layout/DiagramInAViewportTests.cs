using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Layout;
using Nexaflow.Visuals.Text.Markdown;
using System.Linq;
using System.Windows;

namespace Nexaflow.Tests.Visuals.Layout;

/// <summary>
/// A graph diagram shown on the shared pan/zoom viewport — how a host shows one too big for the panel it has, which
/// is every import tree the PE inspector draws.
///
/// <para>
/// A graph is laid out as the graph it is, at whatever size that comes to, and it pays no attention to the room it is
/// given: there is no honest way to wrap one into a panel. So the panel stops being what has to hold it, and the
/// viewport fits it instead — which is also what makes opening a module safe, since the graph may double in size
/// and the page still shows all of it.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
public class DiagramInAViewportTests
{
    /// <summary>A root with <paramref name="children"/> children: a graph far wider than any panel it is put in.</summary>
    private static string Fanned(int children) =>
        "```mermaid\ngraph TD\n" +
        string.Concat(Enumerable.Range(0, children).Select(at => $"  root --> c{at}[\"Child {at}\"]\n")) + "```\n";

    private static readonly Size Panel = new(400, 300);

    /// <summary>The diagram on a viewport the size of a modest panel, measured, arranged and fitted.</summary>
    private static (PanZoomSurface Viewport, MarkdownSurface Diagram) Shown(string markdown)
    {
        var diagram = new MarkdownSurface { Markdown = markdown };
        var viewport = new PanZoomSurface { ZoomOnPlainWheel = true, SurfaceContent = diagram };

        viewport.ContentExtent = () =>
            diagram.Shown.Laid.Size is { Width: > 0, Height: > 0 } size
                ? new CanvasBounds(0, 0, size.Width, size.Height)
                : null;

        viewport.Measure(Panel);
        viewport.Arrange(new Rect(Panel));
        viewport.UpdateLayout();
        viewport.RefreshOverview();

        return (viewport, diagram);
    }

    [TestMethod]
    [CoversNode("executable-dependency-viewport")]
    public void ADiagramOnAViewportTakesTheSizeItsOwnLayoutCameTo() => UiThread.Run(() =>
    {
        var (_, diagram) = Shown(Fanned(12));
        var laid = diagram.Shown.Laid.Size;

        Assert.IsTrue(laid.Width > Panel.Width,
                      $"a twelve-way fan is {laid.Width} across, which no {Panel.Width} panel was ever going to hold");
        Assert.AreEqual(laid.Width, diagram.DesiredSize.Width, 1,
                        "and the surface asks for all of it rather than being cut to the panel");
    });

    [TestMethod]
    [CoversNode("executable-dependency-viewport")]
    public void AndTheViewportScalesItToFit() => UiThread.Run(() =>
    {
        var (viewport, diagram) = Shown(Fanned(12));
        var laid = diagram.Shown.Laid.Size;
        var (scale, _, _) = viewport.View;

        Assert.IsTrue(scale is > 0 and < 1, $"it is scaled down to fit, not shown at {scale:P0}");
        Assert.IsTrue(laid.Width * scale <= Panel.Width + 1, $"the whole width is on the page: {laid.Width * scale}");
        Assert.IsTrue(laid.Height * scale <= Panel.Height + 1, $"and the whole height: {laid.Height * scale}");
    });

    [TestMethod]
    [CoversNode("executable-dependency-viewport")]
    public void AndSomethingSmallEnoughIsLeftAtItsOwnSize() => UiThread.Run(() =>
    {
        var (viewport, _) = Shown(Fanned(1));

        Assert.AreEqual(1, viewport.View.Scale, 1e-9,
                        "a two-node graph blown up to fill the panel looks broken rather than helpful");
    });

    [TestMethod]
    [CoversNode("executable-dependency-viewport")]
    public void AndTheWheelZoomsItWithNoModifierHeld() => UiThread.Run(() =>
    {
        var (viewport, _) = Shown(Fanned(12));
        var fitted = viewport.View.Scale;

        viewport.ZoomBy(1.15);

        Assert.IsTrue(viewport.View.Scale > fitted, "the wheel's own step, which is what it does to the view");

        viewport.ZoomBy(1 / 1.15);

        Assert.AreEqual(fitted, viewport.View.Scale, 1e-9, "and back out again lands where it started");
    });

    [TestMethod]
    [CoversNode("executable-dependency-viewport")]
    public void AndTheOverviewIsAPictureOfIt() => UiThread.Run(() =>
    {
        var (_, diagram) = Shown(Fanned(12));
        var laid = diagram.Shown.Laid.Size;

        var picture = diagram.CapturePicture(new Size(168, 112));

        Assert.IsNotNull(picture, "the whole document, painted small — a graph is known by its shape");
        Assert.IsTrue(picture.Width <= 169 && picture.Height <= 113,
                      $"no bigger than it was asked for: {picture.Width} x {picture.Height}");
        Assert.AreEqual(laid.Width / laid.Height, picture.Width / picture.Height, 0.05,
                        "and the same shape as the graph it is a picture of");
    });

    [TestMethod]
    [CoversNode("executable-dependency-viewport")]
    public void AndTheViewportDrawsItsOverviewFromThatPicture() => UiThread.Run(() =>
    {
        var (viewport, diagram) = Shown(Fanned(12));

        var asked = 0;
        viewport.MiniMapPicture = within => { asked++; return diagram.CapturePicture(within); };

        viewport.ZoomBy(1.15);

        Assert.IsTrue(asked > 0, "asked for as the view moves, rather than the overview drawing boxes");
    });

    [TestMethod]
    [CoversNode("executable-dependency-viewport")]
    public void AndRevealingAPartOfItLeavesTheZoomAlone() => UiThread.Run(() =>
    {
        // What opening a module comes to: the reader asked about one part of the graph, not to see the whole of it
        // from further away. So the scale they were at is kept and the view moves the least that shows the part.
        var (viewport, diagram) = Shown(Fanned(12));
        var laid = diagram.Shown.Laid.Size;
        var fitted = viewport.View.Scale;

        // Well off the bottom of what is on screen, as a graph that has just grown downward is.
        var band = new CanvasBounds(0, laid.Height * 4, laid.Width, (laid.Height * 4) + 200);

        viewport.Reveal(band);

        var (scale, _, down) = viewport.View;

        Assert.AreEqual(fitted, scale, 1e-9, "the zoom is the reader's and is not touched");
        Assert.IsTrue((band.MinY * scale) + down >= -1 && (band.MaxY * scale) + down <= Panel.Height + 1,
                      $"and the band is on the page: {(band.MinY * scale) + down}..{(band.MaxY * scale) + down}");
    });

    [TestMethod]
    [CoversNode("executable-dependency-viewport")]
    public void AndRevealingWhatIsAlreadyOnScreenMovesNothing() => UiThread.Run(() =>
    {
        var (viewport, diagram) = Shown(Fanned(12));
        var laid = diagram.Shown.Laid.Size;
        var was = viewport.View;

        viewport.Reveal(new CanvasBounds(0, 0, laid.Width / 4, laid.Height / 4));

        Assert.AreEqual(was, viewport.View, "nothing to do, so nothing is done");
    });
}
