using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Controls;

namespace Nexaflow.Tests.Visuals.Controls;

/// <summary>
/// What UI Automation reports for the picker's swatch buttons once a client has walked them and the picker has then built its
/// swatch list again — it builds one when its prefix is set and another when it loads.
/// <para>Interactive desktop only (WPF elements need an STA thread). Run with <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("vcommon-color-picker")]
public class ColorPickerAutomationTests
{
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

    /// <summary>Every swatch button's peer as a client reaches it: list, then item, then button.</summary>
    private static List<AutomationPeer> SwatchButtonPeers(ColorPicker picker)
    {
        var list = Descendants(picker).OfType<ItemsControl>().First();
        var listPeer = UIElementAutomationPeer.CreatePeerForElement(list);
        return listPeer.GetChildren().SelectMany(item => item.GetChildren() ?? new List<AutomationPeer>()).ToList();
    }

    [TestMethod]
    public void ARebuiltSwatchList_StillReportsWhereItsButtonsAre() => UiThread.Run(() =>
    {
        var picker = new ColorPicker { AutomationPrefix = "Picker" };
        using var source = new HwndSource(new HwndSourceParameters("ColorPickerAutomationTests", 400, 300)) { RootVisual = picker };
        picker.UpdateLayout();

        var before = SwatchButtonPeers(picker);
        Assert.AreNotEqual(0, before.Count, "the swatch buttons are in the tree");

        picker.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        picker.UpdateLayout();
        picker.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new System.Action(() => { }));

        var empty = SwatchButtonPeers(picker).Where(p => p.GetBoundingRectangle().IsEmpty || p.IsOffscreen()).Select(p => p.GetAutomationId()).ToList();
        Assert.AreEqual(0, empty.Count, "buttons reported empty or offscreen: " + string.Join(", ", empty));
    });

    [TestMethod]
    public void TheSwatches_FollowTheThemeWithoutTheListBeingBuiltAgain() => UiThread.Run(() =>
    {
        var picker = new ColorPicker { AutomationPrefix = "Picker" };
        picker.Resources["Swatch.Teal"] = new SolidColorBrush(Colors.Teal);
        using var source = new HwndSource(new HwndSourceParameters("ColorPickerAutomationTests", 400, 300)) { RootVisual = picker };
        picker.UpdateLayout();

        Button Teal() => Descendants(picker).OfType<Button>().First(b => AutomationProperties.GetAutomationId(b) == "Picker_SwatchTeal");
        var list = picker.Swatches;
        Assert.AreEqual(Colors.Teal, ((SolidColorBrush)Teal().Background).Color);

        // A theme switch replaces the bank's brushes; the chips pick them up as they are.
        picker.Resources["Swatch.Teal"] = new SolidColorBrush(Colors.Purple);
        Assert.AreEqual(Colors.Purple, ((SolidColorBrush)Teal().Background).Color, "the chip shows the retuned brush");
        Assert.AreSame(list, picker.Swatches, "and nothing replaced the list to get there");
    });
}
