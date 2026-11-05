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
    public void ANarrowBand_HasItsEndsStepAside_SoEachEndAndTheBandCanBeTakenHoldOf() => WithSlider(slider =>
    {
        var lower = (FrameworkElement)slider.Template.FindName(RangeSlider.LowerPart, slider);
        var upper = (FrameworkElement)slider.Template.FindName(RangeSlider.UpperPart, slider);
        var band = (FrameworkElement)slider.Template.FindName(RangeSlider.SpanPart, slider);
        double end = lower.Width;

        double Left(FrameworkElement e) => System.Windows.Controls.Canvas.GetLeft(e);
        void Settle() => slider.UpdateLayout();

        // A wide band: each end on its own value, the band between them.
        Settle();
        double wideGap = Left(upper) - Left(lower);
        Assert.IsTrue(wideGap > end, "a wide band's ends lie apart");

        // Narrowed to a single unit: the ends step aside rather than over each other, and the band keeps room to be dragged.
        slider.LowerValue = 50;
        slider.UpperValue = 51;
        Settle();
        Assert.IsTrue(Left(upper) >= Left(lower) + end - 1e-6, "the ends no longer lie over each other");
        Assert.IsTrue(band.Width >= end / 2 - 1e-6, "the band between them is half an end wide at least");
        Assert.IsTrue(Left(band) >= Left(lower) + end - 1e-6 && Left(band) + band.Width <= Left(upper) + 1e-6, "and lies between them, uncovered");

        // At a bound, the pair stays on the rail.
        slider.LowerValue = 0;
        slider.UpperValue = 1;
        Settle();
        Assert.IsTrue(Left(lower) >= -1e-6, "the lower end does not step off the rail's start");
        slider.LowerValue = 99;
        slider.UpperValue = 100;
        Settle();
        Assert.IsTrue(Left(upper) + end <= slider.ActualWidth + 1e-6, "nor the upper off its end");
    });

    /// <summary>A host holding its band as the lens blur does: in whole steps of a hundredth, and never narrower than one.</summary>
    private sealed class SteppedBand : System.ComponentModel.INotifyPropertyChanged
    {
        private double _near = 0.2, _far = 0.6;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public double Near
        {
            get => _near * 100;
            set => Banded(System.Math.Round(value) / 100, _far);
        }

        public double Far
        {
            get => _far * 100;
            set => Banded(_near, System.Math.Round(value) / 100);
        }

        private void Banded(double near, double far)
        {
            double from = System.Math.Min(near, far), to = System.Math.Max(near, far);
            if (to - from < 0.01)
            {
                double middle = System.Math.Clamp((from + to) / 2, 0.005, 0.995);
                (from, to) = (middle - 0.005, middle + 0.005);
            }
            (_near, _far) = (from, to);
            PropertyChanged?.Invoke(this, new(nameof(Near)));
            PropertyChanged?.Invoke(this, new(nameof(Far)));
        }
    }

    [TestMethod]
    public void EachEnd_SetThroughUiAutomation_ReachesAHostThatHoldsTheBandInSteps() => WithSlider(slider =>
    {
        var band = new SteppedBand();
        slider.DataContext = band;
        System.Windows.Data.BindingOperations.SetBinding(slider, RangeSlider.LowerValueProperty, new System.Windows.Data.Binding(nameof(SteppedBand.Near)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        System.Windows.Data.BindingOperations.SetBinding(slider, RangeSlider.UpperValueProperty, new System.Windows.Data.Binding(nameof(SteppedBand.Far)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        slider.UpdateLayout();

        IRangeValueProvider End(string part) => (IRangeValueProvider)UIElementAutomationPeer.CreatePeerForElement((UIElement)slider.Template.FindName(part, slider))
                                                                         .GetPattern(PatternInterface.RangeValue);
        foreach (var (part, to) in new[] { (RangeSlider.UpperPart, 22.0), (RangeSlider.LowerPart, 21.0), (RangeSlider.UpperPart, 40.0) })
        {
            End(part).SetValue(to);
            Assert.AreEqual(to, End(part).Value, 0.01, $"{part} set to {to}: the slider stands at {slider.LowerValue}–{slider.UpperValue}, the host at {band.Near}–{band.Far}");
        }
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
