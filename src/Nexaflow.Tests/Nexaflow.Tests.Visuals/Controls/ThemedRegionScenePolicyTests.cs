using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Controls;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Tests.Visuals.Controls;

/// <summary>
/// A theme scene is the shell's only forever-animating surface, so it is the one thing the battery
/// policy switches off. These tests pin the two halves of that: a suppressed region realises no scene
/// at all (rather than pausing one, which would still cost a visual tree), and a region already on
/// screen follows the policy live - which is what makes unplugging the charger take effect without a
/// restart. They also pin what must NOT change: the <c>{Region}.Bg</c> veil is colour, not animation,
/// so it stays either way.
/// <para>Interactive desktop only (WPF elements need an STA thread). Run with
/// <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("theme-scene-battery-policy")]
public class ThemedRegionScenePolicyTests
{
    private const string SceneMarker = "scene-realised";

    /// <summary>
    /// Builds a region whose resources carry both a <c>Scene.Window</c> template and a
    /// <c>Window.Bg</c> veil, templated and laid out so <c>OnApplyTemplate</c> has run.
    /// </summary>
    private static void WithRegion(Action<ThemedRegion> test) => UiThread.Run(() =>
    {
        bool original = BackgroundAnimationPolicy.ScenesEnabled;
        try
        {
            var region = new ThemedRegion { Region = "Window" };
            region.Resources.Add("Scene.Window", SceneTemplate());
            region.Resources.Add("Window.Bg", new SolidColorBrush(Colors.Magenta));

            // Off-screen there is no Application to resolve the implicit theme style, so apply the
            // shipped one by hand - the test then drives the real template, PART names and all.
            region.Style = ShippedStyle();

            region.Measure(new Size(400, 300));
            region.Arrange(new Rect(0, 0, 400, 300));
            region.UpdateLayout();
            region.ApplyTemplate();

            test(region);
        }
        finally
        {
            BackgroundAnimationPolicy.ScenesEnabled = original;
        }
    });

    /// <summary>The real <c>ThemedRegion</c> style from Visuals.Common's generic.xaml.</summary>
    private static Style ShippedStyle()
    {
        var generic = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Nexaflow.Visuals.Common;component/themes/generic.xaml"),
        };
        return (Style)generic[typeof(ThemedRegion)];
    }

    /// <summary>A scene stand-in: any template will do, since the assertion is whether it was realised.</summary>
    private static DataTemplate SceneTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(FrameworkElement.NameProperty, "SceneRoot");
        border.SetValue(Border.BackgroundProperty, Brushes.Black);
        var template = new DataTemplate { VisualTree = border };
        template.Seal();
        return template;
    }

    /// <summary>The scene host inside the control template - realised only when a scene is allowed.</summary>
    private static ContentControl SceneHost(ThemedRegion region)
    {
        var host = FindDescendant<ContentControl>(region, c => c.Name == "PART_Scene");
        Assert.IsNotNull(host, "ThemedRegion's template should expose a PART_Scene ContentControl.");
        return host;
    }

    private static Border Backdrop(ThemedRegion region)
    {
        var backdrop = FindDescendant<Border>(region, b => b.Name == "PART_Backdrop");
        Assert.IsNotNull(backdrop, "ThemedRegion's template should expose a PART_Backdrop Border.");
        return backdrop;
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> match) where T : FrameworkElement
    {
        if (root is T hit && match(hit)) return hit;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
            if (FindDescendant(VisualTreeHelper.GetChild(root, i), match) is { } found)
                return found;
        return null;
    }

    [TestMethod]
    public void ScenesEnabled_RealisesTheThemesSceneTemplate() => WithRegion(region =>
    {
        BackgroundAnimationPolicy.ScenesEnabled = true;

        var host = SceneHost(region);
        Assert.IsNotNull(host.ContentTemplate, "The theme supplies Scene.Window, so it should be applied.");
        Assert.IsNotNull(host.Content, "The scene host needs non-null content for the template to realise.");
    });

    [TestMethod]
    public void ScenesSuppressed_RealisesNoSceneAtAll() => WithRegion(region =>
    {
        BackgroundAnimationPolicy.ScenesEnabled = false;

        var host = SceneHost(region);
        Assert.IsNull(host.Content, "A suppressed scene must not be realised - pausing one still costs a visual tree.");
        Assert.IsNull(host.ContentTemplate, "The scene template should be dropped, not merely emptied of content.");
    });

    [TestMethod]
    public void PolicyFlip_DropsAndRebuildsTheSceneWithoutARestart() => WithRegion(region =>
    {
        BackgroundAnimationPolicy.ScenesEnabled = true;
        Assert.IsNotNull(SceneHost(region).Content, "Baseline: the scene should be up before the policy flips.");

        // Unplugging the charger.
        BackgroundAnimationPolicy.ScenesEnabled = false;
        Assert.IsNull(SceneHost(region).Content, "Going on battery should drop the live scene, not wait for a restart.");

        // Plugging back in.
        BackgroundAnimationPolicy.ScenesEnabled = true;
        Assert.IsNotNull(SceneHost(region).Content, "Back on mains, the scene should come back on its own.");
    });

    [TestMethod]
    public void SuppressingScenes_LeavesTheRegionsColourVeilAlone() => WithRegion(region =>
    {
        BackgroundAnimationPolicy.ScenesEnabled = false;

        var veil = Backdrop(region).Background as SolidColorBrush;
        Assert.AreEqual(Colors.Magenta, veil?.Color,
            "{Region}.Bg is colour, not animation - a theme must keep its tint on battery.");
    });
}
