using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using Nexaflow.Core;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Guards the two things a new theme silently gets wrong.
/// <list type="bullet">
///   <item>A palette missing one of the layer-1 contract keys only fails where that key is bound,
///         which can be a view nobody opens for weeks.</item>
///   <item><c>ThemeManager.Load</c> treats a <c>Theme.&lt;name&gt;.xaml</c> that throws exactly like
///         one that isn't there — the layer is optional, so both come back null. A malformed overrides
///         file therefore ships as "that theme has no overrides", losing its region tints and, for an
///         immersive theme, its whole backdrop, with nothing logged.</item>
/// </list>
/// <para>
/// Read as XML from the source tree rather than through <c>pack://</c>, which needs an
/// <c>Application</c> to resolve — and one created on a short-lived STA thread would leave
/// <c>Application.Current</c> holding a dead dispatcher for every other test in the process. Every
/// question asked here is about the shape of a declaration, which the markup answers on its own.
/// </para>
/// </summary>
[TestClass]
[CoversNode("themes")]
public class ThemeLayerContractTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Layer 1's contract: the key, and the element every theme must declare it as.</summary>
    private static readonly (string Key, string Element)[] BasePalette =
    [
        ("BgColor",          "Color"), ("SurfaceColor",     "Color"),
        ("Surface2Color",    "Color"), ("BorderColor",      "Color"),
        ("BorderLightColor", "Color"), ("AccentColor",      "Color"),
        ("Accent2Color",     "Color"), ("TextColor",        "Color"),
        ("TextMutedColor",   "Color"), ("DeepBgColor",      "Color"),

        ("BgBrush",          "SolidColorBrush"), ("SurfaceBrush",     "SolidColorBrush"),
        ("Surface2Brush",    "SolidColorBrush"), ("BorderBrush",      "SolidColorBrush"),
        ("BorderLightBrush", "SolidColorBrush"), ("AccentBrush",      "SolidColorBrush"),
        ("Accent2Brush",     "SolidColorBrush"), ("TextBrush",        "SolidColorBrush"),
        ("TextMutedBrush",   "SolidColorBrush"), ("DeepBgBrush",      "SolidColorBrush"),

        ("AccentGradientBrush", "LinearGradientBrush"), ("BarGradientBrush", "LinearGradientBrush"),

        ("TopBarHeight",      "GridLength"), ("TabBarHeight", "GridLength"),
        ("InteractionHeight", "GridLength"),
    ];

    [TestMethod]
    public void EveryTheme_SuppliesTheWholeBasePalette()
    {
        foreach (var theme in Enum.GetValues<ThemeOption>())
        {
            string path = ThemeFile($"Colors.{theme}.xaml");
            Assert.IsTrue(File.Exists(path), $"{theme} is in ThemeOption but ships no palette ({path})");

            var declared = KeyedElements(XDocument.Load(path));
            foreach (var (key, element) in BasePalette)
            {
                Assert.IsTrue(declared.TryGetValue(key, out var actual),
                    $"Colors.{theme}.xaml does not declare {key}");
                Assert.AreEqual(element, actual,
                    $"Colors.{theme}.xaml declares {key} as <{actual}>, not <{element}>");
            }
        }
    }

    /// <summary>
    /// Every overrides layer present must be well-formed, because a broken one is indistinguishable
    /// from an absent one at runtime. <see cref="XDocument.Load(string)"/> throwing IS the assertion.
    /// </summary>
    [TestMethod]
    public void EveryOverridesLayer_IsWellFormed()
    {
        var present = Enum.GetValues<ThemeOption>()
            .Where(t => File.Exists(ThemeFile($"Theme.{t}.xaml")))
            .ToList();

        // Ocean is the reference immersive theme; without this the test would pass by having found
        // nothing to check, were the walk to the source tree ever to stop landing.
        CollectionAssert.Contains(present, ThemeOption.Ocean, "no overrides layers found at all");

        foreach (var theme in present)
        {
            var keys = KeyedElements(XDocument.Load(ThemeFile($"Theme.{theme}.xaml")));

            if (keys.TryGetValue("Scene.Window", out var element))
                Assert.AreEqual("DataTemplate", element,
                    $"Theme.{theme}.xaml declares Scene.Window as <{element}> — ThemedRegion realises "
                    + "nothing but a DataTemplate, so the backdrop would silently never draw");
        }
    }

    /// <summary>
    /// The immersive themes are the ones whose whole point is a backdrop, and it reaches the shell
    /// through exactly one key. Named individually because "has a Scene.Window" is what makes each of
    /// them immersive rather than plain — a theme quietly losing the key still loads and still themes.
    /// </summary>
    [DataTestMethod]
    [DataRow(ThemeOption.Arctic)]
    [DataRow(ThemeOption.Ocean)]
    [DataRow(ThemeOption.Sunny)]
    [DataRow(ThemeOption.Nature)]
    [DataRow(ThemeOption.Sandstone)]
    [DataRow(ThemeOption.Gothic)]
    public void ImmersiveTheme_SuppliesAWindowScene(ThemeOption theme)
    {
        string path = ThemeFile($"Theme.{theme}.xaml");
        Assert.IsTrue(File.Exists(path), $"{theme} is immersive but ships no overrides layer");
        Assert.IsTrue(KeyedElements(XDocument.Load(path)).ContainsKey("Scene.Window"),
                      $"Theme.{theme}.xaml supplies no Scene.Window — the theme would render flat");
    }

    /// <summary>Every <c>x:Key</c>'d element in a dictionary, mapped to its element name.</summary>
    private static Dictionary<string, string> KeyedElements(XDocument doc) =>
        doc.Root!.Elements()
            .Where(e => e.Attribute(Xaml + "Key") is not null)
            .ToDictionary(e => e.Attribute(Xaml + "Key")!.Value, e => e.Name.LocalName);

    private static string ThemeFile(string name) =>
        Path.Combine(RepoRoot(), "src", "Nexaflow.Core", "Themes", name);

    /// <summary>Walks up from the test binary to the folder holding <c>Nexaflow.slnx</c>.</summary>
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Nexaflow.slnx")))
                return dir.FullName;

        throw new InvalidOperationException(
            $"Could not locate the repo root (no Nexaflow.slnx above '{AppContext.BaseDirectory}').");
    }
}
