using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Controls;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Tests.Visuals.Controls;

/// <summary>
/// An animated theme scene is the shell's only forever-animating surface, so it is the one thing the
/// battery policy switches off. These tests pin the two halves of that: a suppressed region realises
/// no scene at all (rather than pausing one, which would still cost a visual tree), and a region
/// already on screen follows the policy live - which is what makes unplugging the charger take effect
/// without a restart.
/// <para>They also pin the two things that must NOT change when it does. The <c>{Region}.Bg</c> veil
/// is colour rather than animation, so it stays either way; and a <c>StillScene.{Region}</c> backdrop
/// draws once and then costs what that veil costs, so suppressing it would reclaim nothing and lose
/// the theme its art. Both are the same argument, and the still-scene tests are what stop the gate
/// from quietly widening back over them.</para>
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
    private static void WithRegion(Action<ThemedRegion> test) => WithRegion(test, "Scene.Window");

    /// <summary>As <see cref="WithRegion(Action{ThemedRegion})"/>, but the theme supplies whichever
    /// backdrop keys are named — so one test can set up an animated scene, a still one, or both.</summary>
    private static void WithRegion(Action<ThemedRegion> test, params string[] sceneKeys) => UiThread.Run(() =>
    {
        bool original = BackgroundAnimationPolicy.ScenesEnabled;
        try
        {
            var region = new ThemedRegion { Region = "Window" };
            foreach (var key in sceneKeys)
                region.Resources.Add(key, SceneTemplate());
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

    [TestMethod]
    public void StillScene_SurvivesSceneSuppression() => WithRegion(region =>
    {
        BackgroundAnimationPolicy.ScenesEnabled = false;

        var host = SceneHost(region);
        Assert.IsNotNull(host.ContentTemplate,
            "A StillScene never animates, so suppression would reclaim nothing and cost the theme its art.");
        Assert.IsNotNull(host.Content, "The still backdrop should still be realised on battery.");
    }, "StillScene.Window");

    [TestMethod]
    public void StillScene_IsUnmovedByAPolicyFlipInEitherDirection() => WithRegion(region =>
    {
        BackgroundAnimationPolicy.ScenesEnabled = true;
        var onMains = SceneHost(region).ContentTemplate;
        Assert.IsNotNull(onMains, "Baseline: the still backdrop should be up on mains.");

        BackgroundAnimationPolicy.ScenesEnabled = false;
        Assert.AreSame(onMains, SceneHost(region).ContentTemplate,
            "Unplugging must not disturb a still backdrop - not even by dropping and re-realising it.");

        BackgroundAnimationPolicy.ScenesEnabled = true;
        Assert.AreSame(onMains, SceneHost(region).ContentTemplate, "…nor plugging back in.");
    }, "StillScene.Window");

    /// <summary>
    /// A theme is expected to supply one key or the other, but if both arrive the still one has to win:
    /// it is the only choice that renders in both power states, so preferring the animated one would
    /// make the region go blank on battery despite the theme having shipped a backdrop that cannot.
    /// </summary>
    [TestMethod]
    public void BothKeysPresent_TheStillOneWinsSoTheRegionIsNeverBlank() =>
        WithRegion(region =>
        {
            BackgroundAnimationPolicy.ScenesEnabled = false;
            Assert.IsNotNull(SceneHost(region).Content,
                "With a still backdrop available, suppression must not leave the region with nothing.");
        }, "StillScene.Window", "Scene.Window");
}
