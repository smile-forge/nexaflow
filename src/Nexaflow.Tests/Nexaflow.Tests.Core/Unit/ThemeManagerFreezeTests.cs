using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Nexaflow.Core;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Verifies <see cref="ThemeManager.FreezeResources"/> freezes theme Freezables across the merged
/// layers — including a token brush whose colour is a cross-dictionary <c>{StaticResource}</c> into
/// the palette (the case that forces freezing to run post-merge: the brush can't resolve until its
/// palette sibling is present). Frozen brushes are immutable, so they're safe to share across UI
/// threads. Mirrors the real palette → tokens layering with inline dictionaries (no Application /
/// pack URIs needed). UI category: WPF resources are built on an STA thread.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("theme-freeze-on-merge")]
public class ThemeManagerFreezeTests
{
    // Palette (a merged child) defines the colour + a same-dict brush; the token-style brush at the
    // root resolves its colour from the palette ACROSS the dictionary boundary — the cross-layer case
    // FreezeResources must handle. One parse, so the {StaticResource} resolves (the real app's
    // BAML-loaded dictionaries defer resolution; XamlReader resolves eagerly, hence the single merge).
    private const string Xaml =
        """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary>
                    <Color x:Key="DeepBgColor">#080C16</Color>
                    <SolidColorBrush x:Key="SurfaceBrush" Color="{StaticResource DeepBgColor}"/>
                </ResourceDictionary>
            </ResourceDictionary.MergedDictionaries>
            <SolidColorBrush x:Key="Chrome.Deep" Color="{StaticResource DeepBgColor}"/>
        </ResourceDictionary>
        """;

    [TestMethod]
    public void FreezeResources_FreezesAcrossMergedLayers() => UiThread.Run(() =>
    {
        var root = (ResourceDictionary)XamlReader.Parse(Xaml);

        // Baseline: XAML brushes start unfrozen (the cross-layer token resolved against the palette).
        Assert.IsFalse(((Freezable)root["SurfaceBrush"]).IsFrozen);
        Assert.IsFalse(((Freezable)root["Chrome.Deep"]).IsFrozen);

        ThemeManager.FreezeResources(root);

        // Same-layer palette brush is frozen.
        Assert.IsTrue(((SolidColorBrush)root["SurfaceBrush"]).IsFrozen, "same-layer brush not frozen");

        // Cross-layer token brush is frozen, with its colour resolved from the palette sibling.
        var token = (SolidColorBrush)root["Chrome.Deep"];
        Assert.IsTrue(token.IsFrozen, "cross-layer token brush not frozen");
        Assert.AreEqual(Color.FromRgb(0x08, 0x0C, 0x16), token.Color);

        // Non-Freezable entries (the raw Color) are left untouched and don't trip the walk.
        Assert.IsInstanceOfType(root["DeepBgColor"], typeof(Color));

        // Idempotent: a second pass over already-frozen resources is a no-op, not a throw.
        ThemeManager.FreezeResources(root);
        Assert.IsTrue(((SolidColorBrush)root["Chrome.Deep"]).IsFrozen);
    });
}
