using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Controls;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Tests.Visuals.Controls;

/// <summary>
/// The zoom chrome the three viewers share. Its preset rows are built in code rather than declared in the
/// markup, because each row's AutomationId has to compose from the host's prefix — so nothing at compile
/// time proves those ids exist or that clicking a row moves the zoom. These tests are that proof, and they
/// pin the exact ids the text viewer's journey already addresses.
/// <para>Interactive desktop only (WPF elements need an STA thread). Run with
/// <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("vcommon-text-zoom")]
public class ZoomChipTests
{
    private static void WithChip(string prefix, System.Action<ZoomChip, TextZoom> test) => UiThread.Run(() =>
    {
        var zoom = new TextZoom();
        var chip = new ZoomChip { PagePrefix = prefix, Zoom = zoom };

        // No window, so force a layout pass — bindings only settle once measured.
        chip.Measure(new Size(400, 100));
        chip.Arrange(new Rect(0, 0, 400, 100));
        chip.UpdateLayout();

        test(chip, zoom);
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

    private static DependencyObject? ById(DependencyObject root, string id) =>
        Descendants(root).FirstOrDefault(d => (string?)d.GetValue(AutomationProperties.AutomationIdProperty) == id);

    /// <summary>The popup's rows are not in the chip's visual tree until it opens, so reach the panel the
    /// code-behind fills by name rather than by walking the tree.</summary>
    private static IEnumerable<Button> PresetRows(ZoomChip chip)
        => ((Panel)chip.FindName("PresetPanel")!).Children.OfType<Button>();

    [TestMethod]
    public void TheLabelsAutomationId_ComposesFromThePagePrefix() => WithChip("Text", (chip, _) =>
    {
        // The exact id TextJourneyTests already clicks — it must survive the chip being shared.
        Assert.AreEqual("Text_ZoomLabel", chip.AutomationIdLabel);
        Assert.IsNotNull(ById(chip, "Text_ZoomLabel"), "no element carries Text_ZoomLabel");
    });

    [TestMethod]
    public void ChangingThePrefix_RecomposesTheIds() => WithChip("Text", (chip, _) =>
    {
        chip.PagePrefix = "Markdown";
        Assert.AreEqual("Markdown_ZoomLabel", chip.AutomationIdLabel);
    });

    [TestMethod]
    public void EveryPreset_GetsAPrefixedId() => WithChip("Hex", (chip, zoom) =>
    {
        var rows = PresetRows(chip).ToList();
        CollectionAssert.AreEqual(
            zoom.Presets.Select(p => $"Hex_Zoom{p}").ToList(),
            rows.Select(AutomationProperties.GetAutomationId).ToList(),
            "one row per preset, each carrying the host's prefix");
    });

    [TestMethod]
    public void ClickingAPreset_SetsTheZoom() => WithChip("Text", (chip, zoom) =>
    {
        var row = PresetRows(chip).Single(b => AutomationProperties.GetAutomationId(b) == "Text_Zoom120");
        row.RaiseEvent(new RoutedEventArgs(ButtonBase_Click));
        Assert.AreEqual(120, zoom.Percent);
    });

    private static readonly RoutedEvent ButtonBase_Click =
        System.Windows.Controls.Primitives.ButtonBase.ClickEvent;

    [TestMethod]
    public void TheLabelShowsThePercentage_AndTracksIt() => WithChip("Text", (chip, zoom) =>
    {
        var label = (TextBlock)ById(chip, "Text_ZoomLabel")!;
        Assert.AreEqual("100%", label.Text);

        zoom.Percent = 130;
        chip.UpdateLayout();
        // The journey reads this text to confirm a zoom applied, so the format is a contract.
        Assert.AreEqual("130%", label.Text);
    });
}
