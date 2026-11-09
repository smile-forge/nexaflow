using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Controls;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Tests.Visuals.Controls;

/// <summary>
/// A window set aside hidden — the standby a resident app keeps ready — draws nothing, so its animated scene is dropped
/// as on battery rather than left animating unseen, and comes back when the window is shown. Unlike
/// <see cref="ThemedRegionScenePolicyTests"/>, which lays a region out off-screen, this needs a real window to hide and
/// show. Run with <c>--filter "TestCategory=Desktop"</c>.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("theme-scene-battery-policy")]
public class ThemedRegionHiddenWindowTests
{
    [TestMethod]
    public void AHiddenWindow_DropsItsAnimatedScene_AndShowingItBringsItBack() => UiThread.Run(() =>
    {
        bool original = BackgroundAnimationPolicy.ScenesEnabled;
        var region = new ThemedRegion { Region = "Window", Style = ThemedRegionScenePolicyTests.ShippedStyle() };
        region.Resources.Add("Scene.Window", ThemedRegionScenePolicyTests.SceneTemplate());
        var window = new Window
        {
            Content = region, Width = 400, Height = 300, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000,
        };
        try
        {
            BackgroundAnimationPolicy.ScenesEnabled = true;
            window.Show();
            window.UpdateLayout();
            Assert.IsNotNull(ThemedRegionScenePolicyTests.SceneHost(region).Content, "Baseline: shown, the scene is up.");

            window.Hide();
            Assert.IsNull(ThemedRegionScenePolicyTests.SceneHost(region).Content,
                "Hidden, nothing of it draws, so nothing of it should animate.");

            window.Show();
            Assert.IsNotNull(ThemedRegionScenePolicyTests.SceneHost(region).Content, "Shown again, the scene comes back on its own.");
        }
        finally
        {
            window.Close();
            BackgroundAnimationPolicy.ScenesEnabled = original;
        }
    });
}
