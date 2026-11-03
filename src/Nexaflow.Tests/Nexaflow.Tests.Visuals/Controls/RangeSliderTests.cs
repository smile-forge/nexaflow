using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Controls;

namespace Nexaflow.Tests.Visuals.Controls;

/// <summary>
/// A slider over a span: its ends stay in order where they are moved and in bounds however they are set — a span set from
/// outside arriving whole — and each end is a slider of its own to UI Automation under an id composed from the control's,
/// which nothing at compile time proves and which journeys address.
/// <para>Interactive desktop only (WPF elements need an STA thread). Run with <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("vcommon-range-slider")]
public class RangeSliderTests
{
    private static void WithSlider(System.Action<RangeSlider> test) => UiThread.Run(() =>
    {
        // No application, so no theme lookup: the shipped style is read from Visuals.Common's generic.xaml.
        var generic = (ResourceDictionary)Application.LoadComponent(new System.Uri("/Nexaflow.Visuals.Common;component/themes/generic.xaml", System.UriKind.Relative));
        var slider = new RangeSlider { Style = (Style)generic[typeof(RangeSlider)], Width = 200, Minimum = 0, Maximum = 100, LowerValue = 20, UpperValue = 60 };
        AutomationProperties.SetAutomationId(slider, "Band");
        // No window, so force a layout pass — the template is applied once measured.
        slider.Measure(new Size(200, 24));
        slider.Arrange(new Rect(0, 0, 200, 24));
        slider.UpdateLayout();
        test(slider);
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    [TestMethod]
    public void ItsEnds_StayInOrderAndInBounds_HoweverTheyAreSet() => WithSlider(slider =>
    {
        slider.SetEnd(RangeEnd.Lower, 80);
        Assert.AreEqual(60, slider.LowerValue, "moved here, the lower end is held at the upper");
        slider.SetEnd(RangeEnd.Upper, -10);
        Assert.AreEqual(60, slider.UpperValue, "and the upper at the lower");
        slider.UpperValue = 150;
        Assert.AreEqual(100, slider.UpperValue, "set from outside, an end is held to the bounds");

        // A host moving the whole span sets one end before the other: neither is held to where the other was.
        slider.LowerValue = 10;
        slider.UpperValue = 30;
        slider.LowerValue = 70;
        Assert.AreEqual(70, slider.LowerValue, "the lower end set past the upper is taken as given");
        slider.UpperValue = 90;
        Assert.AreEqual((70.0, 90.0), (slider.LowerValue, slider.UpperValue), "and the span arrives whole");
    });

    [TestMethod]
    public void EachEnd_IsASliderToUiAutomation_UnderTheControlsIdWithLowerOrUpper() => WithSlider(slider =>
    {
        var ends = Descendants(slider).OfType<RangeThumb>().ToList();
        Assert.AreEqual(2, ends.Count, "two ends");
        foreach (var (id, end, to) in new[] { ("BandLower", RangeEnd.Lower, 35.0), ("BandUpper", RangeEnd.Upper, 75.0) })
        {
            var peer = ends.Select(UIElementAutomationPeer.CreatePeerForElement).Single(p => p.GetAutomationId() == id);
            Assert.AreEqual(AutomationControlType.Slider, peer.GetAutomationControlType(), $"{id} reads as a slider");
            var range = (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue);
            Assert.AreEqual(slider.ValueOf(end), range.Value);
            range.SetValue(to);
            Assert.AreEqual(to, slider.ValueOf(end), $"setting {id} moves its end");
            Assert.AreEqual((0.0, 100.0), (range.Minimum, range.Maximum));
        }
    });
}
