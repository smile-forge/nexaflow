using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Svg.UI;

/// <summary>
/// One-pass UI journey for the SVG viewer: opens the sample via the explicit <b>"As SVG"</b> ActionStrip
/// button, then works the toolbar — the checkerboard toggle both ways and Reset view — soft-asserting each
/// so one gap doesn't hide the rest.
/// <para>
/// Pan and zoom are not driven here: they are mouse gestures against a live canvas, and the arithmetic
/// behind them is asserted directly in <c>ViewportFitTests</c>.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("svg")]
public class SvgJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    [CoversNode("svg-ui")]
    [CoversNode("svg-reset-view")]
    public void Svg_Controls_RespondInOnePass()
    {
        var view = OpenFileVia(TestSampleData.Path("svg"), "sample.svg", "As SVG", "SvgView");
        Assert.IsNotNull(view, "SvgView did not open via the 'As SVG' action.");

        CheckInvoke("Checkerboard off", "Svg_Checkerboard");
        CheckInvoke("Checkerboard on", "Svg_Checkerboard");
        CheckInvoke("Reset view", "Svg_ResetView");

        AssertJourney();
    }
}
